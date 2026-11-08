using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Housekeeping.Services;
using Housekeeping.Server.Hosting;
using Microsoft.AspNetCore.Builder;

namespace Housekeeping.DesktopHost
{
    public sealed class TrayDaemonLaunchOptions
    {
        public string[] RawArgs { get; set; } = Array.Empty<string>();
        public bool StartMinimized { get; set; }
        public int? PortOverride { get; set; }
        public bool? BindLanOverride { get; set; }
        public string? CustomDataDirectory { get; set; }
        public string? CustomConfigFilePath { get; set; }
        public string? CustomLogsDirectory { get; set; }
        public bool TestMode { get; set; }
        public bool DryRun { get; set; }
        public bool ManagedByMother { get; set; }
    }

    /// <summary>
    /// Always-on Windows System Tray application context hosting Kestrel + SignalR in-process,
    /// bridging native Windows file/folder dialogs on the STA thread, and launching either
    /// the user's default browser or a disposable WebView2 standalone window on demand.
    /// </summary>
    public sealed class TrayDaemonApplicationContext : ApplicationContext
    {
        private readonly StaNativeDialogBridge _dialogBridge;
        private readonly HousekeepingRuntimeContainer _runtime;
        private readonly WebApplication _webApp;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly Icon? _customIcon;
        private readonly int _activePort;
        private readonly string? _customDataDirectory;
        private readonly bool _managedByMother;

        private ToolStripMenuItem _pauseResumeMenuItem = null!;
        private ToolStripMenuItem _stopRunMenuItem = null!;
        private DisposableWebViewForm? _standaloneWindow;
        private bool _shuttingDown;

        public TrayDaemonApplicationContext(TrayDaemonLaunchOptions launchOptions)
        {
            _customDataDirectory = launchOptions.CustomDataDirectory;
            _managedByMother = launchOptions.ManagedByMother;
            _dialogBridge = new StaNativeDialogBridge();
            _dialogBridge.RestoreUiRequested += OpenPreferredDesktopUi;
            _dialogBridge.OpenStandaloneWindowRequested += () => OpenStandaloneWindow();

            _runtime = new HousekeepingRuntimeContainer(
                customDataDirectory: launchOptions.CustomDataDirectory,
                nativeDialogs: _dialogBridge,
                startBackgroundTimers: true,
                customConfigFilePath: launchOptions.CustomConfigFilePath,
                customLogsDirectory: launchOptions.CustomLogsDirectory,
                isTestMode: launchOptions.TestMode,
                forceDryRun: launchOptions.DryRun);

            int configuredPort = launchOptions.PortOverride ?? _runtime.ConfigService.Config.WebServer.Port;
            if (configuredPort <= 0 || configuredPort > 65535) configuredPort = 5178;
            _activePort = configuredPort;

            var serverOptions = new HousekeepingServerOptions
            {
                PortOverride = _activePort,
                BindLanOverride = launchOptions.BindLanOverride,
                CustomDataDirectory = launchOptions.CustomDataDirectory,
                CustomConfigFilePath = launchOptions.CustomConfigFilePath,
                CustomLogsDirectory = launchOptions.CustomLogsDirectory,
                TestMode = launchOptions.TestMode,
                DryRun = launchOptions.DryRun,
                NativeDialogBridge = _dialogBridge,
                StartBackgroundTimers = true
            };

            _webApp = HousekeepingServerHost.BuildWebApplication(
                launchOptions.RawArgs,
                serverOptions,
                prebuiltRuntime: _runtime);

            // Start Kestrel off the STA SynchronizationContext to avoid any startup deadlock
            Task.Run(() => _webApp.StartAsync()).GetAwaiter().GetResult();

            _webApp.Lifetime.ApplicationStopping.Register(() =>
            {
                if (_shuttingDown) return;
                try
                {
                    if (_dialogBridge.IsAvailable && _dialogBridge.InvokeRequired)
                    {
                        _dialogBridge.BeginInvoke(new Action(ShutdownAndExit));
                    }
                    else
                    {
                        ShutdownAndExit();
                    }
                }
                catch (Exception stopEx)
                {
                    Debug.WriteLine($"[TrayDaemonApplicationContext] ApplicationStopping callback ignored: {stopEx.Message}");
                }
            });

            bool bindAll = launchOptions.BindLanOverride ?? _runtime.ConfigService.Config.WebServer.BindLanAndTailnet;
            _runtime.Orchestrator.Log("INFO", $"[DesktopHost] Housekeeping Engine Online — listening on {(bindAll ? "0.0.0.0" : "127.0.0.1")}:{_activePort}");

            _customIcon = TryLoadAppIcon();
            _trayMenu = BuildTrayContextMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon = _customIcon ?? SystemIcons.Application,
                Text = TruncateNotifyText($"Housekeeping — Online (:{_activePort})"),
                ContextMenuStrip = _trayMenu,
                Visible = !_managedByMother
            };

