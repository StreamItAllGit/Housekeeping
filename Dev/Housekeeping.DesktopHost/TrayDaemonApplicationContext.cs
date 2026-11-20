// @module: Housekeeping.DesktopHost.TrayDaemonApplicationContext
// @purpose: Hosts the background WebApplication + optional WinForms System Tray icon (suppressed under Mother supervision) and WebView2 desktop window.
// @depends-on: System, System.Diagnostics, System.Drawing, System.Linq, System.Threading, System.Threading.Tasks, System.Windows.Forms, Housekeeping.Services, Housekeeping.Server.Hosting, Microsoft.AspNetCore.Builder
// @exports: TrayDaemonLaunchOptions, TrayDaemonApplicationContext

using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
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
        public string? BindHostOverride { get; set; }
        public string? SuiteId { get; set; }
        public string? CustomDataDirectory { get; set; }
        public string? CustomConfigFilePath { get; set; }
        public string? CustomLogsDirectory { get; set; }
        public bool IsolatedOverride { get; set; }
        public bool DryRun { get; set; }
        public bool ManagedByMother { get; set; }
    }

    public sealed class TrayDaemonApplicationContext : ApplicationContext
    {
        private readonly StaNativeDialogBridge _dialogBridge;
        private readonly HousekeepingRuntimeContainer _runtime;
        private readonly WebApplication _webApp;
        private readonly NotifyIcon? _notifyIcon;
        private readonly ContextMenuStrip? _trayMenu;
        private readonly Icon? _customIcon;
        private readonly int _activePort;
        private readonly bool _managedByMother;

        private readonly ToolStripMenuItem? _pauseResumeMenuItem;
        private readonly ToolStripMenuItem? _stopCurrentJobMenuItem;
        private readonly ToolStripMenuItem? _stopEntireQueueMenuItem;
        private DisposableWebViewForm? _standaloneWindow;
        private bool _shuttingDown;

        public TrayDaemonApplicationContext(TrayDaemonLaunchOptions launchOptions)
        {
            _managedByMother = ResolveManagedByMother(launchOptions);
            _dialogBridge = new StaNativeDialogBridge();
            _dialogBridge.RestoreUiRequested += OpenPreferredDesktopUi;
            _dialogBridge.OpenStandaloneWindowRequested += () => OpenStandaloneWindow();

            _runtime = new HousekeepingRuntimeContainer(
                customDataDirectory: launchOptions.CustomDataDirectory,
                nativeDialogs: _dialogBridge,
                startBackgroundTimers: true,
                customConfigFilePath: launchOptions.CustomConfigFilePath,
                customLogsDirectory: launchOptions.CustomLogsDirectory,
                isolatedOverride: launchOptions.IsolatedOverride,
                forceDryRun: launchOptions.DryRun);

            int configuredPort = launchOptions.PortOverride ?? _runtime.ConfigService.Config.WebServer.Port;
            if (configuredPort <= 0 || configuredPort > 65535) configuredPort = 5178;
            _activePort = configuredPort;

            var serverOptions = new HousekeepingServerOptions
            {
                PortOverride = _activePort,
                BindLanOverride = _managedByMother ? false : launchOptions.BindLanOverride,
                BindHostOverride = _managedByMother ? "127.0.0.1" : launchOptions.BindHostOverride,
                ManagedByMother = _managedByMother,
                SuiteId = launchOptions.SuiteId,
                CustomDataDirectory = launchOptions.CustomDataDirectory,
                CustomConfigFilePath = launchOptions.CustomConfigFilePath,
                CustomLogsDirectory = launchOptions.CustomLogsDirectory,
                IsolatedOverride = launchOptions.IsolatedOverride,
                DryRun = launchOptions.DryRun,
                NativeDialogBridge = _dialogBridge,
                StartBackgroundTimers = true
            };

            _webApp = HousekeepingServerHost.BuildWebApplication(
                launchOptions.RawArgs,
                serverOptions,
                prebuiltRuntime: _runtime);

            Task.Run(() => _webApp.StartAsync()).GetAwaiter().GetResult();

            _webApp.Lifetime.ApplicationStopping.Register(() =>
            {
                if (_shuttingDown) return;
                try
                {
                    if (_dialogBridge.IsAvailable && _dialogBridge.InvokeRequired)
                        _dialogBridge.BeginInvoke(new Action(ShutdownAndExit));
                    else
                        ShutdownAndExit();
                }
                catch (Exception stopEx)
                {
                    Debug.WriteLine($"[TrayDaemonApplicationContext] ApplicationStopping callback ignored: {stopEx.Message}");
                }
            });

            string bindDisplay = ResolveBindDisplay(launchOptions, _managedByMother, _runtime);
            _runtime.Orchestrator.Log("INFO", $"[DesktopHost] Housekeeping Engine Online — listening on {bindDisplay}:{_activePort}");

            _customIcon = TrayMenuFactory.TryLoadAppIcon();
            if (!_managedByMother)
            {
                var menuControls = TrayMenuFactory.Create(
                    _activePort,
                    BaseLocalUrl,
                    _runtime,
                    OpenInBrowser,
                    OpenStandaloneWindow,
                    OpenPreferredDesktopUi,
                    ShowBalloon,
                    RequestEngineShutdown);

                _trayMenu = menuControls.Menu;
                _pauseResumeMenuItem = menuControls.PauseResumeMenuItem;
                _stopCurrentJobMenuItem = menuControls.StopCurrentJobMenuItem;
                _stopEntireQueueMenuItem = menuControls.StopEntireQueueMenuItem;

                _notifyIcon = new NotifyIcon
                {
                    Icon = _customIcon ?? SystemIcons.Application,
                    Text = TrayMenuFactory.TruncateNotifyText($"Housekeeping — Online (:{_activePort})"),
                    ContextMenuStrip = _trayMenu,
                    Visible = true
                };

                _notifyIcon.DoubleClick += (_, _) => OpenPreferredDesktopUi();
            }

            _runtime.Orchestrator.OnTrayNotificationRequested += HandleTrayNotificationRequested;
            _runtime.Orchestrator.OnStateChanged += HandlePipelineStateChanged;

            if (!launchOptions.StartMinimized && !_managedByMother)
                OpenPreferredDesktopUi();
        }

        private string BaseLocalUrl => $"http://127.0.0.1:{_activePort}";

        public void OpenPreferredDesktopUi() => OpenPreferredDesktopUi(BaseLocalUrl);

        public void OpenPreferredDesktopUi(string targetUrl)
        {
            string mode = _runtime.ConfigService.Config.WebServer.DefaultDesktopLaunchMode ?? "StandaloneWindow";
            if (string.Equals(mode, "Browser", StringComparison.OrdinalIgnoreCase))
                OpenInBrowser(targetUrl);
            else
                OpenStandaloneWindow(targetUrl);
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

            _standaloneWindow.FormClosed += (_, _) => _standaloneWindow = null;
            _standaloneWindow.Show();
            _standaloneWindow.BringToFront();
            _standaloneWindow.Activate();
        }

        private static bool ResolveManagedByMother(TrayDaemonLaunchOptions opts)
        {
            if (opts.ManagedByMother) return true;
            if (opts.RawArgs.Any(a => a.Equals("--managed-by-mother", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("--managed-by-suite", StringComparison.OrdinalIgnoreCase)))
                return true;

            foreach (string key in new[] { "HOUSEKEEPING_MANAGED_BY_MOTHER", "MOTHER_MANAGED", "MANAGED_BY_SUITE", "MOTHER_SUPERVISED" })
            {
                string? val = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrWhiteSpace(val) &&
                    (val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("yes", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            return false;
        }

        private static string ResolveBindDisplay(TrayDaemonLaunchOptions opts, bool managedByMother, HousekeepingRuntimeContainer runtime)
        {
            if (managedByMother) return "127.0.0.1 (Mother-managed)";
            if (!string.IsNullOrWhiteSpace(opts.BindHostOverride)) return opts.BindHostOverride.Trim();
            bool bindAll = opts.BindLanOverride ?? runtime.ConfigService.Config.WebServer.BindLanAndTailnet;
            return bindAll ? "0.0.0.0" : "127.0.0.1";
        }

        private static void OpenInBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] OpenInBrowser ignored: {ex.Message}"); }
        }

        private void HandleTrayNotificationRequested(string title, string message, bool isWarning)
        {
            if (_shuttingDown || !_dialogBridge.IsAvailable || _managedByMother || _notifyIcon == null) return;
            try { _dialogBridge.BeginInvoke(new Action(() => ShowBalloon(title, message, isWarning))); }
            catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Notification ignored: {ex.Message}"); }
        }

        private void HandlePipelineStateChanged(PipelineRuntimeStateDto state)
        {
            if (_shuttingDown || !_dialogBridge.IsAvailable || _managedByMother || _notifyIcon == null) return;
            try
            {
                _dialogBridge.BeginInvoke(new Action(() =>
                {
                    if (_shuttingDown || _notifyIcon == null) return;
                    if (_pauseResumeMenuItem != null)
                    {
                        _pauseResumeMenuItem.Enabled = state.IsRunning;
                        _pauseResumeMenuItem.Text = state.IsPaused ? "▶ Resume Paused Queue" : "⏸ Pause Active Queue";
                    }
                    if (_stopCurrentJobMenuItem != null)
                        _stopCurrentJobMenuItem.Enabled = state.IsRunning;
                    if (_stopEntireQueueMenuItem != null)
                        _stopEntireQueueMenuItem.Enabled = state.IsRunning || state.PendingRuns.Count > 0;

                    string tooltip = state.IsRunning
                        ? $"Housekeeping — {(state.IsPaused ? "Paused" : "Running")}: {state.ActiveJobName ?? "Queue"} ({state.ProgressPercent:0}%)"
                        : $"Housekeeping — Online (:{_activePort})";
                    _notifyIcon.Text = TrayMenuFactory.TruncateNotifyText(tooltip);
                }));
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] State update ignored: {ex.Message}"); }
        }

        private void ShowBalloon(string title, string message, bool isWarning)
        {
            if (_shuttingDown || _notifyIcon == null) return;
            try
            {
                _notifyIcon.ShowBalloonTip(
                    4500,
                    string.IsNullOrWhiteSpace(title) ? "Housekeeping" : title,
                    string.IsNullOrWhiteSpace(message) ? "Notification" : message,
                    isWarning ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] ShowBalloon ignored: {ex.Message}"); }
        }

        private void RequestEngineShutdown()
        {
            if (_shuttingDown) return;
            if (_runtime.Orchestrator.IsRunning)
            {
                DialogResult confirm = MessageBox.Show(
                    "A Housekeeping job or queue is currently running.\n\nGracefully abort the active run (preserving the interrupted checkpoint and Up Next queue for recovery) and shut down the Housekeeping engine?",
                    "Confirm Housekeeping Engine Shutdown",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes) return;
            }
            ShutdownAndExit();
        }

        private void ShutdownAndExit()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            _runtime.Orchestrator.OnTrayNotificationRequested -= HandleTrayNotificationRequested;
            _runtime.Orchestrator.OnStateChanged -= HandlePipelineStateChanged;
            _runtime.Orchestrator.InitiateGracefulShutdownAbort("System Tray Shutdown");

            if (_standaloneWindow != null && !_standaloneWindow.IsDisposed)
            {
                try { _standaloneWindow.Close(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Window close ignored: {ex.Message}"); }
                _standaloneWindow = null;
            }

            if (_notifyIcon != null)
            {
                try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] NotifyIcon dispose ignored: {ex.Message}"); }
            }

            try
            {
                Task.Run(async () =>
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                    try { await _webApp.StopAsync(stopCts.Token); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] StopAsync ignored: {ex.Message}"); }
                    try { await _webApp.DisposeAsync(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] DisposeAsync ignored: {ex.Message}"); }
                }).GetAwaiter().GetResult();
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Shutdown task ignored: {ex.Message}"); }

            try { _runtime.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Runtime dispose ignored: {ex.Message}"); }
            try { _trayMenu?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Menu dispose ignored: {ex.Message}"); }
            try { _customIcon?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] Icon dispose ignored: {ex.Message}"); }
            try { _dialogBridge.Close(); _dialogBridge.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[TrayDaemonApplicationContext] DialogBridge dispose ignored: {ex.Message}"); }

            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_shuttingDown)
                ShutdownAndExit();
            base.Dispose(disposing);
        }
    }
}
