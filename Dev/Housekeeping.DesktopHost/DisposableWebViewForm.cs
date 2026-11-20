using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Housekeeping.DesktopHost
{
    /// <summary>
    /// Native standalone Windows shell hosting Housekeeping 3.0 via WebView2.
    /// Closing this window disposes the WebView2 instance completely (freeing 100% of
    /// msedgewebview2.exe Chromium processes and RAM) while the Kestrel + Tray daemon
    /// continues running undisturbed in the background.
    /// </summary>
    public sealed class DisposableWebViewForm : Form
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static int _loaderDllConfigured;

        private WebView2? _webView;
        private readonly string _initialUrl;
        private readonly int _localServerPort;
        private readonly string? _customUserDataRoot;
        private readonly Action<string>? _onFallbackToBrowser;
        private bool _isCoreReady;

#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
        private WebViewAutomationKeySubClass? _webViewKeyHook;
        internal WebView2? WebViewControl => _webView;
#endif

        public DisposableWebViewForm(
            string initialUrl,
            int localServerPort,
            Icon? appIcon = null,
            Action<string>? onFallbackToBrowser = null,
            string? customUserDataRoot = null)
        {
            _initialUrl = initialUrl;
            _localServerPort = localServerPort;
            _customUserDataRoot = customUserDataRoot;
            _onFallbackToBrowser = onFallbackToBrowser;

            Name = "HousekeepingMainForm";
            Text = "Housekeeping";
            Size = new Size(1480, 930);
            MinimumSize = new Size(920, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(9, 14, 23); // #090e17 Sister-App dark canvas
            ForeColor = Color.FromArgb(241, 245, 249);

            if (appIcon != null)
            {
                Icon = appIcon;
            }

            _webView = new WebView2
            {
                Name = "HousekeepingWebView",
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(9, 14, 23)
            };

            Controls.Add(_webView);

#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
            _webView.HandleCreated += (_, _) => AttachWebViewKeyHook();
            _webView.HandleDestroyed += (_, _) => DetachWebViewKeyHook();
            DesktopAutomationBridge.RegisterForm(this);
#endif
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryEnableImmersiveDarkTitleBar(Handle);
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_webView == null || IsDisposed) return;

            try
            {
                string archFolder = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string nativeLoaderDir = Path.Combine(AppContext.BaseDirectory, "runtimes", archFolder, "native");
                if (File.Exists(Path.Combine(nativeLoaderDir, "WebView2Loader.dll")) &&
                    Interlocked.Exchange(ref _loaderDllConfigured, 1) == 0)
                {
                    CoreWebView2Environment.SetLoaderDllFolderPath(nativeLoaderDir);
                }

                string userDataDir = !string.IsNullOrWhiteSpace(_customUserDataRoot)
                    ? Path.Combine(Path.GetFullPath(_customUserDataRoot), "webview2_cache")
                    : Path.Combine(
                        KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Housekeeping",
                        "webview2_cache");
                Directory.CreateDirectory(userDataDir);

                var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                if (_webView == null || IsDisposed) return;

                await _webView.EnsureCoreWebView2Async(env);
                if (_webView == null || IsDisposed || _webView.CoreWebView2 == null) return;

                _isCoreReady = true;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.NewWindowRequested += HandleNewWindowRequested;
                _webView.Source = new Uri(_initialUrl);
#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
                DesktopAutomationBridge.NotifyWebViewReady(this);
#endif
            }
            catch (Exception ex)
            {
                string fallbackUrl = _initialUrl;
                _onFallbackToBrowser?.Invoke($"WebView2 Runtime unavailable ({ex.Message}); opening in default browser.");
                if (!ShouldSuppressExternalLaunch())
                {
                    OpenExternalUrl(fallbackUrl);
                }
                if (!IsDisposed)
                {
                    Close();
                }
            }
        }

        public void NavigateAndFocus(string url)
        {
            if (IsDisposed) return;

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Show();
            BringToFront();
            Activate();

            if (_webView != null && _isCoreReady && _webView.CoreWebView2 != null && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    _webView.CoreWebView2.Navigate(url);
                }
                catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[DisposableWebViewForm] Non-fatal error ignored: {__suppressedEx1.Message}"); }
            }
        }

        private void HandleNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri)) return;

            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? target))
            {
                bool isSameLocalServer =
                    (string.Equals(target.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(target.Host, "localhost", StringComparison.OrdinalIgnoreCase)) &&
                    target.Port == _localServerPort;

                if (!isSameLocalServer)
                {
                    e.Handled = true;
                    if (!ShouldSuppressExternalLaunch())
                    {
                        OpenExternalUrl(e.Uri);
                    }
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
            DetachWebViewKeyHook();
            DesktopAutomationBridge.UnregisterForm(this);
#endif
            var view = _webView;
            _webView = null;
            _isCoreReady = false;

            if (view != null)
            {
                try
                {
                    if (view.CoreWebView2 != null)
                    {
                        view.CoreWebView2.NewWindowRequested -= HandleNewWindowRequested;
                    }
                }
                catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[DisposableWebViewForm] Non-fatal error ignored: {__suppressedEx2.Message}"); }

                try
                {
                    Controls.Remove(view);
                    view.Dispose();
                }
                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[DisposableWebViewForm] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            }

            base.OnFormClosed(e);

            // Reclaim managed wrapper references promptly after WebView2 subprocess teardown
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0100 && DesktopAutomationBridge.HandleAutomationKeyDown(this, m.WParam.ToInt32()))
            {
                return;
            }
            base.WndProc(ref m);
        }

        internal void EnableAutomationViewportControl()
        {
            if (!IsDisposed)
            {
                MinimumSize = new Size(360, 540);
            }
        }

        internal void ApplyAutomationViewport(int width, int height)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ApplyAutomationViewport(width, height)));
                return;
            }

            EnableAutomationViewportControl();
            if (WindowState != FormWindowState.Normal)
            {
                WindowState = FormWindowState.Normal;
            }
            ClientSize = new Size(Math.Max(360, width), Math.Max(540, height));
            PerformLayout();
        }

        internal async Task DispatchSpaKeyAsync(string key, bool ctrl)
        {
            if (IsDisposed || !_isCoreReady || _webView?.CoreWebView2 == null) return;
            string escaped = key.Replace("\\", "\\\\").Replace("'", "\\'");
            string ctrlLit = ctrl ? "true" : "false";
            string script = $"window.dispatchEvent(new KeyboardEvent('keydown', {{ key: '{escaped}', ctrlKey: {ctrlLit}, bubbles: true }}));";
            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(script).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException)
            {
                Debug.WriteLine($"[DisposableWebViewForm] Automation script dispatch skipped: {ex.Message}");
            }
        }

        private void AttachWebViewKeyHook()
        {
            if (_webView is { IsDisposed: false, IsHandleCreated: true } && _webViewKeyHook is null)
            {
                _webViewKeyHook = new WebViewAutomationKeySubClass(this, _webView.Handle);
            }
        }

        private void DetachWebViewKeyHook()
        {
            if (_webViewKeyHook is not null)
            {
                _webViewKeyHook.ReleaseHandle();
                _webViewKeyHook = null;
            }
        }

        private sealed class WebViewAutomationKeySubClass : NativeWindow
        {
            private readonly DisposableWebViewForm _owner;

            public WebViewAutomationKeySubClass(DisposableWebViewForm owner, IntPtr handle)
            {
                _owner = owner;
                AssignHandle(handle);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0100 && DesktopAutomationBridge.HandleAutomationKeyDown(_owner, m.WParam.ToInt32()))
                {
                    return;
                }
                base.WndProc(ref m);
            }
        }
#endif

        private static bool ShouldSuppressExternalLaunch()
        {
            if (SandboxEnvironmentSeam.ResolveCurrent().IsSandboxActive) return true;
            string? suppress = Environment.GetEnvironmentVariable("FOUNDATION_SUPPRESS_MODALS");
            return !string.IsNullOrWhiteSpace(suppress) &&
                   (suppress.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                    suppress.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        private static void TryEnableImmersiveDarkTitleBar(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int useDark = 1;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            }
            catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[DisposableWebViewForm] Non-fatal error ignored: {__suppressedEx4.Message}"); }
        }

        private static void OpenExternalUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[DisposableWebViewForm] Non-fatal error ignored: {__suppressedEx5.Message}"); }
        }
    }
}
