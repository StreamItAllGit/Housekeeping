using System;
using System.IO;
using Housekeeping.Server.Hosting;
using Housekeeping.Services;

var cli = HousekeepingCliParser.Parse(args);
if (!string.IsNullOrWhiteSpace(cli.ParseError))
{
    Console.Error.WriteLine($"Error: {cli.ParseError}");
    Console.Error.WriteLine("Run with --help for usage information.");
    return 2;
}

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
    cli.IsIsolatedInvocation,
    archetype: "2-tray-companion");

var options = new HousekeepingServerOptions
{
    PortOverride = cli.PortOverride,
    BindLanOverride = cli.BindLanOverride,
    CustomDataDirectory = cli.CustomDataDirectory,
    CustomConfigFilePath = cli.CustomConfigFilePath,
    CustomLogsDirectory = cli.CustomLogsDirectory,
    IsolatedOverride = cli.IsIsolatedInvocation,
    DryRun = cli.DryRun
};

try
{
    using var app = HousekeepingServerHost.BuildWebApplication(cli.RemainingArgs, options);
    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    CrashBundleService.WriteCrashBundle(ex, logsDirOverride: initialLogsDir);
    throw;
}
