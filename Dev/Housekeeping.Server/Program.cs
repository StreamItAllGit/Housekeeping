using System;
using System.IO;
using Foundation.Core;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;

#if !(DEBUG || HOUSEKEEPING_TEST_AUTOMATION)
if (HousekeepingCliParser.IsAutomationRequested(args))
{
    Console.Error.WriteLine(
        "Fail-closed: --automation-port and --automation-token are disabled in Release builds. " +
        "Build with Debug or -p:HousekeepingTestAutomation=true (ExitCode=2).");
    return 2;
}
#endif

var cli = HousekeepingCliParser.Parse(args);
if (!string.IsNullOrWhiteSpace(cli.ParseError))
{
    Console.Error.WriteLine($"Error: {cli.ParseError}");
    Console.Error.WriteLine("Run with --help for usage information.");
    return 2;
}

#if !(DEBUG || HOUSEKEEPING_TEST_AUTOMATION)
if (cli.IsAutomationRequested)
{
    Console.Error.WriteLine("Fail-closed: --automation-* options are disabled in Release builds (ExitCode=2).");
    return 2;
}
#endif

if (cli.ShowVersion)
{
    Console.WriteLine(RemoteNodeDeploymentService.GetLocalAppVersion());
    return 0;
}

if (cli.ShowHelp)
{
    Console.Write(HousekeepingCliParser.BuildHelpText("Housekeeping.Server.exe"));
    return 0;
}

if (cli.RunStatus)
{
    return HousekeepingCliRunner.RunStatusCheck(cli);
}

if (cli.RunRepairConfig)
{
    return HousekeepingCliRunner.RunRepairConfig(cli);
}

if (cli.RunDoctor)
{
    return HousekeepingCliRunner.RunDoctor(cli);
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

string? effectiveBindHost = cli.ManagedByMother ? "127.0.0.1" : cli.BindHost;
bool? effectiveBindLan = cli.ManagedByMother
    ? false
    : HousekeepingCliParser.ResolveEffectiveBindLanOverride(effectiveBindHost, cli.BindLanOverride);

try
{
    SandboxResolution sandbox = SandboxEnvironmentSeam.ResolveCurrent();
    if (sandbox.IsSandboxActive)
    {
        if (!string.IsNullOrWhiteSpace(cli.CustomDataDirectory))
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomDataDirectory, sandbox);
        if (!string.IsNullOrWhiteSpace(cli.CustomConfigFilePath))
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomConfigFilePath, sandbox);
        if (!string.IsNullOrWhiteSpace(cli.CustomLogsDirectory))
            SandboxEnvironmentSeam.AssertSandboxPathIfActive(cli.CustomLogsDirectory, sandbox);
    }

    Housekeeping.Services.CrashBundleService.RegisterGlobalHandlers(
        () => initialLogsDir,
        RemoteNodeDeploymentService.GetLocalAppVersion(),
        cli.IsIsolatedInvocation || sandbox.IsSandboxActive,
        archetype: "2-tray-companion");

    var options = new HousekeepingServerOptions
    {
        PortOverride = cli.PortOverride,
        BindLanOverride = effectiveBindLan,
        CustomDataDirectory = cli.CustomDataDirectory,
        CustomConfigFilePath = cli.CustomConfigFilePath,
        CustomLogsDirectory = cli.CustomLogsDirectory,
        IsolatedOverride = cli.IsIsolatedInvocation,
        DryRun = cli.DryRun,
        ManagedByMother = cli.ManagedByMother,
        BindHostOverride = string.IsNullOrWhiteSpace(effectiveBindHost) ? null : effectiveBindHost,
        SuiteId = string.IsNullOrWhiteSpace(cli.SuiteId) ? null : cli.SuiteId
    };

    using var app = HousekeepingServerHost.BuildWebApplication(cli.RemainingArgs, options);
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (SandboxEnvironmentSeam.IsContainmentViolation(ex))
{
    Console.Error.WriteLine($"Sandbox containment violation (ExitCode={SandboxEnvironmentSeam.ContainmentExitCode}): {ex.Message}");
    return SandboxEnvironmentSeam.ContainmentExitCode;
}
catch (Exception ex)
{
    Housekeeping.Services.CrashBundleService.WriteCrashBundle(ex, logsDirOverride: initialLogsDir);
    throw;
}
