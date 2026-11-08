using System;
using System.Diagnostics;
using System.IO;

namespace Housekeeping.Services
{
    /// <summary>
    /// Manages the Windows Task Scheduler keep-alive watchdog for Housekeeping.
    /// Ensures Housekeeping is relaunched minimized to tray if it crashes or is closed,
    /// and ensures it starts automatically upon Windows user logon.
    /// </summary>
    public static class WindowsWatchdogService
    {
        public const string TaskName = "Housekeeping_Watchdog";

        public static bool IsWatchdogRegistered()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{TaskName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static (bool Success, string Message) RegisterWatchdog(int intervalMinutes = 15, bool onLogon = true)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                    ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Housekeeping.exe");

                if (!File.Exists(exePath))
                {
                    return (false, "Could not determine Housekeeping.exe application executable path.");
                }

                // If existing task exists, remove it first
                UnregisterWatchdog();

                // Build action command with --minimized and --watchdog flags
                string actionCmd = $"\\\"{exePath}\\\" --minimized --watchdog";

                // Step 1: Create recurring interval task (e.g. every 15 or 30 minutes)
                int safeInterval = Math.Clamp(intervalMinutes, 5, 1440);

                // Detect if running elevated as Administrator
                bool isAdmin = false;
                try
                {
                    using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[WindowsWatchdogService] Non-fatal error ignored: {__suppressedEx1.Message}"); }

                // Try highest privileges if admin, but always fallback to limited (standard user) to prevent 'Access is denied'
                string[] privilegeModes = isAdmin ? new[] { "highest", "limited" } : new[] { "limited" };
                string lastStdErr = "";
                string lastStdOut = "";
                int lastExitCode = -1;
                bool taskCreated = false;
                string successfulMode = "";

                foreach (var rl in privilegeModes)
                {
                    string args = $"/create /tn \"{TaskName}\" /tr \"{actionCmd}\" /sc minute /mo {safeInterval} /rl {rl} /f";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var p = Process.Start(psi);
                    if (p == null) continue;
                    string stdOut = p.StandardOutput.ReadToEnd();
                    string stdErr = p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);

                    lastExitCode = p.ExitCode;
                    lastStdErr = stdErr;
                    lastStdOut = stdOut;

                    if (p.ExitCode == 0)
                    {
                        taskCreated = true;
                        successfulMode = rl;
                        break;
                    }
                }

                if (!taskCreated)
                {
                    return (false, $"Task Scheduler returned exit code {lastExitCode}: {lastStdErr} {lastStdOut}");
                }

                string modeLabel = successfulMode == "highest" ? "Elevated Administrator task" : "Standard User task";
                return (true, $"Watchdog successfully registered ({modeLabel} checking every {safeInterval} minutes).");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to register watchdog: {ex.Message}");
            }
        }

        public static (bool Success, string Message) UnregisterWatchdog()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/delete /tn \"{TaskName}\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p == null) return (false, "Failed to invoke schtasks.exe.");
                p.WaitForExit(3000);
                return (true, "Watchdog task unregistered successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to unregister watchdog: {ex.Message}");
            }
        }
    }
}
