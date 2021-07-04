﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Clowd.UI;
using Clowd.UI.Config;
using Clowd.UI.Helpers;
using Clowd.Upload;
using Clowd.Util;
using PropertyChanged;
using RT.Serialization;
using RT.Util;
using PData = PropertyTools.DataAnnotations;

namespace Clowd.Config
{
    //public enum SettingsCategory
    //{
    //    General,
    //    Hotkeys,
    //    Capture,
    //    Editor,
    //    Uploads,
    //    Windows,
    //    Video,
    //}
    public enum AccentScheme
    {
        [Description("User defined")]
        User,
        [Description("Automatic")]
        System
    }

    public enum SettingsDisplayMode
    {
        [Description("Tabbed")]
        Tabbed,
        [Description("Single Page")]
        SinglePage
    }

    public enum UploadsProvider
    {
        [Description("None / Disabled")]
        None = 0,
        [Description("Azure Storage")]
        Azure
    }

    public enum SelectedWindowForegroundPromotion
    {
        [Description("Disabled")]
        None,
        [Description("When Clicked")]
        WhenClicked,
        [Description("When Hovered")]
        WhenHovered
    }

    [Settings("Clowd", SettingsKind.UserSpecific, SettingsSerializer.ClassifyXml)]
    public class GeneralSettings : SettingsBase, INotifyPropertyChanged, IDisposable
    {
        //[Browsable(false), ClassifyIgnore]
        //public string AppDataDirectory => Constants.AppDataDirectory;

        //[Browsable(false), ClassifyIgnore]
        //public string SettingsDirectory => AppDataDirectory;

        //[Browsable(false), ClassifyIgnore]
        //public string LogsDirectory => System.IO.Path.Combine(SettingsDirectory, "logs");

        [Browsable(false), ClassifyIgnore]
        public new object Attribute { get; } = null;

        [Browsable(false)]
        public event PropertyChangedEventHandler PropertyChanged;

        [Browsable(false)]
        public bool FirstRun { get; set; } = true;

        [Browsable(false)]
        public string LastUploadPath { get; set; }

        [Browsable(false)]
        public string LastSavePath { get; set; }

        [Category(nameof(SettingsCategory.General)), DisplayName("Confirm before exit")]
        [Description("If true, Clowd will prompt for confirmation before closing.")]
        public bool ConfirmClose { get; set; } = true;

        //[Description("Specifies whether to use the system default window chrome, or a modern metro design.")]
        //public bool UseCustomWindowChrome { get; set; } = false;

        //[DisplayName("Tray-drop enabled")]
        //[Description("If true, allows dropping files directly on to the windows tray icon to start an upload.")]
        //public bool TrayDropEnabled => false;

        [DisplayName("Accent color"), PData.EnableBy(nameof(AccentScheme), AccentScheme.User)]
        [Description("Allows you to set a custom accent color when the appropriate accent mode is also set.")]
        public Color UserAccentColor { get; set; } = Color.FromRgb(59, 151, 210);

        [DisplayName("Accent mode")]
        [Description("If user is selected, you can define a custom accent color. " +
                     "Otherwise, Clowd will choose a color based on the system color")]
        public AccentScheme AccentScheme { get; set; } = AccentScheme.User;

        public bool CheckForUpdatesAutomatically { get; set; } = true;

        [DataType(DataType.Duration)]
        [Description("This is how often clowd checks for updates.")]
        [PData.VisibleBy(nameof(CheckForUpdatesAutomatically))]
        public TimeSpan UpdateCheckInterval { get; set; } = TimeSpan.FromHours(2);

        [ClassifyIgnore]
        public UpdaterControl Updates { get; set; } = new UpdaterControl();

        [DisplayName("Settings Display Mode")]
        [Description("This controls whether the settings window is separated into tabbed content" +
                     "or as a single scrolling page.")]
        public SettingsDisplayMode DisplayMode { get; set; } = SettingsDisplayMode.Tabbed;

        [Category(nameof(SettingsCategory.Hotkeys)), DisplayName("General - File Upload"), ClassifyIgnoreIfDefault]
        public GlobalTrigger FileUploadShortcut { get; set; }
            = new GlobalTrigger(() => App.Current.UploadFile());

        //[Category("Hotkeys"), DisplayName("General - Open Clowd"), ClassifyIgnoreIfDefault]
        //public GlobalTrigger OpenHomeShortcut { get; set; }
        //    = new GlobalTrigger(() => App.Current.ShowHome());

        [ExpandAsCategory(nameof(SettingsCategory.Capture))]
        public CaptureSettings CaptureSettings { get; set; } = new CaptureSettings();

        [ExpandAsCategory(nameof(SettingsCategory.Editor))]
        public EditorSettings EditorSettings { get; set; } = new EditorSettings();

        [ExpandAsCategory(nameof(SettingsCategory.Uploads))]
        public UploadSettings UploadSettings { get; set; } = new UploadSettings();

