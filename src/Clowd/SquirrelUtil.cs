﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Clowd.PlatformUtil.Windows;
using Clowd.UI.Helpers;
using Clowd.Util;
using Squirrel;

[assembly: AssemblyMetadata("SquirrelAwareVersion", "1")]

namespace Clowd
{
    internal static class SquirrelUtil
    {
        public static string CurrentVersion { get; } = ThisAssembly.AssemblyInformationalVersion;
        public static bool IsFirstRun { get; private set; }
        public static bool JustRestarted { get; private set; }
        public static bool IsInstalled { get; private set; }

        private static string UniqueAppKey => "Clowd";
        private static readonly object _lock = new object();
        private static SquirrelUpdateViewModel _model;
        private static InstallerServices _srv;

        public static string[] Startup(string[] args)
        {
            _srv = new InstallerServices(UniqueAppKey, InstallerLocation.CurrentUser);

            SquirrelAwareApp.HandleEvents(
                onInitialInstall: OnInstall,
                onAppUpdate: OnUpdate,
                onAppUninstall: OnUninstall,
                onEveryRun: OnEveryRun,
                arguments: args);

            JustRestarted = args.Contains("--squirrel-restarted", StringComparer.OrdinalIgnoreCase);

            // if app is still running, filter out squirrel args and continue
            return args.Where(a => !a.Contains("--squirrel", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public static SquirrelUpdateViewModel GetUpdateViewModel()
        {
            if (_model == null)
                throw new InvalidOperationException("Can't update before app has been initialized");
            return _model;
        }

        private static void OnInstall(SemanticVersion ver, IAppTools tools)
        {
            tools.CreateUninstallerRegistryEntry();
            tools.CreateShortcutForThisExe(ShortcutLocation.StartMenuRoot | ShortcutLocation.Desktop);

            var menu = new ExplorerMenuLaunchItem("Upload with Clowd", SquirrelRuntimeInfo.EntryExePath, SquirrelRuntimeInfo.EntryExePath);
            _srv.ExplorerAllFilesMenu = menu;
            _srv.ExplorerDirectoryMenu = menu;
            _srv.AutoStartLaunchPath = SquirrelRuntimeInfo.EntryExePath;
        }

        private static void OnUpdate(SemanticVersion ver, IAppTools tools)
        {
            tools.CreateUninstallerRegistryEntry();
            tools.CreateShortcutForThisExe(ShortcutLocation.StartMenuRoot | ShortcutLocation.Desktop);

            // only update registry during update if they have not been removed by user
            var menu = new ExplorerMenuLaunchItem("Upload with Clowd", SquirrelRuntimeInfo.EntryExePath, SquirrelRuntimeInfo.EntryExePath);
            if (_srv.ExplorerAllFilesMenu != null)
                _srv.ExplorerAllFilesMenu = menu;
            if (_srv.ExplorerDirectoryMenu != null)
                _srv.ExplorerDirectoryMenu = menu;
            if (_srv.AutoStartLaunchPath != null)
                _srv.AutoStartLaunchPath = SquirrelRuntimeInfo.EntryExePath;
        }

        private static void OnUninstall(SemanticVersion ver, IAppTools tools)
        {
            _srv.RemoveAll();

            //tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenuRoot | ShortcutLocation.Desktop);
            //tools.RemoveUninstallerRegistryEntry();
        }

        private static void OnEveryRun(SemanticVersion ver, IAppTools tools, bool firstRun)
        {
            IsFirstRun = firstRun;
            IsInstalled = tools.CurrentlyInstalledVersion() != null;
            _model = new SquirrelUpdateViewModel(JustRestarted, IsInstalled);
        }

        public class SquirrelUpdateViewModel : SimpleNotifyObject
        {
            public bool ContextMenuRegistered
            {
                get
                {
                    var files = _srv.ExplorerAllFilesMenu;
                    var directory = _srv.ExplorerDirectoryMenu;
                    if (files == null || directory == null)
                        return false;
                    return true;
                }
                set
                {
                    if (value)
                    {
                        var menu = new ExplorerMenuLaunchItem("Upload with Clowd", SquirrelRuntimeInfo.EntryExePath, SquirrelRuntimeInfo.EntryExePath);
                        _srv.ExplorerAllFilesMenu = menu;
                        _srv.ExplorerDirectoryMenu = menu;
                    }
                    else
                    {
                        _srv.ExplorerAllFilesMenu = null;
                        _srv.ExplorerDirectoryMenu = null;
                    }

                    OnPropertyChanged();
                }
            }

            public bool AutoRunRegistered
            {
                get => _srv.AutoStartLaunchPath != null;
                set
                {
                    _srv.AutoStartLaunchPath = value ? SquirrelRuntimeInfo.EntryExePath : null;
                    OnPropertyChanged();
                }
            }

            public RelayUICommand ClickCommand
            {
                get => _clickCommand;
                protected set => Set(ref _clickCommand, value);
            }

            public string ClickCommandText
            {
                get => _clickCommandText;
                protected set => Set(ref _clickCommandText, value);
            }

            public string Description
            {
                get => _description;
                protected set => Set(ref _description, value);
            }

            public bool IsWorking
            {
                get => _isWorking;
                protected set => Set(ref _isWorking, value);
            }

            private ReleaseEntry _newVersion;
            private IDisposable _timer;
            private RelayUICommand _clickCommand;
            private string _clickCommandText;
            private string _description;
            private bool _isWorking;

            public SquirrelUpdateViewModel(bool justUpdated, bool isInstalled)
            {
                ClickCommand = new RelayUICommand(OnClick, CanExecute);
                if (true || isInstalled)
                {
                    ClickCommandText = "Check for updates";
                    Description = "Version: " + ThisAssembly.AssemblyInformationalVersion;
                    _timer = DisposableTimer.Start(TimeSpan.FromMinutes(5), CheckForUpdateTimer);

                    if (justUpdated)
                        Description += ", just updated!";
                }
                else
                {
                    IsWorking = true;
                    ClickCommandText = "Not Available";
                    Description = "Can't check for updates in portable mode";
                }
            }

            private void CheckForUpdateTimer()
            {
                if (_newVersion != null)
                {
                    // restart automatically if update waiting to install and system is idle
                    var idleTime = PlatformUtil.Platform.Current.GetSystemIdleTime();
                    if (idleTime > TimeSpan.FromMinutes(30))
                    {
                        RestartApp();
                    }
                }
                else
                {
                    CheckForUpdatesUnattended();
                }
            }

            public async Task CheckForUpdatesUnattended()
            {
                Exception ex = null;
                try
                {
                    lock (_lock)
                    {
                        if (_newVersion != null) return;
                        if (IsWorking) return;
                        IsWorking = true;
                    }

                    CommandManager.InvalidateRequerySuggested();
                    ClickCommandText = "Checking...";
                    using var mgr = new UpdateManager(Config.SettingsRoot.Current.General.UpdateReleaseUrl);
                    _newVersion = await mgr.UpdateApp(OnProgress);
                }
                catch (Exception e)
                {
                    ex = e;
                }
                finally
                {
                    if (_newVersion != null)
                    {
                        ClickCommandText = "Restart Clowd";
                        Description = $"Version {_newVersion.Version} has been downloaded";
                    }
                    else
                    {
                        ClickCommandText = "Check for Updates";
                        Description = "Version: " + CurrentVersion + ", no update available";
                    }

                    if (ex != null)
                    {
                        Description = ex.Message;
                        // log this
                    }

                    lock (_lock) IsWorking = false;
                    CommandManager.InvalidateRequerySuggested();
                }
            }

            private async void OnClick(object parameter)
            {
                if (_newVersion == null)
                {
                    // no update downloaded, lets check
                    await CheckForUpdatesUnattended();
                }
                else
                {
                    RestartApp();
                }
            }

            private void RestartApp()
            {
                UpdateManager.RestartAppWhenExited(arguments: "--squirrel-restarted");
                App.Current.ExitApp();
            }

            private void OnProgress(int obj)
            {
                if (obj < 33)
                    Description = $"Checking for updates: {obj}%";
                else
                    Description = $"Downloading updates: {obj}%";
            }

            private bool CanExecute(object parameter)
            {
                return !IsWorking;
            }
        }
    }

    internal class SquirrelLogger : Squirrel.SimpleSplat.ILogger
    {
        private readonly NLog.Logger _log;

        protected SquirrelLogger()
        {
            _log = NLog.LogManager.GetLogger("Squirrel");
        }

        public Squirrel.SimpleSplat.LogLevel Level { get; set; }

        public static void Register()
        {
            Squirrel.SimpleSplat.SquirrelLocator.CurrentMutable.Register(() => new SquirrelLogger(), typeof(Squirrel.SimpleSplat.ILogger));
        }

        public void Write(string message, Squirrel.SimpleSplat.LogLevel logLevel)
        {
            switch (logLevel)
            {
                case Squirrel.SimpleSplat.LogLevel.Debug:
                    _log.Debug(message);
                    break;
                case Squirrel.SimpleSplat.LogLevel.Info:
                    _log.Info(message);
                    break;
                case Squirrel.SimpleSplat.LogLevel.Warn:
                    _log.Warn(message);
                    break;
                case Squirrel.SimpleSplat.LogLevel.Error:
                    _log.Error(message);
                    break;
                case Squirrel.SimpleSplat.LogLevel.Fatal:
                    _log.Fatal(message);
                    break;
                default:
                    _log.Info(message);
                    break;
            }
        }
    }
}
