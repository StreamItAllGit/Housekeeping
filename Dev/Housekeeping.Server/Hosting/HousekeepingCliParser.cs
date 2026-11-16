using System;
using System.Collections.Generic;
using System.Text;
using Housekeeping.Services;

namespace Housekeeping.Server.Hosting
{
    public sealed class HousekeepingParsedCli
    {
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public bool RunStatus { get; set; }
        public bool RunRepairConfig { get; set; }
        public bool StartMinimized { get; set; }
        public bool IsWatchdog { get; set; }
        public bool ManagedByMother { get; set; }
        public bool DryRun { get; set; }
        public bool? BindLanOverride { get; set; }
        public int? PortOverride { get; set; }
        public string? CustomDataDirectory { get; set; }
        public string? CustomConfigFilePath { get; set; }
        public string? CustomLogsDirectory { get; set; }
        public string[] RemainingArgs { get; set; } = Array.Empty<string>();
        public string? ParseError { get; set; }

        public bool IsIsolatedInvocation =>
            DryRun ||
            PortOverride.HasValue ||
            !string.IsNullOrWhiteSpace(CustomDataDirectory) ||
            !string.IsNullOrWhiteSpace(CustomConfigFilePath) ||
            !string.IsNullOrWhiteSpace(CustomLogsDirectory) ||
            string.Equals(Environment.GetEnvironmentVariable("APP_ISOLATED_TEST"), "1", StringComparison.OrdinalIgnoreCase);
    }

