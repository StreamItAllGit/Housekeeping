// @module: Housekeeping.DesktopHost.DesktopAutomationBridge
// @purpose: Debug/TestAutomation-only Foundation.Automation loopback bridge, WebView2 CapturePreviewAsync snapshot provider, and interactive UI/navigation state dispatch.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Drawing.Imaging, System.IO, System.Runtime.InteropServices, System.Threading, System.Threading.Tasks, System.Windows.Forms, Foundation.Automation, Microsoft.Web.WebView2.Core, Housekeeping.Server.Hosting
// @exports: DesktopAutomationBridge

#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Foundation.Automation;
using Housekeeping.Server.Hosting;
using Microsoft.Web.WebView2.Core;

namespace Housekeeping.DesktopHost
{
    /// <summary>
    /// Hosts opt-in 127.0.0.1 Foundation.Automation loopback inspection, WebView2 PNG capture,
    /// and window-scoped keyboard/viewport navigation dispatch strictly in Debug/TestAutomation builds.
    /// </summary>
    internal sealed class DesktopAutomationBridge : IRenderSurfaceSnapshotProvider, IDisposable
    {
        private static readonly object InstanceLock = new();
        private static DesktopAutomationBridge? _instance;
        private static DisposableWebViewForm? _activeForm;

        private readonly Control? _ownedAnchor;
        private readonly System.Windows.Forms.Timer _refreshTimer;
        private readonly object _frameSync = new();
        private AutomationLoopbackServer? _server;
        private CoreWebView2? _hookedCore;
        private byte[]? _latestRgba;
        private int _latestWidth, _latestHeight, _captureInFlight, _disposed;

        internal static bool IsAutomationActive
        {
            get { lock (InstanceLock) return _instance is not null; }
        }

        private DesktopAutomationBridge(Control? ownedAnchor)
        {
            _ownedAnchor = ownedAnchor;
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _refreshTimer.Tick += (_, _) => QueueCaptureOnUi();
        }

        public static DesktopAutomationBridge? TryStart(string[]? args, HousekeepingParsedCli cli)
        {
            if (!cli.IsAutomationRequested && !HousekeepingCliParser.IsAutomationRequested(args)) return null;
            if (!cli.AutomationPort.HasValue) return null;

            var helperArgs = new List<string> { "--automation-port", cli.AutomationPort.Value.ToString() };
            if (!string.IsNullOrWhiteSpace(cli.AutomationToken))
            {
                helperArgs.Add("--automation-token");
                helperArgs.Add(cli.AutomationToken);
            }

            DisposableWebViewForm? formSnapshot;
            lock (InstanceLock) formSnapshot = _activeForm;
            Control? ownedAnchor = null;
            Control rootControl = formSnapshot is { IsDisposed: false } ? formSnapshot : (ownedAnchor = CreateAnchorControl());

            var bridge = new DesktopAutomationBridge(ownedAnchor);
            AutomationLoopbackServer? server = AutomationCliHelper.TryCreateFromArgs("housekeeping", helperArgs.ToArray(), rootControl, bridge, allowedSnapshotRoot: null);
            if (server is null)
            {
                bridge.Dispose();
                return null;
            }

            bridge._server = server;
            lock (InstanceLock) _instance = bridge;
            if (formSnapshot is { IsDisposed: false })
            {
                formSnapshot.EnableAutomationViewportControl();
                bridge.AttachForm(formSnapshot);
            }
            server.Start();
            bridge._refreshTimer.Start();
            bridge.QueueCaptureOnUi();
            return bridge;
        }

        internal static void RegisterForm(DisposableWebViewForm form)
        {
            DesktopAutomationBridge? active;
            lock (InstanceLock) { _activeForm = form; active = _instance; }
            if (active is null) return;
            form.EnableAutomationViewportControl();
            active.AttachForm(form);
        }

        internal static void UnregisterForm(DisposableWebViewForm form)
        {
            DesktopAutomationBridge? active;
            lock (InstanceLock)
            {
                if (!ReferenceEquals(_activeForm, form)) return;
                _activeForm = null;
                active = _instance;
            }
            active?.DetachCore();
        }

        internal static void NotifyWebViewReady(DisposableWebViewForm form)
        {
            DesktopAutomationBridge? active;
            lock (InstanceLock) { if (!ReferenceEquals(_activeForm, form)) return; active = _instance; }
            active?.AttachForm(form);
        }

        internal static void InvalidateAndRefresh()
        {
            DesktopAutomationBridge? active;
            lock (InstanceLock) active = _instance;
            if (active is null) return;
            lock (active._frameSync) { active._latestRgba = null; active._latestWidth = 0; active._latestHeight = 0; }
            active.RequestRefresh();
        }

        internal static bool HandleAutomationKeyDown(DisposableWebViewForm? form, int virtualKey)
        {
            if (form is null) { lock (InstanceLock) form = _activeForm; }
            if (!IsAutomationActive || form is null || form.IsDisposed) return false;

            if (virtualKey is >= 0x31 and <= 0x37)
            {
                _ = form.DispatchSpaKeyAsync(((char)virtualKey).ToString(), ctrl: false);
                _ = Task.Delay(150).ContinueWith(_ => InvalidateAndRefresh(), TaskScheduler.Default);
                return true;
            }
            if (virtualKey == 0x75) { _ = form.DispatchSpaKeyAsync("k", ctrl: true); _ = Task.Delay(150).ContinueWith(_ => InvalidateAndRefresh(), TaskScheduler.Default); return true; }
            if (virtualKey == 0x1B) { _ = form.DispatchSpaKeyAsync("Escape", ctrl: false); _ = Task.Delay(120).ContinueWith(_ => InvalidateAndRefresh(), TaskScheduler.Default); return true; }
            if (virtualKey == 0x77) { form.ApplyAutomationViewport(412, 915); InvalidateAndRefresh(); return true; }
            if (virtualKey == 0x78) { form.ApplyAutomationViewport(1920, 1080); InvalidateAndRefresh(); return true; }
            if (virtualKey == 0x74) { form.WebViewControl?.CoreWebView2?.Reload(); InvalidateAndRefresh(); return true; }
            return false;
        }

