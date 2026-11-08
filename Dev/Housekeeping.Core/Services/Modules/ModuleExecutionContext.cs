using System;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Models;

namespace Housekeeping.Services.Modules
{
    public class ModuleRunStats
    {
        private int _processed;
        private int _skipped;
        private int _errors;

        public Action<int, int, int>? OnStatsDelta { get; set; }

        public int Processed
        {
            get => _processed;
            set
            {
                int delta = value - _processed;
                _processed = value;
                if (delta > 0) OnStatsDelta?.Invoke(delta, 0, 0);
            }
        }

        public int Skipped
        {
            get => _skipped;
            set
            {
                int delta = value - _skipped;
                _skipped = value;
                if (delta > 0) OnStatsDelta?.Invoke(0, delta, 0);
            }
        }

        public int Errors
        {
            get => _errors;
            set
            {
                int delta = value - _errors;
                _errors = value;
                if (delta > 0) OnStatsDelta?.Invoke(0, 0, delta);
            }
        }
    }

    public class ModuleExecutionContext
    {
        public required AppConfig Config { get; init; }
        public required ConfigService ConfigService { get; init; }
        public required DependencyCheckService Dependencies { get; init; }
        public required NetworkSafetyService NetworkSafety { get; init; }
        public required ProcessRunnerService ProcessRunner { get; init; }
        public required bool IsDryRun { get; init; }
        public required Func<CancellationToken, Task> WaitIfQueuePausedAsync { get; init; }
        public required Action<string, string> Log { get; init; }
        public required Action<double, string> UpdateModuleProgress { get; init; }
        public HousekeepingJob? ActiveJob { get; init; }
        public ModuleRunStats Stats { get; } = new();

        public ProxyGeneratorConfig ActiveProxyConfig => ActiveJob?.ProxyGenerator ?? Config.ProxyGenerator;
        public ArchiveCompressorConfig ActiveArchiveConfig => ActiveJob?.ArchiveCompressor ?? Config.ArchiveCompressor;
        public MediaTranscoderConfig ActiveTranscoderConfig => ActiveJob?.MediaTranscoder ?? Config.MediaTranscoder;
        public RomLauncherGeneratorConfig ActiveRomConfig => ActiveJob?.RomLauncherGenerator ?? Config.RomLauncherGenerator;
        public CloudSyncConfig ActiveCloudConfig => ActiveJob?.CloudSync ?? Config.CloudSync;
        public HumbleSyncConfig ActiveHumbleConfig => ActiveJob?.HumbleSync ?? Config.HumbleSync;
        public CustomScriptsConfig ActiveCustomConfig => ActiveJob?.CustomScripts ?? Config.CustomScripts;
        public CollectionManifestConfig ActiveCollectionManifestConfig => ActiveJob?.CollectionManifest ?? Config.CollectionManifest;

        public bool ResolveUseExternalCmd(ModuleBaseConfig moduleConfig)
        {
            if (ActiveJob != null && ActiveJob.RunInExternalCmd)
                return true;
            if (moduleConfig.RunInExternalCmd)
                return true;
            return !Config.DefaultUseBuiltInTerminal;
        }
    }
}