            _notifyIcon.DoubleClick += (_, _) => OpenPreferredDesktopUi();

            _runtime.Orchestrator.OnTrayNotificationRequested += HandleTrayNotificationRequested;
            _runtime.Orchestrator.OnStateChanged += HandlePipelineStateChanged;

            if (!launchOptions.StartMinimized)
            {
                OpenPreferredDesktopUi();
            }
        }

        private string BaseLocalUrl => $"http://127.0.0.1:{_activePort}";

        private ContextMenuStrip BuildTrayContextMenu()
        {
            var menu = new ContextMenuStrip();

            var headerItem = new ToolStripMenuItem($"Housekeeping (:{_activePort})")
            {
                Enabled = false,
                Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
            };
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("🌐 Open in Browser", null, (_, _) => OpenInBrowser(BaseLocalUrl));
            menu.Items.Add("🪟 Open Standalone Window", null, (_, _) => OpenStandaloneWindow(BaseLocalUrl));
            menu.Items.Add("🛡 Open Archive Compliance Reports", null, (_, _) => OpenPreferredDesktopUi($"{BaseLocalUrl}/#reports"));

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("▶ Run Active Queue", null, (_, _) =>
            {
                var res = _runtime.Orchestrator.StartOrEnqueueQueueRun(null, isDryRun: false, triggerSource: "SystemTray", enqueueIfBusy: true);
                if (!res.Accepted || res.Queued)
                {
                    ShowBalloon("Housekeeping", res.Message, isWarning: !res.Accepted);
                }
            });

            menu.Items.Add("🧪 Dry-Run Active Queue", null, (_, _) =>
            {
                var res = _runtime.Orchestrator.StartOrEnqueueQueueRun(null, isDryRun: true, triggerSource: "SystemTrayDryRun", enqueueIfBusy: true);
                if (!res.Accepted || res.Queued)
                {
                    ShowBalloon("Housekeeping", res.Message, isWarning: !res.Accepted);
                }
            });

            _pauseResumeMenuItem = new ToolStripMenuItem("⏸ Pause / Resume Queue", null, (_, _) =>
            {
                _runtime.Orchestrator.TogglePause();
            })
            {
                Enabled = false
            };
            menu.Items.Add(_pauseResumeMenuItem);

            _stopRunMenuItem = new ToolStripMenuItem("⏹ Stop Active Run", null, (_, _) =>
            {
                _runtime.Orchestrator.StopActiveRun();
            })
            {
                Enabled = false
            };
            menu.Items.Add(_stopRunMenuItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("⏻ Shut Down Housekeeping Engine", null, (_, _) => RequestEngineShutdown());

            return menu;
        }

        public void OpenPreferredDesktopUi()
        {
            OpenPreferredDesktopUi(BaseLocalUrl);
        }

        public void OpenPreferredDesktopUi(string targetUrl)
        {
            string mode = _runtime.ConfigService.Config.WebServer.DefaultDesktopLaunchMode ?? "StandaloneWindow";
            if (string.Equals(mode, "Browser", StringComparison.OrdinalIgnoreCase))
            {
                OpenInBrowser(targetUrl);
            }
            else
            {
                OpenStandaloneWindow(targetUrl);
            }
        }

        public void OpenStandaloneWindow(string? targetUrl = null)
        {
            string url = string.IsNullOrWhiteSpace(targetUrl) ? BaseLocalUrl : targetUrl;
            if (_dialogBridge.InvokeRequired)
            {
                _dialogBridge.BeginInvoke(new Action(() => OpenStandaloneWindow(url)));
                return;
            }

            if (_standaloneWindow != null && !_standaloneWindow.IsDisposed)
            {
                _standaloneWindow.NavigateAndFocus(url);
                return;
            }

            _standaloneWindow = new DisposableWebViewForm(
                url,
                _activePort,
                _customIcon,
                msg => _runtime.Orchestrator.Log("WARN", $"[DesktopHost] {msg}"),
                customUserDataRoot: _runtime.ConfigService.DataDirectory);

            _standaloneWindow.FormClosed += (_, _) =>
            {
                _standaloneWindow = null;
            };

            _standaloneWindow.Show();
            _standaloneWindow.BringToFront();
            _standaloneWindow.Activate();
        }

        private static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        private void HandleTrayNotificationRequested(string title, string message, bool isWarning)
        {
            if (_shuttingDown || !_dialogBridge.IsAvailable || _managedByMother) return;
            try
            {
                _dialogBridge.BeginInvoke(new Action(() => ShowBalloon(title, message, isWarning)));
            }
            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx2.Message}"); }
        }

