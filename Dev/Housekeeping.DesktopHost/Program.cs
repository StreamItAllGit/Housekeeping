using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Foundation.Core;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;

namespace Housekeeping.DesktopHost
{
    internal static class Program
    {
        private const string CanonicalSingleInstanceMutexName = @"Global\Mother_Housekeeping_Mutex";
        private const int ATTACH_PARENT_PROCESS = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [STAThread]
        public static int Main(string[] args)
        {
#if !(DEBUG || HOUSEKEEPING_TEST_AUTOMATION)
            if (HousekeepingCliParser.IsAutomationRequested(args))
            {
                TryAttachParentConsole();
                Console.Error.WriteLine(
                    "Fail-closed: --automation-port and --automation-token are disabled in Release builds. " +
                    "Build with Debug or -p:HousekeepingTestAutomation=true (ExitCode=2).");
                Console.Error.Flush();
                return 2;
            }
#endif

            var cli = HousekeepingCliParser.Parse(args);

            if (!string.IsNullOrWhiteSpace(cli.ParseError))
            {
                TryAttachParentConsole();
                Console.Error.WriteLine($"Error: {cli.ParseError}");
                Console.Error.WriteLine("Run with --help for usage information.");
                Console.Error.Flush();
                return 2;
            }

#if !(DEBUG || HOUSEKEEPING_TEST_AUTOMATION)
            if (cli.IsAutomationRequested)
            {
                TryAttachParentConsole();
                Console.Error.WriteLine("Fail-closed: --automation-* options are disabled in Release builds (ExitCode=2).");
                Console.Error.Flush();
                return 2;
            }
#endif

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

            if (cli.RunStatus)
            {
                TryAttachParentConsole();
                return HousekeepingCliRunner.RunStatusCheck(cli);
            }

            if (cli.RunRepairConfig)
            {
                TryAttachParentConsole();
                return HousekeepingCliRunner.RunRepairConfig(cli);
            }

            if (cli.RunDoctor)
            {
                TryAttachParentConsole();
                return HousekeepingCliRunner.RunDoctor(cli);
            }

            SandboxResolution sandbox;
            try
            {
                sandbox = SandboxEnvironmentSeam.ResolveCurrent();
                if (sandbox.IsSandboxActive)
                {
                    if (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory))
                        SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomDataDirectory, sandbox);
                    if (!string.IsNullOrWhiteSpace(cli.CustomConfigFilePath))
                        SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomConfigFilePath, sandbox);
                    if (!string.IsNullOrWhiteSpace(cli.CustomLogsDirectory))
                        SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomLogsDirectory, sandbox);
                }
            }
            catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
            {
                TryAttachParentConsole();
                Console.Error.WriteLine($"Sandbox containment violation (ExitCode={SandboxEnvironmentSeam.ContainmentExitCode}): {ex.Message}");
                Console.Error.Flush();
                return SandboxEnvironmentSeam.ContainmentExitCode;
            }

            string mutexName = ResolveMutexName(cli, sandbox);
            using var singleInstanceMutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                if (!cli.StartMinimized && !cli.IsWatchdog)
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

            Housekeeping.Services.CrashBundleService.RegisterGlobalHandlers(
                () => initialLogsDir,
                RemoteNodeDeploymentService.GetLocalAppVersion(),
                cli.IsIsolatedInvocation || sandbox.IsSandboxActive,
                archetype: "2-tray-companion");

            string? effectiveBindHost = cli.ManagedByMother ? "127.0.0.1" : cli.BindHost;
            bool? effectiveBindLan = cli.ManagedByMother
                ? false
                : HousekeepingCliParser.ResolveEffectiveBindLanOverride(effectiveBindHost, cli.BindLanOverride);

            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (_, e) =>
                {
                    try
                    {
                        if (e.Exception != null)
                        {
                            Housekeeping.Services.CrashBundleService.WriteCrashBundle(e.Exception);
                        }
                    }
                    catch (Exception threadLogEx)
                    {
                        Debug.WriteLine($"[Program] ThreadException crash bundle write failed: {threadLogEx.Message}");
                    }
                };

                ApplicationConfiguration.Initialize();

                var launchOptions = new TrayDaemonLaunchOptions
                {
                    RawArgs = cli.RemainingArgs,
                    StartMinimized = cli.StartMinimized,
                    PortOverride = cli.PortOverride,
                    BindLanOverride = effectiveBindLan,
                    CustomDataDirectory = cli.CustomDataDirectory,
                    CustomConfigFilePath = cli.CustomConfigFilePath,
                    CustomLogsDirectory = cli.CustomLogsDirectory,
                    IsolatedOverride = cli.IsIsolatedInvocation,
                    DryRun = cli.DryRun,
                    ManagedByMother = cli.ManagedByMother
                };
                ApplyOptionalSuiteLaunchOptions(launchOptions, effectiveBindHost, cli.SuiteId);

                using var trayContext = new TrayDaemonApplicationContext(launchOptions);
