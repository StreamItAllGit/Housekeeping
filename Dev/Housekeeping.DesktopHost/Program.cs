using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;

namespace Housekeeping.DesktopHost
{
    internal static class Program
    {
        private const string CanonicalSingleInstanceMutexName = @"Global\Housekeeping_2_SingleInstance_Mutex";
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [STAThread]
        public static int Main(string[] args)
        {
            var cli = HousekeepingCliParser.Parse(args);

            if (!string.IsNullOrWhiteSpace(cli.ParseError))
            {
                TryAttachParentConsole();
                Console.Error.WriteLine($"Error: {cli.ParseError}");
                Console.Error.WriteLine("Run with --help for usage information.");
                return 2;
            }

            if (cli.ShowVersion)
            {
                TryAttachParentConsole();
                Console.Out.WriteLine(RemoteNodeDeploymentService.GetLocalAppVersion());
                Console.Out.Flush();
                return 0;
            }

            if (cli.ShowHelp)
            {
                TryAttachParentConsole();
                Console.Out.Write(HousekeepingCliParser.BuildHelpText("Housekeeping.exe"));
                Console.Out.Flush();
                return 0;
            }

            string mutexName = ResolveMutexName(cli);
            using var singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                if (!cli.StartMinimized && !cli.IsWatchdog && !cli.Headless && !cli.TestMode)
                {
                    StaNativeDialogBridge.BroadcastRestoreMessage();
                }
                return 0;
            }

            string baseTrimmed = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string defaultLogsRoot = string.Equals(Path.GetFileName(baseTrimmed), "app", StringComparison.OrdinalIgnoreCase) &&
                                     Directory.GetParent(baseTrimmed) is { } baseParent
                ? baseParent.FullName
                : baseTrimmed;

            string initialLogsDir = !string.IsNullOrWhiteSpace(cli.CustomLogsDirectory)
                ? cli.CustomLogsDirectory
                : (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory)
                    ? Path.Combine(cli.CustomDataDirectory, "logs")
                    : Path.Combine(defaultLogsRoot, "logs"));

            CrashBundleService.RegisterGlobalHandlers(
                () => initialLogsDir,
                RemoteNodeDeploymentService.GetLocalAppVersion(),
                cli.TestMode,
                archetype: "2-tray-companion");

            try
            {
                if (cli.Headless)
                {
                    var app = HousekeepingServerHost.BuildWebApplication(cli.RemainingArgs, new HousekeepingServerOptions
                    {
                        PortOverride = cli.PortOverride,
                        BindLanOverride = cli.BindLanOverride,
                        CustomDataDirectory = cli.CustomDataDirectory,
                        CustomConfigFilePath = cli.CustomConfigFilePath,
                        CustomLogsDirectory = cli.CustomLogsDirectory,
                        TestMode = cli.TestMode,
                        DryRun = cli.DryRun,
                        StartBackgroundTimers = true
                    });
                    app.Run();
                    return 0;
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (_, e) =>
                {
                    try
                    {
                        if (e.Exception != null)
                        {
                            CrashBundleService.WriteCrashBundle(e.Exception);
                        }
                    }
                    catch (Exception threadLogEx)
                    {
                        Debug.WriteLine($"[Program] ThreadException crash bundle write failed: {threadLogEx.Message}");
                    }
                };

                ApplicationConfiguration.Initialize();

                using var trayContext = new TrayDaemonApplicationContext(new TrayDaemonLaunchOptions
                {
                    RawArgs = cli.RemainingArgs,
                    StartMinimized = cli.StartMinimized || cli.TestMode,
                    PortOverride = cli.PortOverride,
                    BindLanOverride = cli.BindLanOverride,
                    CustomDataDirectory = cli.CustomDataDirectory,
                    CustomConfigFilePath = cli.CustomConfigFilePath,
                    CustomLogsDirectory = cli.CustomLogsDirectory,
                    TestMode = cli.TestMode,
                    DryRun = cli.DryRun,
                    ManagedByMother = cli.ManagedByMother
                });

                Application.Run(trayContext);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    CrashBundleService.WriteCrashBundle(ex, logsDirOverride: initialLogsDir);
                    string crashDir = !string.IsNullOrWhiteSpace(cli.CustomLogsDirectory)
                        ? cli.CustomLogsDirectory
                        : (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory)
                            ? cli.CustomDataDirectory
                            : AppDomain.CurrentDomain.BaseDirectory);
                    Directory.CreateDirectory(crashDir);
                    File.AppendAllText(
                        Path.Combine(crashDir, "crash_desktophost.log"),
                        $"[{DateTime.Now:O}] Fatal startup exception: {CrashBundleService.SanitizeText(ex.ToString())}{Environment.NewLine}");
                }
                catch (Exception logEx)
                {
                    Debug.WriteLine($"[Program] Crash log write failed: {logEx.Message}");
                }

                if (!cli.Headless && !cli.IsWatchdog && !cli.StartMinimized && !cli.TestMode)
                {
                    MessageBox.Show(
                        $"Housekeeping failed to start:\n\n{ex.Message}",
                        "Housekeeping Startup Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return 1;
            }
            finally
            {
                try
                {
                    singleInstanceMutex.ReleaseMutex();
                }
                catch (Exception mutexEx)
                {
                    Debug.WriteLine($"[Program] ReleaseMutex skipped: {mutexEx.Message}");
                }
            }
        }

        private static void TryAttachParentConsole()
        {
            try
            {
                if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                {
                    if (AttachConsole(ATTACH_PARENT_PROCESS))
                    {
                        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Program] AttachConsole skipped: {ex.Message}");
            }
        }

        private static string ResolveMutexName(HousekeepingParsedCli cli)
        {
            if (!cli.IsSandboxedInvocation)
            {
                return CanonicalSingleInstanceMutexName;
            }

            string baseRef = !string.IsNullOrWhiteSpace(cli.CustomDataDirectory)
                ? Path.GetFullPath(cli.CustomDataDirectory)
                : (!string.IsNullOrWhiteSpace(cli.CustomConfigFilePath)
                    ? Path.GetFullPath(cli.CustomConfigFilePath)
                    : AppDomain.CurrentDomain.BaseDirectory);
            string logRef = !string.IsNullOrWhiteSpace(cli.CustomLogsDirectory)
                ? Path.GetFullPath(cli.CustomLogsDirectory)
                : "";
            string scopeKey = $"{baseRef.ToLowerInvariant()}|{logRef.ToLowerInvariant()}|{cli.PortOverride?.ToString() ?? "default"}|{cli.Headless}|{cli.TestMode}|{cli.DryRun}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey));
            string hex12 = Convert.ToHexString(hash).Substring(0, 12);
            return $@"Global\Housekeeping_3_Scoped_{hex12}";
        }
    }
}
