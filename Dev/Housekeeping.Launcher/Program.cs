using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Housekeeping.Launcher;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    public static int Main(string[] args)
    {
        string rootDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string appExe = Path.Combine(rootDir, "app", "Housekeeping.exe");
        if (!File.Exists(appExe))
        {
            // Fallback for development / unnested layouts
            string altExe = Path.Combine(rootDir, "app", "Housekeeping.DesktopHost.exe");
            if (File.Exists(altExe))
            {
                appExe = altExe;
            }
            else if (File.Exists(Path.Combine(rootDir, "Housekeeping.DesktopHost.exe")))
            {
                appExe = Path.Combine(rootDir, "Housekeeping.DesktopHost.exe");
            }
            else
            {
                if (OperatingSystem.IsWindows())
                {
                    try { AttachConsole(AttachParentProcess); } catch (Exception ex) { Debug.WriteLine($"[Housekeeping.Launcher] AttachConsole fallback ignored: {ex.Message}"); }
                }
                Console.Error.WriteLine($"Housekeeping application binary not found at '{appExe}'.");
                return 1;
            }
        }

        bool isCliOrSyncWait = ShouldWaitForChild(args);
        if (isCliOrSyncWait && OperatingSystem.IsWindows())
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    if (AttachConsole(AttachParentProcess))
                    {
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Housekeeping.Launcher] AttachConsole ignored: {ex.Message}");
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = rootDir,
            UseShellExecute = false
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using Process? proc = Process.Start(psi);
        if (proc == null)
        {
            return 1;
        }

        if (isCliOrSyncWait)
        {
            proc.WaitForExit();
            return proc.ExitCode;
        }

        return 0;
    }

    private static bool ShouldWaitForChild(string[] args)
    {
        if (args.Length == 0)
            return false;

        foreach (string raw in args)
        {
            string a = raw.Trim();
            if (a.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--status", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--repair-config", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--headless", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--test-mode", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string first = args[0].Trim();
        if (!first.StartsWith("-", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