    public static class HousekeepingCliParser
    {
        public static HousekeepingParsedCli Parse(string[]? args)
        {
            var result = new HousekeepingParsedCli();
            var remaining = new List<string>();
            args ??= Array.Empty<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-?", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "/?", StringComparison.OrdinalIgnoreCase))
                {
                    result.ShowHelp = true;
                }
                else if (string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase))
                {
                    result.ShowVersion = true;
                }
                else if (string.Equals(a, "--status", StringComparison.OrdinalIgnoreCase))
                {
                    result.RunStatus = true;
                }
                else if (string.Equals(a, "--repair-config", StringComparison.OrdinalIgnoreCase))
                {
                    result.RunRepairConfig = true;
                }
                else if (string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--tray-only", StringComparison.OrdinalIgnoreCase))
                {
                    result.StartMinimized = true;
                }
                else if (string.Equals(a, "--watchdog", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsWatchdog = true;
                    result.StartMinimized = true;
                }
                else if (string.Equals(a, "--managed-by-mother", StringComparison.OrdinalIgnoreCase))
                {
                    result.ManagedByMother = true;
                    result.StartMinimized = true;
                }
                else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    result.DryRun = true;
                }
                else if (string.Equals(a, "--localhost-only", StringComparison.OrdinalIgnoreCase))
                {
                    result.BindLanOverride = false;
                }
                else if (TryReadOptionValue(args, ref i, "--port", out string? portVal, out string? portErr))
                {
                    if (portErr != null)
                    {
                        result.ParseError = portErr;
                        return result;
                    }
                    if (!int.TryParse(portVal, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
                    {
                        result.ParseError = $"Invalid --port value '{portVal}'. Port must be an integer between 1 and 65535.";
                        return result;
                    }
                    result.PortOverride = parsedPort;
                }
                else if (TryReadOptionValue(args, ref i, "--data-dir", out string? dataVal, out string? dataErr))
                {
                    if (dataErr != null)
                    {
                        result.ParseError = dataErr;
                        return result;
                    }
                    result.CustomDataDirectory = dataVal;
                }
                else if (TryReadOptionValue(args, ref i, "--config", out string? cfgVal, out string? cfgErr))
                {
                    if (cfgErr != null)
                    {
                        result.ParseError = cfgErr;
                        return result;
                    }
                    result.CustomConfigFilePath = cfgVal;
                }
                else if (TryReadOptionValue(args, ref i, "--log-dir", out string? logVal, out string? logErr))
                {
                    if (logErr != null)
                    {
                        result.ParseError = logErr;
                        return result;
                    }
                    result.CustomLogsDirectory = logVal;
                }
                else
                {
                    remaining.Add(a);
                }
            }

            if (string.IsNullOrWhiteSpace(result.CustomDataDirectory))
            {
                result.CustomDataDirectory = FirstNonEmptyEnv("HOUSEKEEPING_DATA_DIR", "DATA_DIR", "HOUSEKEEPING_HOME", "APP_HOME");
            }

            if (string.IsNullOrWhiteSpace(result.CustomConfigFilePath))
            {
                result.CustomConfigFilePath = FirstNonEmptyEnv("HOUSEKEEPING_CONFIG", "CONFIG_PATH");
            }

            if (string.IsNullOrWhiteSpace(result.CustomLogsDirectory))
            {
                result.CustomLogsDirectory = FirstNonEmptyEnv("HOUSEKEEPING_LOG_DIR", "LOG_DIR");
            }

            if (!result.PortOverride.HasValue)
            {
                string? envPortText = FirstNonEmptyEnv("HOUSEKEEPING_PORT", "PORT");
                if (int.TryParse(envPortText, out int envPort) && envPort >= 1 && envPort <= 65535)
                {
                    result.PortOverride = envPort;
                }
            }

            if (!result.DryRun && IsTruthyEnv("HOUSEKEEPING_DRY_RUN", "DRY_RUN"))
            {
                result.DryRun = true;
            }

            if (!result.ManagedByMother && IsTruthyEnv("HOUSEKEEPING_MANAGED_BY_MOTHER", "MOTHER_MANAGED"))
            {
                result.ManagedByMother = true;
            }

            result.RemainingArgs = remaining.ToArray();
            return result;
        }

        public static string BuildHelpText(string binaryName = "Housekeeping.exe")
        {
            string version = RemoteNodeDeploymentService.GetLocalAppVersion();
            var sb = new StringBuilder();
            sb.AppendLine($"Housekeeping v{version} — Media, Archive, Cloud, Manifest & ROM Automation Engine");
            sb.AppendLine();
            sb.AppendLine($"Usage: {binaryName} [options]");
            sb.AppendLine();
            sb.AppendLine("Containment & Path Options:");
            sb.AppendLine("  --data-dir <dir>       Redirect runtime data, history, snapshots, and caches to <dir>");
            sb.AppendLine("                         (Env: HOUSEKEEPING_DATA_DIR, DATA_DIR, HOUSEKEEPING_HOME, APP_HOME)");
            sb.AppendLine("  --config <file>        Load and persist configuration at <file> instead of <data-dir>\\settings.json");
            sb.AppendLine("                         (Env: HOUSEKEEPING_CONFIG, CONFIG_PATH)");
            sb.AppendLine("  --log-dir <dir>        Write live logs and telemetry_heartbeat.json exclusively to <dir>");
            sb.AppendLine("                         (Env: HOUSEKEEPING_LOG_DIR, LOG_DIR)");
            sb.AppendLine();
            sb.AppendLine("Hosting & Execution Options:");
            sb.AppendLine("  --port <1-65535>       Override Kestrel HTTP listening port (Env: HOUSEKEEPING_PORT, PORT)");
            sb.AppendLine("  --localhost-only       Bind strictly to 127.0.0.1 loopback (disable LAN/Tailnet listener)");
            sb.AppendLine("  --status               Print one-shot JSON daemon & configuration status and exit");
            sb.AppendLine("  --repair-config        Validate, migrate, or quarantine/recover settings.json and exit");
            sb.AppendLine("  --dry-run              Force all pipeline and job dispatches into non-destructive dry-run mode");
            sb.AppendLine("                         (Env: HOUSEKEEPING_DRY_RUN=1)");
            sb.AppendLine("  --tray-only, --minimized, --silent");
            sb.AppendLine("                         Start in System Tray without opening a browser/WebView2 window on launch");
            sb.AppendLine("  --watchdog             Internal Task Scheduler watchdog invocation (implies --tray-only)");
            sb.AppendLine("  --managed-by-mother    Managed child daemon mode under Mother v1.0 (suppresses standalone system tray icon)");
            sb.AppendLine("  --version, -v          Print current application version and exit");
            sb.AppendLine("  --help, -h             Show this command-line reference and exit");
            return sb.ToString();
        }

        private static bool TryReadOptionValue(string[] args, ref int index, string flag, out string? value, out string? error)
        {
            value = null;
            error = null;
            string current = args[index];

            if (string.Equals(current, flag, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    error = $"Missing value for option '{flag}'.";
                    return true;
                }
                value = args[++index];
                return true;
            }

            string prefix = flag + "=";
            if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = current[prefix.Length..].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    error = $"Missing value for option '{flag}'.";
                }
                return true;
            }

            return false;
        }

        private static string? FirstNonEmptyEnv(params string[] names)
        {
            foreach (string name in names)
            {
                string? val = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val.Trim();
                }
            }
            return null;
        }

        private static bool IsTruthyEnv(params string[] names)
        {
            foreach (string name in names)
            {
                string? val = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(val) &&
                    (val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     val.Equals("yes", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