        [ExpandAsCategory(nameof(SettingsCategory.Windows))]
        public FeatureSettings FeatureSettings { get; set; } = new FeatureSettings();

        [ExpandAsCategory(nameof(SettingsCategory.Video))]
        public VideoCapturerSettings VideoSettings { get; set; } = new VideoCapturerSettings();

        //[Browsable(false), ClassifyNotNull]
        //public int[] CustomColors { get; set; } = new int[0];

        private IEnumerable<T> GetAllAssignableToT<T>(T root)
        {
            MethodInfo method = GetType().GetMethod(nameof(GetAllAssignableToT), BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo myself = method.MakeGenericMethod(typeof(T));

            yield return root;

            var subSettings = root
                .GetType()
                .GetProperties()
                .Where(p => typeof(T).IsAssignableFrom(p.PropertyType) && (!p.PropertyType.IsGenericType || p.PropertyType.GetGenericTypeDefinition() != typeof(TrulyObservableCollection<>)))
                .Select(p => p.GetValue(root))
                .Cast<T>();

            foreach (var s in subSettings)
                foreach (var results in (IEnumerable<T>)myself.Invoke(this, new[] { (object)s }))
                    yield return results;
        }

        public void Dispose()
        {
            foreach (var a in GetAllAssignableToT<IDisposable>(this))
                a?.Dispose();
        }

        public override void SaveQuiet(string filename = null, SettingsSerializer? serializer = null)
        {
            Save(filename, serializer, SettingsOnFailure.DoNothing);
        }

        public override void SaveLoud(string filename = null, SettingsSerializer? serializer = null)
        {
            Save(filename, serializer, SettingsOnFailure.Throw);
        }

        public override void Save(string filename = null, SettingsSerializer? serializer = null, SettingsOnFailure onFailure = SettingsOnFailure.Throw)
        {
            SaveDebounced(filename, serializer, onFailure);
        }

        [Browsable(false), ClassifyIgnore]
        private static Debouncer _deboucer = new Debouncer(1000);
        private void SaveDebounced(string filename = null, SettingsSerializer? serializer = null, SettingsOnFailure onFailure = SettingsOnFailure.Throw)
        {
            _deboucer.Debounce(() => SaveInternal(filename, serializer, onFailure));
        }

        private void SaveInternal(string filename = null, SettingsSerializer? serializer = null, SettingsOnFailure onFailure = SettingsOnFailure.Throw)
        {
            Console.WriteLine("SAVED!!!");
            base.Save(filename, serializer, onFailure);
        }
    }