        private void HandlePipelineStateChanged(PipelineRuntimeStateDto state)
        {
            if (_shuttingDown || !_dialogBridge.IsAvailable) return;
            try
            {
                _dialogBridge.BeginInvoke(new Action(() =>
                {
                    if (_shuttingDown) return;
                    _pauseResumeMenuItem.Enabled = state.IsRunning;
                    _pauseResumeMenuItem.Text = state.IsPaused ? "▶ Resume Paused Queue" : "⏸ Pause Active Queue";
                    _stopRunMenuItem.Enabled = state.IsRunning;

                    if (!_managedByMother)
                    {
                        string tooltip = state.IsRunning
                            ? $"Housekeeping — {(state.IsPaused ? "Paused" : "Running")}: {state.ActiveJobName ?? "Queue"} ({state.ProgressPercent:0}%)"
                            : $"Housekeeping — Online (:{_activePort})";
                        _notifyIcon.Text = TruncateNotifyText(tooltip);
                    }
                }));
            }
            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx3.Message}"); }
        }

        private void ShowBalloon(string title, string message, bool isWarning)
        {
            if (_shuttingDown) return;
            try
            {
                _notifyIcon.ShowBalloonTip(
                    4500,
                    string.IsNullOrWhiteSpace(title) ? "Housekeeping" : title,
                    string.IsNullOrWhiteSpace(message) ? "Notification" : message,
                    isWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx4.Message}"); }
        }

        private void RequestEngineShutdown()
        {
            if (_shuttingDown) return;

            if (_runtime.Orchestrator.IsRunning)
            {
                DialogResult confirm = MessageBox.Show(
                    "A Housekeeping job or queue is currently running.\n\nStop the active run and shut down the Housekeeping background engine?",
                    "Confirm Housekeeping Engine Shutdown",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            ShutdownAndExit();
        }

        private void ShutdownAndExit()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            _runtime.Orchestrator.OnTrayNotificationRequested -= HandleTrayNotificationRequested;
            _runtime.Orchestrator.OnStateChanged -= HandlePipelineStateChanged;

            if (_runtime.Orchestrator.IsRunning)
            {
                _runtime.Orchestrator.StopActiveRun();
            }

            if (_standaloneWindow != null && !_standaloneWindow.IsDisposed)
            {
                try { _standaloneWindow.Close(); } catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx5.Message}"); }
                _standaloneWindow = null;
            }

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx6.Message}"); }

            try
            {
                Task.Run(async () =>
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    try { await _webApp.StopAsync(stopCts.Token); } catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx7.Message}"); }
                    try { await _webApp.DisposeAsync(); } catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx8.Message}"); }
                }).GetAwaiter().GetResult();
            }
            catch (System.Exception __suppressedEx9) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx9.Message}"); }

            try { _runtime.Dispose(); } catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx10.Message}"); }
            try { _trayMenu.Dispose(); } catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx11.Message}"); }
            try { _customIcon?.Dispose(); } catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx12.Message}"); }
            try { _dialogBridge.Close(); _dialogBridge.Dispose(); } catch (System.Exception __suppressedEx13) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx13.Message}"); }

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_shuttingDown)
            {
                ShutdownAndExit();
            }
            base.Dispose(disposing);
        }

        private static Icon? TryLoadAppIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_icon.ico");
                if (File.Exists(icoPath))
                {
                    return new Icon(icoPath);
                }

                string exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    return Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch (System.Exception __suppressedEx14) { System.Diagnostics.Debug.WriteLine($"[TrayDaemonApplicationContext] Non-fatal error ignored: {__suppressedEx14.Message}"); }

            return null;
        }

        private static string TruncateNotifyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Housekeeping";
            // Win32 NotifyIcon.Text strictly caps at 63 characters (< 64)
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }
    }
}
