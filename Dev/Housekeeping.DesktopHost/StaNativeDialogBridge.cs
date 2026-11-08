using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Housekeeping.Server.Hosting;

namespace Housekeeping.DesktopHost
{
    /// <summary>
    /// Hidden STA WinForms message-only window that bridges Kestrel localhost HTTP requests
    /// to native Windows OS FolderBrowserDialog / OpenFileDialog instances and listens for
    /// single-instance IPC wakeup broadcasts.
    /// </summary>
    public sealed class StaNativeDialogBridge : Form, INativeDialogBridge
    {
        public static readonly uint RestoreWindowMessageId = RegisterWindowMessage("HOUSEKEEPING_RESTORE_WINDOW");
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public event Action? RestoreUiRequested;
        public event Action? OpenStandaloneWindowRequested;

        public StaNativeDialogBridge()
        {
            Text = "Housekeeping_StaBridge";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;

            // Force handle creation on the STA thread immediately so BeginInvoke and WndProc are active.
            _ = Handle;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public static void BroadcastRestoreMessage()
        {
            if (RestoreWindowMessageId != 0)
            {
                PostMessage(HWND_BROADCAST, RestoreWindowMessageId, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public bool IsAvailable => !IsDisposed && IsHandleCreated;

        public Task<string?> PickFolderAsync(string? initialPath, string? description)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!IsAvailable)
            {
                tcs.TrySetResult(null);
                return tcs.Task;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using var topOwner = CreateTransientTopMostOwner();
                        using var dlg = new FolderBrowserDialog
                        {
                            Description = string.IsNullOrWhiteSpace(description)
                                ? "Select folder for Housekeeping"
                                : description,
                            UseDescriptionForTitle = true,
                            ShowNewFolderButton = true
                        };

                        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                        {
                            dlg.SelectedPath = initialPath;
                            dlg.InitialDirectory = initialPath;
                        }

                        DialogResult result = dlg.ShowDialog(topOwner);
                        tcs.TrySetResult(result == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath)
                            ? dlg.SelectedPath
                            : null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        public Task<string?> PickFileAsync(string? initialPath, string? filter, string? title)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!IsAvailable)
            {
                tcs.TrySetResult(null);
                return tcs.Task;
            }

            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using var topOwner = CreateTransientTopMostOwner();
                        using var dlg = new OpenFileDialog
                        {
                            Title = string.IsNullOrWhiteSpace(title) ? "Select file for Housekeeping" : title,
                            Filter = string.IsNullOrWhiteSpace(filter) ? "All Files (*.*)|*.*" : filter,
                            CheckFileExists = true,
                            RestoreDirectory = true
                        };

                        if (!string.IsNullOrWhiteSpace(initialPath))
                        {
                            if (Directory.Exists(initialPath))
                            {
                                dlg.InitialDirectory = initialPath;
                            }
                            else if (File.Exists(initialPath))
                            {
                                dlg.InitialDirectory = Path.GetDirectoryName(initialPath) ?? "";
                                dlg.FileName = Path.GetFileName(initialPath);
                            }
                        }

                        DialogResult result = dlg.ShowDialog(topOwner);
                        tcs.TrySetResult(result == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.FileName)
                            ? dlg.FileName
                            : null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        public void RequestOpenStandaloneWindow()
        {
            if (!IsAvailable) return;
            try
            {
                BeginInvoke(new Action(() => OpenStandaloneWindowRequested?.Invoke()));
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[StaNativeDialogBridge] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        protected override void WndProc(ref Message m)
        {
            if (RestoreWindowMessageId != 0 && (uint)m.Msg == RestoreWindowMessageId)
            {
                RestoreUiRequested?.Invoke();
                return;
            }

            base.WndProc(ref m);
        }

        private static Form CreateTransientTopMostOwner()
        {
            var owner = new Form
            {
                TopMost = true,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.CenterScreen,
                Size = new Size(1, 1),
                Opacity = 0
            };
            owner.Show();
            owner.Activate();
            return owner;
        }
    }
}