        public bool TryCaptureRgba(out int width, out int height, out byte[] rgbaBytes)
        {
            DisposableWebViewForm? form;
            lock (InstanceLock) form = _activeForm;
            bool needRefresh = false;
            if (form is { IsDisposed: false, Visible: true } && form.WebViewControl is { IsDisposed: false, Visible: true, CoreWebView2: not null })
            {
                lock (_frameSync)
                {
                    if (_latestRgba is not null && _latestWidth > 0 && _latestHeight > 0)
                    {
                        width = _latestWidth; height = _latestHeight; rgbaBytes = (byte[])_latestRgba.Clone();
                        return true;
                    }
                    needRefresh = true;
                }
            }
            if (needRefresh) RequestRefresh();
            width = 0; height = 0; rgbaBytes = Array.Empty<byte>();
            return false;
        }

        private void RequestRefresh()
        {
            DisposableWebViewForm? form;
            lock (InstanceLock) form = _activeForm;
            if (Volatile.Read(ref _disposed) != 0 || form is null || form.IsDisposed || !form.IsHandleCreated) return;
            try { form.BeginInvoke(QueueCaptureOnUi); }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { _ = ex; }
        }

        private void AttachForm(DisposableWebViewForm form)
        {
            if (form.IsDisposed || form.WebViewControl is not { CoreWebView2: { } core }) return;
            if (!ReferenceEquals(_hookedCore, core))
            {
                DetachCore();
                _hookedCore = core;
                core.DOMContentLoaded += OnDomOrNavReady;
                core.NavigationCompleted += OnDomOrNavReady;
            }
            QueueCaptureOnUi();
        }

        private void DetachCore()
        {
            if (_hookedCore is null) return;
            try { _hookedCore.DOMContentLoaded -= OnDomOrNavReady; _hookedCore.NavigationCompleted -= OnDomOrNavReady; }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException) { _ = ex; }
            _hookedCore = null;
        }

        private void OnDomOrNavReady(object? sender, object e) => QueueCaptureOnUi();
        private void QueueCaptureOnUi() { if (Volatile.Read(ref _disposed) == 0) _ = CaptureFrameAsync(); }

        private async Task CaptureFrameAsync()
        {
            DisposableWebViewForm? form;
            lock (InstanceLock) form = _activeForm;
            if (Volatile.Read(ref _disposed) != 0 || form is null || form.IsDisposed || !form.Visible ||
                form.WebViewControl is not { IsDisposed: false, Visible: true, CoreWebView2: { } core } wv ||
                wv.Width <= 0 || wv.Height <= 0) return;
            if (Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0) return;

            try
            {
                byte[] pngBytes;
                using (var ms = new MemoryStream())
                {
                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms).ConfigureAwait(true);
                    pngBytes = ms.ToArray();
                }
                if (pngBytes.Length == 0 || Volatile.Read(ref _disposed) != 0) return;
                (int width, int height, byte[] rgba) = await Task.Run(() => DecodePngToRgba(pngBytes)).ConfigureAwait(false);
                if (width <= 0 || height <= 0 || rgba.Length == 0 || Volatile.Read(ref _disposed) != 0) return;
                lock (_frameSync) { _latestWidth = width; _latestHeight = height; _latestRgba = rgba; }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or COMException) { _ = ex; }
            finally { Interlocked.Exchange(ref _captureInFlight, 0); }
        }

        private static (int Width, int Height, byte[] Rgba) DecodePngToRgba(byte[] pngBytes)
        {
            using var ms = new MemoryStream(pngBytes, writable: false);
            using var bmp = new Bitmap(ms);
            int w = bmp.Width, h = bmp.Height;
            if (w <= 0 || h <= 0) return (0, 0, Array.Empty<byte>());
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int absStride = Math.Abs(data.Stride);
                byte[] rowBuf = new byte[absStride], rgba = new byte[checked(w * h * 4)];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Stride >= 0 ? IntPtr.Add(data.Scan0, y * data.Stride) : IntPtr.Add(data.Scan0, (h - 1 - y) * absStride), rowBuf, 0, absStride);
                    for (int x = 0, dstRow = y * w * 4; x < w; x++)
                    {
                        int s = x * 4, d = dstRow + s;
                        rgba[d] = rowBuf[s + 2]; rgba[d + 1] = rowBuf[s + 1]; rgba[d + 2] = rowBuf[s]; rgba[d + 3] = rowBuf[s + 3];
                    }
                }
                return (w, h, rgba);
            }
            finally { bmp.UnlockBits(data); }
        }

        private static Control CreateAnchorControl()
        {
            var anchor = new AutomationAnchorControl { Name = "HousekeepingAutomationHost", Width = 1, Height = 1, Visible = false };
            _ = anchor.Handle;
            return anchor;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (InstanceLock) { if (ReferenceEquals(_instance, this)) _instance = null; }
            DetachCore();
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _server?.Dispose();
            _ownedAnchor?.Dispose();
        }

        private sealed class AutomationAnchorControl : Control
        {
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0100 && HandleAutomationKeyDown(null, m.WParam.ToInt32())) return;
                base.WndProc(ref m);
            }
        }
    }
}
#endif
