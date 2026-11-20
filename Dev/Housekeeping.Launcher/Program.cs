// @module: Housekeeping.Launcher.Program
// @purpose: Root Housekeeping.exe shim delegating CLI/GUI forwarding directly to Foundation.Launcher.RootLauncherHost.
// @depends-on: System, System.IO, Foundation.Launcher
// @exports: Program

using System;
using System.IO;
using Foundation.Launcher;

namespace Housekeeping.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        RootLauncherHost.Run(
            Path.Combine("app", "Housekeeping.exe"),
            "Housekeeping",
            args ?? Array.Empty<string>(),
            HousekeepingLauncherArgsHelper.CreateOptions());
}
