// @module: Housekeeping.Installer.Integration
// @purpose: Handles automation CLI validation (ExitCode=2 in Release), IPlatformRegistry suite metadata, non-destructive legacy migration copy, and sandbox-safe Housekeeping_Watchdog cleanup.
// @depends-on: System, System.Collections.Generic, System.Diagnostics, System.Globalization, System.IO, System.Text, System.Text.Json, System.Threading, System.Threading.Tasks, Foundation.Core, Foundation.Installer
// @exports: HousekeepingInstallerIntegration

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Core;
using Foundation.Installer;

namespace Housekeeping.Installer;

internal static class HousekeepingInstallerIntegration
{
    internal const string HousekeepingRegistrySubKey = @"Software\StreamItAll\Housekeeping";
    internal const string LegacyRunRegistrySubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    internal const string LegacyRunValueName = "Housekeeping2";
    internal const string LegacyWatchdogTaskName = "Housekeeping_Watchdog";

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal static (bool WriteShortcuts, bool WriteAutoStart, bool WriteRegistry) PlanLivePostInstallActions(
        bool suppressHostMutations,
        bool dryRun,
        bool isWindows,
        bool createShortcuts,
        bool autoStart)
    {
        if (suppressHostMutations || dryRun || !isWindows)
            return (false, false, false);

        return (createShortcuts, autoStart, true);
    }

    internal static bool TryHandleAutomationArgs(
        string[] args,
        out string[] hostArgs,
        out string[] automationArgs,
        out string? errorMessage)
    {
        var host = new List<string>(args.Length);
        var auto = new List<string>();
        bool sawAutomationFlag = false;
        errorMessage = null;

        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i] ?? string.Empty;
            string trimmed = raw.Trim();

            if (TryMatchKeyValueFlag(args, ref i, trimmed, "--automation-port", out string? portVal, out bool missingPortVal))
            {
                sawAutomationFlag = true;
                if (missingPortVal || !int.TryParse(portVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int portNum) || portNum < 0 || portNum > 65535)
                {
                    errorMessage = "Option '--automation-port' requires an integer port between 0 and 65535.";
                    hostArgs = Array.Empty<string>();
                    automationArgs = Array.Empty<string>();
                    return false;
                }
                auto.Add("--automation-port");
                auto.Add(portNum.ToString(CultureInfo.InvariantCulture));
                continue;
            }

            if (TryMatchKeyValueFlag(args, ref i, trimmed, "--automation-token", out string? tokenVal, out bool missingTokenVal))
            {
                sawAutomationFlag = true;
                if (missingTokenVal || string.IsNullOrWhiteSpace(tokenVal))
                {
                    errorMessage = "Option '--automation-token' requires a non-empty bearer token.";
                    hostArgs = Array.Empty<string>();
                    automationArgs = Array.Empty<string>();
                    return false;
                }
                auto.Add("--automation-token");
                auto.Add(tokenVal.Trim());
                continue;
            }

            host.Add(raw);
        }

#if !(DEBUG || HOUSEKEEPING_TEST_AUTOMATION)
        if (sawAutomationFlag)
        {
            errorMessage = "Automation flags (--automation-port / --automation-token) are disabled in Release builds.";
            hostArgs = Array.Empty<string>();
            automationArgs = Array.Empty<string>();
            return false;
        }
#else
        _ = sawAutomationFlag;