#if DEBUG || HOUSEKEEPING_TEST_AUTOMATION
                using var automationBridge = DesktopAutomationBridge.TryStart(args, cli);
#endif
                Application.Run(trayContext);
                return 0;
            }
            catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
            {
                TryAttachParentConsole();
                Console.Error.WriteLine($"Sandbox containment violation (ExitCode={SandboxEnvironmentSeam.ContainmentExitCode}): {ex.Message}");
                Console.Error.Flush();
                return SandboxEnvironmentSeam.ContainmentExitCode;
            }
            catch (Exception ex)
            {
                try
                {
                    Housekeeping.Services.CrashBundleService.WriteCrashBundle(ex, logsDirOverride: initialLogsDir);
                    string crashDir = !string.IsNullOrWhiteSpace(cli.CustomLogsDirectory)
                        ? cli.CustomLogsDirectory
                        : (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory)
                            ? cli.CustomDataDirectory
                            : AppDomain.CurrentDomain.BaseDirectory);
                    Directory.CreateDirectory(crashDir);
                    File.AppendAllText(
                        Path.Combine(crashDir, "crash_desktophost.log"),
                        $"[{DateTime.Now:O}] Fatal startup exception: {Housekeeping.Services.CrashBundleService.SanitizeText(ex.ToString())}{Environment.NewLine}");
                }
                catch (Exception logEx)
                {
                    Debug.WriteLine($"[Program] Crash log write failed: {logEx.Message}");
                }

                bool suppressModals = string.Equals(Environment.GetEnvironmentVariable("FOUNDATION_SUPPRESS_MODALS"), "1", StringComparison.OrdinalIgnoreCase);
                if (!cli.IsWatchdog && !cli.StartMinimized && !suppressModals)
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

        private static void ApplyOptionalSuiteLaunchOptions(
            TrayDaemonLaunchOptions options,
            string? bindHost,
            string? suiteId)
        {
            Type t = typeof(TrayDaemonLaunchOptions);
            if (!string.IsNullOrWhiteSpace(bindHost))
                t.GetProperty("BindHostOverride")?.SetValue(options, bindHost);
            if (!string.IsNullOrWhiteSpace(suiteId))
                t.GetProperty("SuiteId")?.SetValue(options, suiteId);
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

        private static string ResolveMutexName(HousekeepingParsedCli cli, SandboxResolution sandbox)
        {
            if (!cli.IsIsolatedInvocation && !sandbox.IsSandboxActive)
            {
                return CanonicalSingleInstanceMutexName;
            }

            string baseRef = !string.IsNullOrWhiteSpace(cli.CustomDataDirectory)
                ? Path.GetFullPath(cli.CustomDataDirectory)
                : (!string.IsNullOrWhiteSpace(cli.CustomConfigFilePath)
                    ? Path.GetFullPath(cli.CustomConfigFilePath)
                    : (sandbox.IsSandboxActive && !string.IsNullOrWhiteSpace(sandbox.SandboxRoot)
                        ? Path.GetFullPath(sandbox.SandboxRoot)
                        : AppDomain.CurrentDomain.BaseDirectory));
            string logRef = !string.IsNullOrWhiteSpace(cli.CustomLogsDirectory)
                ? Path.GetFullPath(cli.CustomLogsDirectory)
                : "";
            string scopeKey = $"{sandbox.SandboxRoot ?? ""}|{baseRef.ToLowerInvariant()}|{logRef.ToLowerInvariant()}|{cli.PortOverride?.ToString() ?? "default"}|{cli.DryRun}";
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeKey));
            string hex12 = Convert.ToHexString(hash).Substring(0, 12);
            return $@"Global\Mother_Housekeeping_Scoped_{hex12}";
        }
    }
}