    public static class PropertyChangedNotificationInterceptor
    {
        public static void Intercept(object target, Action onPropertyChangedAction, string propertyName)
        {
            if (App.Current != null && App.Current.Settings != null)
            {
                onPropertyChangedAction();
                App.Current.Settings.SaveQuiet();
            }
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class CaptureSettings : IDisposable
    {
        [DisplayName("Capture with cursor")]
        [Description("If this is enabled, the cursor will be shown in screenshots")]
        public bool ScreenshotWithCursor { get; set; } = false;

        [DisplayName("Compatibility Mode")]
        [Description("If this is enabled, the capture window will use the windows native cursor and will make some visual sacrifices in favor of high performance and responsiveness")]
        public bool CompatibilityMode { get; set; } = false;

        //[Description("This controls the default state of the pixel magnifier in the capture window")]
        //public bool MagnifierEnabled { get; set; } = true;

        //[Description("This controls whether the tips menu is displayed when capturing a screenshot.")]
        //public bool TipsEnabled { get; set; } = true;

        [Description("If this is true, the Capture window will try to detect and highlight different windows as you hover over them.")]
        public bool DetectWindows { get; set; } = true;

        //[DisplayName("Bring selected window to the foreground")]
        //public SelectedWindowForegroundPromotion SelectedWindowPromotion { get; set; } = SelectedWindowForegroundPromotion.WhenClicked;

        [Category(nameof(SettingsCategory.Hotkeys)), DisplayName("Capture - Region"), ClassifyIgnoreIfDefault]
        public GlobalTrigger CaptureRegionShortcut { get; set; }
            = new GlobalTrigger(Key.PrintScreen, () => App.Current.StartCapture());

        [Category(nameof(SettingsCategory.Hotkeys)), DisplayName("Capture - Fullscreen"), ClassifyIgnoreIfDefault]
        public GlobalTrigger CaptureFullscreenShortcut { get; set; }
            = new GlobalTrigger(Key.PrintScreen, ModifierKeys.Control, App.Current.QuickCaptureFullScreen);

        [Category(nameof(SettingsCategory.Hotkeys)), DisplayName("Capture - Active Window"), ClassifyIgnoreIfDefault]
        public GlobalTrigger CaptureActiveShortcut { get; set; }
            = new GlobalTrigger(Key.PrintScreen, ModifierKeys.Alt, App.Current.QuickCaptureCurrentWindow);

        public void Dispose()
        {
            CaptureRegionShortcut?.Dispose();
            CaptureFullscreenShortcut?.Dispose();
            CaptureActiveShortcut?.Dispose();
        }
    }

    [AddINotifyPropertyChangedInterface]
    public class EditorSettings
    {
        public Color CanvasBackground { get; set; } = Colors.White;

        //public RememberPromptChoice OpenCaptureInExistingEditor { get; set; } = RememberPromptChoice.Ask;

        [DisplayName("Capture padding (px)"), PData.Spinnable(5, 10, 0, 100)]
        [Description("Controls how much space (in pixels) there is between an opened capture and the editors window edge.")]
        public int CapturePadding { get; set; } = 30;

        public AutoDictionary<DrawToolsLib.ToolType, SavedToolSettings> ToolSettings { get; set; } = new AutoDictionary<DrawToolsLib.ToolType, SavedToolSettings>();
    }

    [AddINotifyPropertyChangedInterface]
    public class SavedToolSettings
    {
        public bool TextObjectColorIsAuto { get; set; } = true;
        public Color ObjectColor { get; set; } = Colors.Red;
        public double LineWidth { get; set; } = 2d;
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 12d;
        public FontStyle FontStyle { get; set; } = FontStyles.Normal;
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;
        public FontStretch FontStretch { get; set; } = FontStretches.Normal;
    }

    //[ImplementPropertyChanged]
    //public class UploadSettings : IDisposable, IAzureOptions
    //{
    //    [Category(nameof(SettingsCategory.Hotkeys)), DisplayName("Uploads - Activate Next"), ClassifyIgnoreIfDefault]
    //    [Description("This hotkey activates the next item in the task window. " +
    //                 "For instance, if the next item is an upload it will be copied to the clipboard.")]
    //    public GlobalTrigger ActivateNextShortcut { get; set; }
    //       = new GlobalTrigger(() => TaskWindow.Current?.ActivateNext());

    //    [Category(nameof(SettingsCategory.Uploads)), DisplayName("Upload Storage Provider")]
    //    public UploadsProvider UploadProvider { get; set; } = UploadsProvider.None;

    //    [PData.VisibleBy(nameof(UploadProvider), UploadsProvider.Azure)]
    //    public string AzureConnectionString { get; set; }

    //    [PData.VisibleBy(nameof(UploadProvider), UploadsProvider.Azure)]
    //    public string AzureContainerName { get; set; }

    //    [Description("If true, the original filename will be ignored and a random one will be chosen for the upload.")]
    //    public bool UseUniqueUploadKey { get; set; } = true;

    //    [Description("Use if you would like to override the default URL generated by Clowd. Use the following substitution variables: \n#{uk} - Upload Key\n#{mt} - Mime Type (calculated from file name)")]
    //    public string CustomUrlPattern { get; set; }

    //    public void Dispose()
    //    {
    //        ActivateNextShortcut?.Dispose();
    //    }
    //}

    [AddINotifyPropertyChangedInterface]
    public class FeatureSettings
    {
        [DisplayName("Auto Start on Login")]
        public Installer.Features.IFeature AutoStart { get; } = new Installer.Features.AutoStart();

        [DisplayName("Explorer context-menu entry")]
        public Installer.Features.IFeature ContextMenu { get; } = new Installer.Features.ContextMenu();

        [DisplayName("DirectShow Add-ons")]
        public Installer.Features.IFeature DirectShow { get; } = new Installer.Features.DShowFilter();

        [DisplayName("Desktop / Program Shortcuts")]
        public Installer.Features.IFeature Shortcuts { get; } = new Installer.Features.Shortcuts();
    }

    //public enum BitrateMultiplier : int
    //{
    //    Low = 75,
    //    Medium = 100,
    //    High = 150,
    //}

    //public enum MaxResolution : int
    //{
    //    [Description("Uncapped")]
    //    Uncapped = 0,
    //    [Description("SD - 480p")]
    //    _480p = 480,
    //    [Description("HD - 720p")]
    //    _720p = 720,
    //    [Description("HD - 1080p")]
    //    _1080p = 1080,
    //    [Description("HD - 1440p")]
    //    _1440p = 1440,
    //    [Description("4K - 2160p")]
    //    _2160p = 2160,
    //}

    //[ImplementPropertyChanged]
    //public class VideoSettings : IDisposable
    //{
    //    [PData.DirectoryPath]
    //    public string OutputDirectory { get; set; }

    //    public bool ShowCursor { get; set; } = true;

    //    [PData.Slidable(Minimum = 5, Maximum = 60, SnapToTicks = true, TickFrequency = 5, SmallChange = 5, LargeChange = 5)]
    //    public int FPS { get; set; } = 15;

    //    [DisplayName("Encoder Settings")]
    //    public FFmpegSettings VideoCodec { get; set; } = new FFmpegSettings();

    //    public void Dispose()
    //    {
    //    }
    //}
}
