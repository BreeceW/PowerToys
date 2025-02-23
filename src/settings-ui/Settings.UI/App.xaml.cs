﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using interop;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Telemetry.Events;
using Microsoft.PowerToys.Telemetry;
using Microsoft.UI.Xaml;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Microsoft.PowerToys.Settings.UI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private enum Arguments
        {
            PTPipeName = 1,
            SettingsPipeName,
            PTPid,
            Theme, // used in the old settings
            ElevatedStatus,
            IsUserAdmin,
            ShowOobeWindow,
            ShowScoobeWindow,
            SettingsWindow,
        }

        // Quantity of arguments
        private const int RequiredArgumentsQty = 9;
        private const int RequiredAndOptionalArgumentsQty = 10;

        // Create an instance of the  IPC wrapper.
        private static TwoWayPipeMessageIPCManaged ipcmanager;

        public static bool IsElevated { get; set; }

        public static bool IsUserAnAdmin { get; set; }

        public static int PowerToysPID { get; set; }

        public bool ShowOobe { get; set; }

        public bool ShowScoobe { get; set; }

        public Type StartupPage { get; set; } = typeof(Views.GeneralPage);

        public static Action<string> IPCMessageReceivedCallback { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class.
        /// Initializes the singleton application object. This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        public static void OpenSettingsWindow(Type type)
        {
            if (settingsWindow == null)
            {
                settingsWindow = new MainWindow();
            }

            settingsWindow.Activate();
            settingsWindow.NavigateToSection(type);
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var cmdArgs = Environment.GetCommandLineArgs();

            if (cmdArgs != null && cmdArgs.Length >= RequiredArgumentsQty)
            {
                // Skip the first argument which is prepended when launched by explorer
                if (cmdArgs[0].EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) && cmdArgs[1].EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) && (cmdArgs.Length >= RequiredArgumentsQty + 1))
                {
                    cmdArgs = cmdArgs.Skip(1).ToArray();
                }

                _ = int.TryParse(cmdArgs[(int)Arguments.PTPid], out int powerToysPID);
                PowerToysPID = powerToysPID;

                IsElevated = cmdArgs[(int)Arguments.ElevatedStatus] == "true";
                IsUserAnAdmin = cmdArgs[(int)Arguments.IsUserAdmin] == "true";
                ShowOobe = cmdArgs[(int)Arguments.ShowOobeWindow] == "true";
                ShowScoobe = cmdArgs[(int)Arguments.ShowScoobeWindow] == "true";

                if (cmdArgs.Length == RequiredAndOptionalArgumentsQty)
                {
                    // open specific window
                    switch (cmdArgs[(int)Arguments.SettingsWindow])
                    {
                        case "Overview": StartupPage = typeof(Views.GeneralPage); break;
                        case "AlwaysOnTop": StartupPage = typeof(Views.AlwaysOnTopPage); break;
                        case "Awake": StartupPage = typeof(Views.AwakePage); break;
                        case "ColorPicker": StartupPage = typeof(Views.ColorPickerPage); break;
                        case "FancyZones": StartupPage = typeof(Views.FancyZonesPage); break;
                        case "Run": StartupPage = typeof(Views.PowerLauncherPage); break;
                        case "ImageResizer": StartupPage = typeof(Views.ImageResizerPage); break;
                        case "KBM": StartupPage = typeof(Views.KeyboardManagerPage); break;
                        case "MouseUtils": StartupPage = typeof(Views.MouseUtilsPage); break;
                        case "PowerRename": StartupPage = typeof(Views.PowerRenamePage); break;
                        case "FileExplorer": StartupPage = typeof(Views.PowerPreviewPage); break;
                        case "ShortcutGuide": StartupPage = typeof(Views.ShortcutGuidePage); break;
                        case "VideoConference": StartupPage = typeof(Views.VideoConferencePage); break;
                        default: Debug.Assert(false, "Unexpected SettingsWindow argument value"); break;
                    }
                }

                RunnerHelper.WaitForPowerToysRunner(PowerToysPID, () =>
                {
                    Environment.Exit(0);
                });

                ipcmanager = new TwoWayPipeMessageIPCManaged(cmdArgs[(int)Arguments.SettingsPipeName], cmdArgs[(int)Arguments.PTPipeName], (string message) =>
                {
                    if (IPCMessageReceivedCallback != null && message.Length > 0)
                    {
                        IPCMessageReceivedCallback(message);
                    }
                });
                ipcmanager.Start();

                if (!ShowOobe && !ShowScoobe)
                {
                    settingsWindow = new MainWindow();
                    settingsWindow.Activate();
                    settingsWindow.NavigateToSection(StartupPage);
                }
                else
                {
                    // Create the Settings window so that it's fully initialized and
                    // it will be ready to receive the notification if the user opens
                    // the Settings from the tray icon.
                    if (ShowOobe)
                    {
                        PowerToysTelemetry.Log.WriteEvent(new OobeStartedEvent());
                        OobeWindow oobeWindow = new OobeWindow(OOBE.Enums.PowerToysModules.Overview);
                        oobeWindow.Activate();
                        SetOobeWindow(oobeWindow);
                    }
                    else if (ShowScoobe)
                    {
                        PowerToysTelemetry.Log.WriteEvent(new ScoobeStartedEvent());
                        OobeWindow scoobeWindow = new OobeWindow(OOBE.Enums.PowerToysModules.WhatsNew);
                        scoobeWindow.Activate();
                        SetOobeWindow(scoobeWindow);
                    }
                }
            }
            else
            {
                // For debugging purposes
                // Window is also needed to show MessageDialog
                settingsWindow = new MainWindow();
                settingsWindow.Activate();
                ShowMessageDialog("The application cannot be run as a standalone process. Please start the application through the runner.", "Forbidden");
            }
        }

        private async void ShowMessageDialog(string content, string title = null)
        {
            await ShowDialogAsync(content, title);
        }

        public static Task<IUICommand> ShowDialogAsync(string content, string title = null)
        {
            var dialog = new MessageDialog(content, title ?? string.Empty);
            var handle = NativeMethods.GetActiveWindow();
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException();
            }

            InitializeWithWindow.Initialize(dialog, handle);
            return dialog.ShowAsync().AsTask<IUICommand>();
        }

        public static TwoWayPipeMessageIPCManaged GetTwoWayIPCManager()
        {
            return ipcmanager;
        }

        public static bool IsDarkTheme()
        {
            var selectedTheme = SettingsRepository<GeneralSettings>.GetInstance(settingsUtils).SettingsConfig.Theme.ToUpper(CultureInfo.InvariantCulture);
            var defaultTheme = new UISettings();
            var uiTheme = defaultTheme.GetColorValue(UIColorType.Background).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return selectedTheme == "DARK" || (selectedTheme == "SYSTEM" && uiTheme == "#FF000000");
        }

        private static ISettingsUtils settingsUtils = new SettingsUtils();

        private static MainWindow settingsWindow;
        private static OobeWindow oobeWindow;

        public static void ClearSettingsWindow()
        {
            settingsWindow = null;
        }

        public static MainWindow GetSettingsWindow()
        {
            return settingsWindow;
        }

        public static OobeWindow GetOobeWindow()
        {
            return oobeWindow;
        }

        public static void SetOobeWindow(OobeWindow window)
        {
            oobeWindow = window;
        }

        public static void ClearOobeWindow()
        {
            oobeWindow = null;
        }
    }
}
