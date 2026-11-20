// @module: Housekeeping.Launcher.ArgsHelper
// @purpose: Provides RootLauncherOptions and CLI argument helpers for Housekeeping (--status, --repair-config, --doctor, --bind, --suite-id, --automation-*).
// @depends-on: System, Foundation.Launcher
// @exports: HousekeepingLauncherArgsHelper

using System;
using Foundation.Launcher;

namespace Housekeeping.Launcher;

/// <summary>
/// Wraps <see cref="RootLauncherHost"/> CLI helpers for Housekeeping-specific sync-wait flags and value options.
/// </summary>
public static class HousekeepingLauncherArgsHelper
{
    public static RootLauncherOptions CreateOptions() => new()
    {
        ExtraSyncWaitFlags = ["--status", "--repair-config", "--doctor", "--managed-by-suite", "--managed-by-mother", "--automation-port", "--automation-token"],
        ExtraValueOptions = ["--bind", "--suite-id", "--automation-port", "--automation-token"]
    };

    public static string[] NormalizeArgs(string[]? args, string callerCwd)
    {
        string effectiveCwd = string.IsNullOrWhiteSpace(callerCwd)
            ? Environment.CurrentDirectory
            : callerCwd;
        return RootLauncherHost.NormalizeRelativePathArgs(args, effectiveCwd, CreateOptions());
    }

    public static bool ShouldWaitForChild(string[]? args) =>
        RootLauncherHost.ShouldWaitForChild(args, CreateOptions());
}