#endif

        hostArgs = host.ToArray();
        automationArgs = auto.ToArray();
        return true;
    }

    internal static string ResolveHousekeepingDefaultInstallDirCore(
        SandboxResolution sandbox,
        IPlatformRegistry registry,
        string? dataDir,
        bool isStage1Isolated,
        InstallerEngine? engine)
    {
        if (isStage1Isolated && !string.IsNullOrWhiteSpace(dataDir))
            return Path.Combine(dataDir, "installed-app");

        string? hkHome = Environment.GetEnvironmentVariable("HOUSEKEEPING_HOME");
        if (!string.IsNullOrWhiteSpace(hkHome))
        {
            string fullHome = Path.GetFullPath(hkHome.Trim());
            return sandbox.IsSandboxActive
                ? SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullHome, sandbox)
                : fullHome;
        }

        if (OperatingSystem.IsWindows() || sandbox.IsSandboxActive)
        {
            string? regDir = registry
                .GetValue(PlatformRegistryHive.CurrentUser, HousekeepingRegistrySubKey, "InstallDir")
                ?.AsString();
            if (!string.IsNullOrWhiteSpace(regDir))
            {
                string candidate = Path.GetFullPath(regDir.Trim());
                if ((!sandbox.IsSandboxActive || SandboxEnvironmentSeam.IsPathWithinSandbox(candidate, sandbox)) &&
                    Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (engine is not null)
            return engine.ResolveDefaultInstallDirectory();

        string localAppData = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, sandbox);
        return Path.Combine(localAppData, "Programs", "StreamItAll", "Mother", "Housekeeping");
    }

    internal static void RegisterSuiteMetadata(
        InstallerRuntimeContext ctx,
        string installDir,
        string? suiteDir,
        string? profileId,
        string version)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (string.IsNullOrWhiteSpace(installDir)) return;
        if (ctx.DryRun || ctx.ShouldSuppressHostMutations || ctx.Registry is NoOpPlatformRegistry) return;
        if (!OperatingSystem.IsWindows() && !ctx.IsSandboxActive) return;

        string fullInstallDir = Path.GetFullPath(installDir.Trim());
        string effectiveSuiteDir = !string.IsNullOrWhiteSpace(suiteDir)
            ? Path.GetFullPath(suiteDir.Trim())
            : (Path.GetDirectoryName(fullInstallDir) ?? fullInstallDir);

        if (ctx.IsSandboxActive)
        {
            fullInstallDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(fullInstallDir, ctx.Sandbox);
            effectiveSuiteDir = SandboxEnvironmentSeam.AssertSandboxPathIfActive(effectiveSuiteDir, ctx.Sandbox);
        }

        ctx.Registry.SetValues(
            PlatformRegistryHive.CurrentUser,
            HousekeepingRegistrySubKey,
            new Dictionary<string, PlatformRegistryValue>
            {
                ["InstallDir"] = new(PlatformRegistryValueKind.String, fullInstallDir),
                ["SuiteInstallDir"] = new(PlatformRegistryValueKind.String, effectiveSuiteDir),
                ["InstallProfile"] = new(PlatformRegistryValueKind.String, string.IsNullOrWhiteSpace(profileId) ? "full" : profileId.Trim()),
                ["Version"] = new(PlatformRegistryValueKind.String, string.IsNullOrWhiteSpace(version) ? "0.2.0" : version.Trim())
            });
    }

    internal static void RemoveSuiteMetadata(InstallerRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.DryRun || ctx.ShouldSuppressHostMutations || ctx.Registry is NoOpPlatformRegistry) return;
        if (!OperatingSystem.IsWindows() && !ctx.IsSandboxActive) return;

        try
        {
            ctx.Registry.DeleteSubKeyTree(PlatformRegistryHive.CurrentUser, HousekeepingRegistrySubKey, throwOnMissingSubKey: false);
            ctx.Registry.DeleteValue(PlatformRegistryHive.CurrentUser, LegacyRunRegistrySubKey, LegacyRunValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            ctx.WriteLog("WARN", $"Housekeeping registry metadata cleanup warning: {ex.Message}");
        }
    }

    internal static InstallManifest? TryReadInstallManifest(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;

        foreach (string path in new[]
        {
            Path.Combine(installDir, "app", InstallerEngine.InstallManifestFileName),
            Path.Combine(installDir, InstallerEngine.InstallManifestFileName)
        })
        {
            if (!File.Exists(path)) continue;
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<InstallManifest>(raw, ManifestJsonOpts);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"Housekeeping install manifest read warning ({path}): {ex.Message}");
            }
        }
        return null;
    }

    internal static async Task TryMigrateLegacyInstallAsync(
        InstallerRuntimeContext ctx,
        InstallerAppDescriptor descriptor,
        string installedDir,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (ctx.DryRun || string.IsNullOrWhiteSpace(installedDir)) return;

        string targetRoot = Path.GetFullPath(installedDir.Trim());
        if (ctx.IsSandboxActive)
        {
            targetRoot = SandboxEnvironmentSeam.AssertSandboxPathIfActive(targetRoot, ctx.Sandbox);
        }
        else if (ctx.ShouldSuppressLiveHostMutations &&
                 FileLockRetryHelper.IsCanonicalProductionOrSystemPath(targetRoot, SandboxResolution.Inactive))
        {
            SandboxEnvironmentSeam.ThrowContainmentViolation(
                $"Fail-closed isolation violation: refusing legacy migration to canonical production or system directory '{targetRoot}'.");
        }

        string? legacySource = DetectLegacyHousekeepingInstall(ctx, targetRoot);
        if (!string.IsNullOrWhiteSpace(legacySource))
        {
            if (ctx.IsSandboxActive)
                legacySource = SandboxEnvironmentSeam.AssertSandboxPathIfActive(legacySource, ctx.Sandbox);

            int copied = await LegacyInstallCleanup.CopyLegacyStateNonDestructivelyAsync(legacySource, targetRoot, ct).ConfigureAwait(false);
            ctx.WriteLog("INFO", $"Non-destructive legacy copy completed from '{legacySource}' -> '{targetRoot}' ({copied} file(s) copied).");
        }

        if (!ctx.ShouldSuppressHostMutations && ctx.Registry is not NoOpPlatformRegistry)
        {
            try
            {
                ctx.Registry.DeleteValue(PlatformRegistryHive.CurrentUser, LegacyRunRegistrySubKey, LegacyRunValueName, throwOnMissingValue: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                ctx.WriteLog("WARN", $"Legacy Run key cleanup warning: {ex.Message}");
            }
        }

        TryUnregisterLegacyWatchdog(ctx);
    }

    internal static void TryUnregisterLegacyWatchdog(InstallerRuntimeContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.DryRun || ctx.ShouldSuppressLiveHostMutations || !OperatingSystem.IsWindows())
        {
            ctx.WriteLog("INFO", $"[ISOLATED/SANDBOX/DRY-RUN] Skipped live scheduled task removal ({LegacyWatchdogTaskName}).");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("/Delete");
            psi.ArgumentList.Add("/TN");
            psi.ArgumentList.Add(LegacyWatchdogTaskName);
            psi.ArgumentList.Add("/F");

            using Process? proc = Process.Start(psi);
            if (proc is null) return;
            Task<string> outTask = proc.StandardOutput.ReadToEndAsync();
            Task<string> errTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(4000))
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception killEx) { Trace.WriteLine($"schtasks kill ignored: {killEx.Message}"); }
                ctx.WriteLog("WARN", $"Timed out waiting to unregister legacy task {LegacyWatchdogTaskName}.");
                return;
            }
            Task.WaitAll(new Task[] { outTask, errTask }, 1000);
            ctx.WriteLog(proc.ExitCode == 0 ? "INFO" : "DEBUG", $"Legacy watchdog cleanup ({LegacyWatchdogTaskName}) exit={proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            ctx.WriteLog("WARN", $"Legacy watchdog cleanup warning ({LegacyWatchdogTaskName}): {ex.Message}");
        }
    }

    private static string? DetectLegacyHousekeepingInstall(InstallerRuntimeContext ctx, string targetRoot)
    {
        var candidates = new List<string>();
        if (ctx.IsStage1Isolated)
        {
            if (string.IsNullOrWhiteSpace(ctx.DataDirectory)) return null;
            candidates.Add(Path.Combine(ctx.DataDirectory, "legacy-programs", "Housekeeping"));
            candidates.Add(Path.Combine(ctx.DataDirectory, "legacy-programs", "housekeeping"));
        }
        else if (ctx.IsSandboxActive)
        {
            string storageRoot = !string.IsNullOrWhiteSpace(ctx.Sandbox.StorageDriveRoot)
                ? ctx.Sandbox.StorageDriveRoot
                : Path.Combine(ctx.Sandbox.SandboxRoot!, "D");
            string localApp = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, ctx.Sandbox);
            candidates.Add(Path.Combine(storageRoot, "Programs", "Housekeeping"));
            candidates.Add(Path.Combine(localApp, "Programs", "Housekeeping"));
        }
        else
        {
            string localApp = KnownFolderPaths.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine("D:" + Path.DirectorySeparatorChar, "Programs", "Housekeeping"));
            candidates.Add(Path.Combine(localApp, "Programs", "Housekeeping"));
        }

        string normTarget = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string rawCandidate in candidates)
        {
            string fullCandidate = Path.GetFullPath(rawCandidate);
            if (ctx.IsSandboxActive && !SandboxEnvironmentSeam.IsPathWithinSandbox(fullCandidate, ctx.Sandbox))
                continue;
            if (string.Equals(fullCandidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normTarget, StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.Exists(fullCandidate) && HasLegacyArtifacts(fullCandidate))
                return fullCandidate;
        }
        return null;
    }

    private static bool HasLegacyArtifacts(string dir) =>
        File.Exists(Path.Combine(dir, "Housekeeping.exe")) ||
        File.Exists(Path.Combine(dir, "settings.json")) ||
        File.Exists(Path.Combine(dir, "data", "settings.json")) ||
        File.Exists(Path.Combine(dir, "app", "Housekeeping.exe"));

    private static bool TryMatchKeyValueFlag(
        string[] args,
        ref int index,
        string trimmed,
        string flagName,
        out string? value,
        out bool missingValue)
    {
        value = null;
        missingValue = false;

        if (trimmed.Equals(flagName, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                missingValue = true;
                return true;
            }
            index++;
            value = args[index].Trim();
            return true;
        }

        if (trimmed.StartsWith(flagName + "=", StringComparison.OrdinalIgnoreCase))
        {
            string rawVal = trimmed[(flagName.Length + 1)..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(rawVal))
            {
                missingValue = true;
                return true;
            }
            value = rawVal;
            return true;
        }

        return false;
    }
}
