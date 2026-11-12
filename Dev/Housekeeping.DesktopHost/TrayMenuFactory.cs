// @module: Housekeeping.DesktopHost/TrayMenuFactory
// @purpose: Constructs the Windows System Tray context menu with split Stop Current Job / Stop Entire Queue actions and icon helpers.
// @depends-on: System.Drawing, System.IO, System.Windows.Forms, Housekeeping.Server.Hosting
// @exports: TrayMenuFactory, TrayMenuControls
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Housekeeping.Server.Hosting;

namespace Housekeeping.DesktopHost
{
    internal sealed record TrayMenuControls(
        ContextMenuStrip Menu,
        ToolStripMenuItem PauseResumeMenuItem,
        ToolStripMenuItem StopCurrentJobMenuItem,
        ToolStripMenuItem StopEntireQueueMenuItem);

    internal static class TrayMenuFactory
    {
        public static TrayMenuControls Create(
            int activePort,
            string baseLocalUrl,
            HousekeepingRuntimeContainer runtime,
            Action<string> openInBrowser,
            Action<string?> openStandaloneWindow,
            Action<string> openPreferredDesktopUi,
            Action<string, string, bool> showBalloon,
            Action requestEngineShutdown)
        {
            var menu = new ContextMenuStrip();

            var headerItem = new ToolStripMenuItem($"Housekeeping (:{activePort})")
            {
                Enabled = false,
                Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold)
            };
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("🌐 Open in Browser", null, (_, _) => openInBrowser(baseLocalUrl));
            menu.Items.Add("🪟 Open Standalone Window", null, (_, _) => openStandaloneWindow(baseLocalUrl));
            menu.Items.Add("🛡 Open Archive Compliance Reports", null, (_, _) => openPreferredDesktopUi($"{baseLocalUrl}/#reports"));

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add("▶ Run Active Queue", null, (_, _) =>
            {
                var res = runtime.Orchestrator.StartOrEnqueueQueueRun(null, isDryRun: false, triggerSource: "SystemTray", enqueueIfBusy: true);
                if (!res.Accepted || res.Queued)
                    showBalloon("Housekeeping", res.Message, !res.Accepted);
            });

            menu.Items.Add("🧪 Dry-Run Active Queue", null, (_, _) =>
            {
                var res = runtime.Orchestrator.StartOrEnqueueQueueRun(null, isDryRun: true, triggerSource: "SystemTrayDryRun", enqueueIfBusy: true);
                if (!res.Accepted || res.Queued)
                    showBalloon("Housekeeping", res.Message, !res.Accepted);
            });

            var pauseResumeItem = new ToolStripMenuItem("⏸ Pause / Resume Queue", null, (_, _) =>
            {
                runtime.Orchestrator.TogglePause();
            })
            {
                Enabled = false
            };
            menu.Items.Add(pauseResumeItem);

            var stopCurrentJobItem = new ToolStripMenuItem("⏹ Stop Current Job", null, (_, _) =>
            {
                runtime.Orchestrator.StopCurrentJob();
            })
            {
                Enabled = false
            };
            menu.Items.Add(stopCurrentJobItem);

            var stopEntireQueueItem = new ToolStripMenuItem("⏹ Stop Entire Queue", null, (_, _) =>
            {
                runtime.Orchestrator.StopActiveRun();
            })
            {
                Enabled = false
            };
            menu.Items.Add(stopEntireQueueItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("⏻ Shut Down Housekeeping Engine", null, (_, _) => requestEngineShutdown());

            return new TrayMenuControls(menu, pauseResumeItem, stopCurrentJobItem, stopEntireQueueItem);
        }

        public static Icon? TryLoadAppIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_icon.ico");
                if (File.Exists(icoPath))
                    return new Icon(icoPath);

                string exePath = Environment.ProcessPath ?? "";
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                    return Icon.ExtractAssociatedIcon(exePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TrayMenuFactory] TryLoadAppIcon ignored: {ex.Message}");
            }
            return null;
        }

        public static string TruncateNotifyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Housekeeping";
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }
    }
}
