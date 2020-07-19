﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Clowd.Interop;
using Clowd.Utilities;
using Clowd.Interop.DwmApi;
using Clowd.Interop.Shcore;
using CS.Wpf;
using ScreenVersusWpf;
using System.Drawing;
using System.ComponentModel;
using System.Threading.Tasks;
using Clowd.Interop.Com;
using System.Threading;
using System.Diagnostics;

namespace Clowd.Utilities
{
    public class WindowFinder2 : IDisposable, INotifyPropertyChanged
    {
        public bool MetadataReady
        {
            get => _metadataReady;
            set
            {
                _metadataReady = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MetadataReady)));
            }
        }

        public bool BitmapsReady
        {
            get => _bitmapsReady;
            set
            {
                _bitmapsReady = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BitmapsReady)));
            }
        }

        private bool _metadataReady = false;
        private bool _bitmapsReady = false;

        private const int MaxWindowDepthToSearch = 4;
        private const int MinWinCaptureBounds = 200;

        // this list will be ordered by topmost window handles first, so it can be enumerated easily to find the topmost window at a specific point
        // sub-handles (child windows) will come before their parents, but the Depth property can be used to find the top level window
        private readonly List<CachedWindow> _cachedWindows = new List<CachedWindow>();
        private readonly HashSet<IntPtr> _hWndsAlreadyProcessed = new HashSet<IntPtr>();
        private readonly Stack<CachedWindow> _parentStack = new Stack<CachedWindow>();
        private readonly IVirtualDesktopManager _virtualDesktop = VirtualDesktopManager.CreateNew();
        private readonly int _myProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;

        public event PropertyChangedEventHandler PropertyChanged;

        ~WindowFinder2()
        {
            this.Dispose();
        }
        private WindowFinder2()
        {
        }

        public static WindowFinder2 NewCapture()
        {
            var ths = new WindowFinder2();

            ths._cachedWindows.Clear();
            USER32.EnumWindowProc enumWindowsProc = new USER32.EnumWindowProc(ths.EvalWindow);
            USER32.EnumWindows(enumWindowsProc, IntPtr.Zero);
            ths._hWndsAlreadyProcessed.Clear();
            ths.CalculateVisibilityMetadata();
            ths.MetadataReady = true;

            return ths;
        }

        public void PopulateWindowBitmaps_Async()
        {
            Task.Factory.StartNew(() =>
            {
                PopulateWindowBitmaps_Sync();
                BitmapsReady = true;
            }, TaskCreationOptions.LongRunning);
        }

        public CachedWindow GetWindowThatContainsPoint(ScreenPoint point)
        {
            if (!MetadataReady)
                return null;

            foreach (var window in _cachedWindows)
                if (window.ImageBoundsRect.Contains(point))
                    return window;

            return null;
        }
        public CachedWindow GetTopLevelWindow(CachedWindow child)
        {
            if (child.Parent != null) return GetTopLevelWindow(child.Parent);
            return child;
        }
        public void Dispose()
        {
            _cachedWindows.ForEach(c => c.Dispose());
        }

        private bool EvalWindow(IntPtr hWnd, IntPtr depthPtr)
        {
            var depthInt = depthPtr.ToInt32();
            var parent = depthInt > 0 ? _parentStack.Peek() : null;

            var winInfo = new WINDOWINFO(true);
            if (!USER32.GetWindowInfo(hWnd, ref winInfo))
                throw new Win32Exception();

            var caption = USER32EX.GetWindowCaption(hWnd);
            var asclass = USER32EX.GetWindowClassName(hWnd);

            var winVisible = winInfo.dwStyle.HasFlag(WindowStyles.WS_VISIBLE);
            var winMinimized = winInfo.dwStyle.HasFlag(WindowStyles.WS_MINIMIZE);
            var winMaximized = winInfo.dwStyle.HasFlag(WindowStyles.WS_MAXIMIZE);

            if (!this._hWndsAlreadyProcessed.Contains(hWnd) && winVisible && !winMinimized)
            {
                // skip capture windows created by this process
                USER32.GetWindowThreadProcessId(hWnd, out var processId);
                if (processId == _myProcessId)
                {
                    var window = System.Windows.Interop.HwndSource.FromHwnd(hWnd)?.RootVisual;
                    // ignore WPF debugging windows created by VS
                    if (window != null && window.GetType().AssemblyQualifiedName.Contains("Microsoft.VisualStudio.DesignTools"))
                        return true;
                    // ignore my own fullscreen windows
                    if (window is CaptureWindow || window is VideoOverlayWindow || window is CaptureWindow2)
                        return true;
                }

                if (depthInt == 0)
                {
                    if (!_virtualDesktop.IsWindowOnCurrentVirtualDesktop(hWnd))
                    {
                        // if the window is not on the current virtual desktop we want to ignore it and it's children
                        return true;
                    }

                    var winIsTool = winInfo.dwExStyle.HasFlag(WindowStylesEx.WS_EX_TOOLWINDOW);
                    var winIsLayered = winInfo.dwExStyle.HasFlag(WindowStylesEx.WS_EX_LAYERED);
                    var winIsTopMost = winInfo.dwExStyle.HasFlag(WindowStylesEx.WS_EX_TOPMOST);

                    // if this is a top-level layered tool window, it's probably a transparent overlay and we don't want to capture it
                    if (winIsTool && winIsLayered && winIsTopMost)
                        return true;
                }

                ScreenRect windowRect = ScreenRect.FromSystem(USER32EX.GetTrueWindowBounds(hWnd));
                ScreenRect boundsOfScreenWorkspace = windowRect;

                string className;
                if (this.IsMetroOrPhantomWindow((IntPtr)hWnd, depthInt, out className))
                    return true;

                // clip to workspace if top level maximized window
                if (depthInt == 0 && winMaximized)
                    boundsOfScreenWorkspace = ScreenTools.GetScreenContaining(boundsOfScreenWorkspace).WorkingArea;

                // clip to parent window
                if (parent != null)
                    boundsOfScreenWorkspace = boundsOfScreenWorkspace.Intersect(parent.ImageBoundsRect);

                // clip to screen
                boundsOfScreenWorkspace = ScreenTools.VirtualScreen.Bounds.Intersect(boundsOfScreenWorkspace);

                if (this.BoundsAreLargeEnoughForCapture(boundsOfScreenWorkspace, depthInt))
                {
                    var cwin = new CachedWindow(hWnd, depthInt, className, caption, windowRect, boundsOfScreenWorkspace, parent);

                    if (depthInt < MaxWindowDepthToSearch)
                    {
                        this._parentStack.Push(cwin);
                        USER32.EnumWindowProc enumWindowsProc = new USER32.EnumWindowProc(this.EvalWindow);
                        USER32.EnumChildWindows(hWnd, enumWindowsProc, IntPtr.Add(depthPtr, 1));
                        this._parentStack.Pop();
                    }

                    this._cachedWindows.Add(cwin);
                    this._hWndsAlreadyProcessed.Add(hWnd);
                }
            }
            return true;
        }
        private bool IsMetroOrPhantomWindow(IntPtr hWnd, int depth, out string className)
        {
            StringBuilder stringBuilder = new StringBuilder(255);
            USER32.GetClassName(hWnd, stringBuilder, stringBuilder.Capacity);
            string str = stringBuilder.ToString();
            className = str;
            if (str != null)
            {
                switch (str)
                {
                    case "WorkerW":
                    case "Progman":
                    case "SHELLDLL_DefView": // windows desktop
                    case "SysListView32": // windows desktop
                    case "Intermediate D3D Window": // Chrome / Chromium intermediate window
                    case "CEF-OSC-WIDGET": // NVIDIA GeForce Overlay
                    case "EdgeUiInputWndClass":
                    case "TaskListThumbnailWnd":
                    case "LauncherTipWndClass":
                    case "SearchPane":
                    case "ImmersiveLauncher":
                    case "Touch Tooltip Window":
                    case "Windows.UI.Core.CoreWindow":
                    case "Immersive Chrome Container":
                    case "ImmersiveBackgroundWindow":
                    case "NativeHWNDHost":
                    case "Snapped Desktop":
                    case "ModeInputWnd":
                    case "MetroGhostWindow":
                    case "Shell_Dim":
                    case "Shell_Dialog":
                    case "ApplicationManager_ImmersiveShellWindow":
                        {
                            return true;
                        }
                }
                //check if this is a windows 10 phantom metro window.
                //if it has the children ApplicationFrameInputSinkWindow and ApplicationFrameTitleBarWindow
                //and not Windows.UI.Core.CoreWindow, it is not a real window.
                if (str == "ApplicationFrameWindow" && depth == 0)
                {
                    var children = USER32EX.GetChildWindows(hWnd)
                        .Select(child => new
                        {
                            Handle = child,
                            Class = USER32EX.GetWindowClassName(child)
                        });
                    if (children.Any(child => child.Class == "ApplicationFrameInputSinkWindow") &&
                        children.Any(child => child.Class == "ApplicationFrameTitleBarWindow"))
                    {
                        if (!children.Any(child => child.Class == "Windows.UI.Core.CoreWindow"))
                            return true;
                    }
                }
            }
            return false;
        }
        private bool BoundsAreLargeEnoughForCapture(ScreenRect windowBounds, int depth)
        {
            int minSize = (depth == 0) ? 25 : MinWinCaptureBounds;

            if (windowBounds.Height <= minSize)
                return false;
            return windowBounds.Width > minSize;
        }
        private void CalculateVisibilityMetadata()
        {
            Region screen = new Region(ScreenTools.VirtualScreen.Bounds.ToSystem());
            for (int i = 0; i < _cachedWindows.Count; i++)
            {
                var w = _cachedWindows[i];

                var rect = w.WindowRect.ToSystem();
                w.IsVisible = screen.IsVisible(rect);

                if (w.IsVisible)
                {
                    for (int z = i; z >= 0; z--)
                    {
                        var whTest = _cachedWindows[z];
                        if (w.WindowRect.IntersectsWith(whTest.WindowRect))
                        {
                            if (GetTopLevelWindow(whTest) != GetTopLevelWindow(w))
                            {
                                w.IsPartiallyCovered = true;
                                break;
                            }
                        }
                    }
                }

                if (w.Depth == 0)
                    screen.Exclude(rect);
            }
        }
        private void PopulateWindowBitmaps_Sync()
        {
            var windows = _cachedWindows.Where(w => w.IsVisible && w.Depth == 0 && w.IsPartiallyCovered);
            Parallel.ForEach(windows, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (c) =>
            {
                try
                {
                    Thread t = new Thread(new ThreadStart(() => { c.CaptureWindowBitmap(); }));
                    t.Start();
                    t.Join(1000);
                    if (!t.IsAlive)
                        return;
                    // the thread is taking too long, ie, stuck in a blocking operation that will never return (perhaps if the window never responds to our WM_PAINT message)
                    t.Interrupt();
                    t.Join(200);

                    if (!t.IsAlive)
                        return;

                    // the thread is _still_ alive, lets abort it.
                    t.Abort();
                }
                catch
                {
                    // who cares?
                }
            });
        }

        public class CachedWindow : IDisposable
        {
            public IntPtr Handle { get; private set; }
            public ScreenRect ImageBoundsRect { get; private set; } = ScreenRect.Empty;
            public ScreenRect WindowRect { get; private set; } = ScreenRect.Empty;
            public string Caption { get; private set; }
            public string ClassName { get; private set; }
            public bool IsVisible { get; set; }
            public bool IsPartiallyCovered { get; set; }
            public int Depth { get; private set; }
            public Bitmap WindowBitmap { get; private set; }
            public System.Windows.Media.Imaging.BitmapSource WindowBitmapWpf
            {
                get
                {
                    if (_wpfBitmap == null && WindowBitmap != null)
                        _wpfBitmap = WindowBitmap.ToBitmapSource();

                    return _wpfBitmap;
                }
            }
            public CachedWindow Parent { get; private set; }

            private System.Windows.Media.Imaging.BitmapSource _wpfBitmap = null;

            public CachedWindow()
            {
            }

            public CachedWindow(IntPtr handle, int depth, string className, string caption, ScreenRect windowRect, ScreenRect croppedRect, CachedWindow parent)
            {
                this.Handle = handle;
                this.ImageBoundsRect = croppedRect;
                this.WindowRect = windowRect;
                this.Caption = caption;
                this.ClassName = className;
                //this.ProcessID = processId;
                this.Depth = depth;
                this.Parent = parent;
            }

            ~CachedWindow()
            {
                this.Dispose();
            }

            public void CaptureWindowBitmap()
            {
                if (Depth > 0)
                    throw new InvalidOperationException("Must only call GetBitmap on a top-level window");

                if (WindowBitmap != null)
                    return;

                Bitmap initialBmp;
                ScreenRect basicBounds;
                try
                {

                    RECT normalWindowBounds;
                    if (!USER32.GetWindowRect(this.Handle, out normalWindowBounds))
                        throw new Win32Exception();

                    basicBounds = ScreenRect.FromSystem(normalWindowBounds);

                    initialBmp = new Bitmap(basicBounds.Width, basicBounds.Height);
                    try
                    {
                        using (Graphics g = Graphics.FromImage(initialBmp))
                        {
                            IntPtr dc = g.GetHdc();
                            try
                            {
                                bool success = USER32.PrintWindow(this.Handle, dc, PrintWindowDrawingOptions.PW_RENDERFULLCONTENT);
                                if (!success)
                                    throw new Win32Exception();
                            }
                            finally
                            {
                                g.ReleaseHdc(dc);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        // this most often occurs if the window is elevated or kernal mode and we do not have permission to capture it
                        initialBmp.Dispose();
                        return;
                    }
                }
                catch (ThreadInterruptedException e)
                {
                    // if we're inturrupted here we should exit, as we were stuck in a blocking non-returning function call.
                    return;
                }

                // windows print outside their bounds (drop shadows, etc), GetWindowRect returns the true size (thus also the size of rectangle that PrintWindow needs
                // but typically we want to omit the drop shadow and blending area and just show the logical window size. 
                // To achieve this we need to print the window at full size and then crop away the margins
                // Additionally, when a window is full screen, it extends beyond the screen boundary 

                var xoffset = this.WindowRect.Left - basicBounds.Left;
                var yoffset = this.WindowRect.Top - basicBounds.Top;

                if (xoffset < 1 && yoffset < 1)
                {
                    WindowBitmap = initialBmp;
                }
                else
                {
                    var croppingRectangle = new Rectangle(xoffset, yoffset, WindowRect.Width, WindowRect.Height);
                    var newBmp = initialBmp.Crop(croppingRectangle);
                    WindowBitmap = newBmp;
                    initialBmp.Dispose();
                }
            }

            public void ShowDebug()
            {
                var info = new WINDOWINFO(true);
                USER32.GetWindowInfo(Handle, ref info);

                List<string> exStyles = new List<string>();
                foreach (var env in Enum.GetValues(typeof(WindowStylesEx)).Cast<WindowStylesEx>())
                {
                    if (env == WindowStylesEx.WS_EX_LEFT)
                        continue;
                    if (info.dwExStyle.HasFlag(env))
                        exStyles.Add(Enum.GetName(typeof(WindowStylesEx), env));
                }

                System.Windows.MessageBox.Show(
$@"Window Debug Info:
Title: '{Caption}'
Class: '{ClassName}'
Depth: '{Depth}'
Borders: 'X {info.cxWindowBorders}, Y {info.cyWindowBorders}'
ActualBounds: '{ScreenRect.FromSystem(info.rcWindow)}'
CalcBounds: '{WindowRect.ToString()}'
Style: '{info.dwStyle.ToString()}'
Ex Style: '{String.Join(" | ", exStyles)}'
");
            }

            public override string ToString()
            {
                return $"{Caption} / {ClassName} [x:{ImageBoundsRect.Left} y:{ImageBoundsRect.Top} w:{ImageBoundsRect.Width} h:{ImageBoundsRect.Height}]";
            }

            public void Dispose()
            {
                if (WindowBitmap != null)
                    WindowBitmap.Dispose();
            }

            public override bool Equals(object obj)
            {
                var other = obj as CachedWindow;
                if (other == null)
                    return false;

                return this.Handle == other.Handle;
            }

            public override int GetHashCode()
            {
                return this.Handle.GetHashCode();
            }
        }
    }
}
