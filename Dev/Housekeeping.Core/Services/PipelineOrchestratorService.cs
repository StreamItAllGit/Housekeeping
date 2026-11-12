using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Housekeeping.Interop;
using Housekeeping.Models;
using Housekeeping.Services.Modules;

namespace Housekeeping.Services
{
    public sealed class PendingPipelineRunDto
    {
        public string ItemKey { get; set; } = "";
        public string RunKind { get; set; } = "Queue"; // "Queue" or "SingleJob"
        public string TargetId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsDryRun { get; set; }
        public bool ForceCloudResync { get; set; }
        public string TriggerSource { get; set; } = "Schedule";
        public DateTime EnqueuedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class InterruptedRunCheckpoint
    {
        public string RunKind { get; set; } = "Queue"; // "Queue", "SingleJob", "HashVerifier", "ComplianceScan"
        public string? QueueId { get; set; }
        public string? QueueName { get; set; }
        public List<string> InitialJobIds { get; set; } = new();
        public List<string> CompletedJobIds { get; set; } = new();
        public string? ActiveJobId { get; set; }
        public string? ActiveJobName { get; set; }
        public List<string> PendingChainedQueueIds { get; set; } = new();
        public List<PendingPipelineRunDto> PendingRuns { get; set; } = new();
        public bool IsDryRun { get; set; }
        public bool ForceCloudResync { get; set; }
        public ModuleId? VerifierSourceModule { get; set; }
        public SidecarVerifyMode? VerifierMode { get; set; }
        public double LastProgressPercent { get; set; }
        public string LastStatusText { get; set; } = "";
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class JobLiveStatusDto
    {
        public string JobId { get; set; } = "";
        public string JobName { get; set; } = "";
        public string State { get; set; } = "Idle";
        public double ProgressPercent { get; set; }
        public string DetailText { get; set; } = "";
        public int Processed { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    public sealed class PipelineRuntimeStateDto
    {
        public bool IsRunning { get; set; }
        public bool IsPaused { get; set; }
        public bool IsProcessSuspended { get; set; }
        public bool IsDryRun { get; set; }
        public string? ActiveQueueId { get; set; }
        public string? ActiveQueueName { get; set; }
        public string? ActiveJobId { get; set; }
        public string? ActiveJobName { get; set; }
        public string? ActiveModuleBadge { get; set; }
        public int CurrentJobIndex { get; set; }
        public int TotalJobs { get; set; }
        public double ProgressPercent { get; set; }
        public string SubprocessStatusText { get; set; } = "Ready — Pipeline idle";
        public string FooterStatusText { get; set; } = "Ready — Housekeeping Engine Online";
        public bool IsWaitingForNetwork { get; set; }
        public string NetworkWaitMessage { get; set; } = "";
        public int SessionProcessed { get; set; }
        public int SessionSkipped { get; set; }
        public int SessionErrors { get; set; }
        public int PendingSwapCount { get; set; }
        public string? PendingSwapSampleFile { get; set; }
        public string GlobalNextRunStatusText { get; set; } = "Schedule: Disabled";
        public DateTime? GlobalNextRunUtc { get; set; }
        public string? GlobalNextRunQueueName { get; set; }
        public string PostRunPowerAction { get; set; } = "StayAwake";
        public InterruptedRunCheckpoint? InterruptedRun { get; set; }
        public List<PendingPipelineRunDto> PendingRuns { get; set; } = new();
        public Dictionary<string, JobLiveStatusDto> JobStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed partial class PipelineOrchestratorService : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly TerminalLogBufferService _logBuffer;
        private readonly DependencyCheckService _dependencyService;
        private readonly PendingSwapService _pendingSwapService;
        private readonly NetworkSafetyService _networkSafety;
        private readonly ProcessRunnerService _processRunner;
        private readonly SchedulerAndNotificationService _scheduler;
        private readonly SystemPerformanceTelemetryService _telemetryService;
        private readonly ComplianceReportService _complianceReports;
        private readonly GreenroomSyncIntegrationService _greenroomSync;

        private readonly object _stateLock = new();
        private readonly List<PendingPipelineRunDto> _pendingRuns = new();
        private readonly Dictionary<string, JobLiveStatusDto> _jobStates = new(StringComparer.OrdinalIgnoreCase);

        private bool _isTransitioningPendingRun;
        private string? _transitioningItemKey;
        private string? _lastAutoCompletedItemKey;
        private DateTime _lastAutoCompletedAtUtc = DateTime.MinValue;

        private Timer? _scheduleTimer;
        private CancellationTokenSource? _activeRunCts;
        private Task? _activeExecutionTask;

        private bool _isRunning;
        private bool _isPaused;
        private bool _isDryRun;
        private string? _activeQueueId;
        private string? _activeQueueName;
        private string? _activeJobId;
        private string? _activeJobName;
        private string? _activeModuleBadge;
        private int _currentJobIndex;
        private int _totalJobs;
        private double _progressPercent;
        private string _subprocessStatusText = "Ready — Pipeline idle";
        private string _footerStatusText = "Ready — Housekeeping Engine Online";
        private bool _isWaitingForNetwork;
        private string _networkWaitMessage = "";
        private int _sessionProcessed;
        private int _sessionSkipped;
        private int _sessionErrors;
        private int _isSweepingPendingSwaps;
        private DateTime _lastGuardDeferLogUtc = DateTime.MinValue;
        private DateTime _lastProgressBroadcastUtc = DateTime.MinValue;
        private DateTime _lastStateBroadcastUtc = DateTime.MinValue;
        private DateTime _lastCheckpointProgressFlushUtc = DateTime.MinValue;
        private InterruptedRunCheckpoint? _activeCheckpoint;
        private InterruptedRunCheckpoint? _recoverableInterruptedRun;
        private bool _userRequestedExplicitStop;
        private bool _disposed;

        public string InterruptedCheckpointFilePath => Path.Combine(_configService.DataDirectory, "interrupted_run_checkpoint.json");

        /// <summary>
        /// When true (e.g., launched with --dry-run or HOUSEKEEPING_DRY_RUN=1), forces every pipeline dispatch into non-destructive dry-run mode.
        /// </summary>
        public bool ForceDryRun { get; set; }

        /// <summary>
        /// Optional test hook allowing automated tests to intercept module execution deterministically.
        /// </summary>
        public Func<ModuleExecutionContext, CancellationToken, Task>? CustomModuleExecutor { get; set; }

        public event Action<PipelineRuntimeStateDto>? OnStateChanged;
        public event Action<ProcessProgressInfo>? OnProgressChanged;
        public event Action<string, string, bool>? OnTrayNotificationRequested;

        public Task? ActiveExecutionTask
        {
            get { lock (_stateLock) return _activeExecutionTask; }
        }

        public bool IsRunning
        {
            get { lock (_stateLock) return _isRunning; }
        }

        public PipelineOrchestratorService(
            ConfigService configService,
            TerminalLogBufferService logBuffer,
            DependencyCheckService dependencyService,
            PendingSwapService pendingSwapService,
            ComplianceReportService complianceReports,
            SystemPerformanceTelemetryService telemetryService,
            GreenroomSyncIntegrationService greenroomSync)
        {
            _configService = configService;
            _logBuffer = logBuffer;
            _dependencyService = dependencyService;
            _pendingSwapService = pendingSwapService;
            _complianceReports = complianceReports;
            _telemetryService = telemetryService;
            _greenroomSync = greenroomSync;

            _networkSafety = new NetworkSafetyService(() => _configService.Config, Log, _pendingSwapService);
            _networkSafety.NetworkWaitStateChanged += HandleNetworkWaitStateChanged;

            _processRunner = new ProcessRunnerService(Log, HandleSubprocessProgress);
            _scheduler = new SchedulerAndNotificationService(
                () => _configService.Config,
                Log,
                (title, msg, isWarn) => OnTrayNotificationRequested?.Invoke(title, msg, isWarn));

            string? dryEnv = Environment.GetEnvironmentVariable("HOUSEKEEPING_DRY_RUN")
                             ?? Environment.GetEnvironmentVariable("DRY_RUN");
            if (!string.IsNullOrWhiteSpace(dryEnv) &&
                (dryEnv.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                 dryEnv.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                 dryEnv.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                ForceDryRun = true;
            }

            LoadInterruptedCheckpointOnStartup();
        }

        public NetworkSafetyService NetworkSafety => _networkSafety;
        public ProcessRunnerService ProcessRunner => _processRunner;
        public SchedulerAndNotificationService Scheduler => _scheduler;

        public void StartBackgroundScheduler()
        {
            if (_scheduleTimer != null) return;
            _scheduleTimer = new Timer(_ =>
            {
                try
                {
                    EvaluateQueueSchedules();
                }
                catch (Exception schedEx)
                {
                    Log("WARN", $"[Scheduler] Schedule evaluation warning: {schedEx.Message}");
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));

            InterruptedRunCheckpoint? pendingRecovery;
            lock (_stateLock)
            {
                pendingRecovery = _recoverableInterruptedRun;
            }

            if (pendingRecovery != null && _configService.Config.AutoRestartInterruptedRunsOnStartup)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    if (_disposed) return;
                    Log("HEADER", $"[Auto-Recovery] Auto-restarting mid-shutdown interrupted run: '{pendingRecovery.QueueName ?? pendingRecovery.ActiveJobName ?? pendingRecovery.RunKind}'...");
                    ResumeInterruptedRun(restartFullQueue: false);
                });
            }
        }

        public void Log(string tag, string message)
        {
            _logBuffer.Append(tag, message, _configService.Config);
            bool shouldBroadcast = false;
            if (TerminalLogBufferService.ShouldUpdateFooter(tag, _configService.Config))
            {
                lock (_stateLock)
                {
                    _footerStatusText = message;
                    shouldBroadcast = _isRunning;
                }
            }
            if (shouldBroadcast)
            {
                ThrottledStateBroadcast();
            }
        }

        public PipelineRuntimeStateDto GetStateSnapshot()
        {
            var cfg = _configService.Config;
            var (nextUtc, nextStatus, nextQName) = _scheduler.GetGlobalNextScheduleStatus(cfg);
            var pendingAll = _pendingSwapService.GetAll();

            lock (_stateLock)
            {
                var copyStates = new Dictionary<string, JobLiveStatusDto>(_jobStates.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _jobStates)
                {
                    copyStates[kvp.Key] = new JobLiveStatusDto
                    {
                        JobId = kvp.Value.JobId,
                        JobName = kvp.Value.JobName,
                        State = kvp.Value.State,
                        ProgressPercent = kvp.Value.ProgressPercent,
                        DetailText = kvp.Value.DetailText,
                        Processed = kvp.Value.Processed,
                        Skipped = kvp.Value.Skipped,
                        Errors = kvp.Value.Errors
                    };
                }

                return new PipelineRuntimeStateDto
                {
                    IsRunning = _isRunning,
                    IsPaused = _isPaused,
                    IsProcessSuspended = _processRunner.IsSuspended,
                    IsDryRun = _isDryRun,
                    ActiveQueueId = _activeQueueId,
                    ActiveQueueName = _activeQueueName,
                    ActiveJobId = _activeJobId,
                    ActiveJobName = _activeJobName,
                    ActiveModuleBadge = _activeModuleBadge,
                    CurrentJobIndex = _currentJobIndex,
                    TotalJobs = _totalJobs,
                    ProgressPercent = _progressPercent,
                    SubprocessStatusText = _subprocessStatusText,
                    FooterStatusText = _footerStatusText,
                    IsWaitingForNetwork = _isWaitingForNetwork,
                    NetworkWaitMessage = _networkWaitMessage,
                    SessionProcessed = _sessionProcessed,
                    SessionSkipped = _sessionSkipped,
                    SessionErrors = _sessionErrors,
                    PendingSwapCount = pendingAll.Count,
                    PendingSwapSampleFile = pendingAll.Count > 0 ? Path.GetFileName(pendingAll[0].TargetPath) : null,
                    GlobalNextRunStatusText = nextStatus,
                    GlobalNextRunUtc = nextUtc,
                    GlobalNextRunQueueName = nextQName,
                    PostRunPowerAction = cfg.PostRunPowerAction,
                    InterruptedRun = _isRunning ? null : CloneCheckpoint(_recoverableInterruptedRun),
                    PendingRuns = _pendingRuns.Select(ClonePendingRun).ToList(),
                    JobStates = copyStates
                };
            }
        }

        private static PendingPipelineRunDto ClonePendingRun(PendingPipelineRunDto src)
        {
            return new PendingPipelineRunDto
            {
                ItemKey = src.ItemKey,
                RunKind = src.RunKind,
                TargetId = src.TargetId,
                DisplayName = src.DisplayName,
                IsDryRun = src.IsDryRun,
                ForceCloudResync = src.ForceCloudResync,
                TriggerSource = src.TriggerSource,
                EnqueuedAtUtc = src.EnqueuedAtUtc
            };
        }

        private static string BuildPendingItemKey(string runKind, string targetId) =>
            $"{runKind.Trim()}:{targetId.Trim()}";

        private bool IsTargetCurrentlyRunningLocked(string runKind, string targetId)
        {
            string itemKey = BuildPendingItemKey(runKind, targetId);
            if (_isTransitioningPendingRun &&
                !string.IsNullOrWhiteSpace(_transitioningItemKey) &&
                string.Equals(_transitioningItemKey, itemKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!_isRunning)
                return false;

            if (string.Equals(runKind, "Queue", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(_activeQueueId) &&
                       string.Equals(_activeQueueId, targetId, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(runKind, "SingleJob", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(_activeQueueId) &&
                       !string.IsNullOrWhiteSpace(_activeJobId) &&
                       string.Equals(_activeJobId, targetId, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private bool TryEnqueuePendingRunLocked(
            PendingPipelineRunDto candidate,
            bool isAutoTrigger,
            out int positionOneBased,
            out string rejectReason)
        {
            positionOneBased = 0;
            if (string.IsNullOrWhiteSpace(candidate.ItemKey))
            {
                candidate.ItemKey = BuildPendingItemKey(candidate.RunKind, candidate.TargetId);
            }

            if (IsTargetCurrentlyRunningLocked(candidate.RunKind, candidate.TargetId))
            {
                rejectReason = $"'{candidate.DisplayName}' is already running.";
                return false;
            }

            if (_pendingRuns.Count > 0 &&
                string.Equals(_pendingRuns[^1].ItemKey, candidate.ItemKey, StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = $"'{candidate.DisplayName}' is already queued next (consecutive duplicate queueing blocked).";
                return false;
            }

            if (_pendingRuns.Any(p => string.Equals(p.ItemKey, candidate.ItemKey, StringComparison.OrdinalIgnoreCase)))
            {
                rejectReason = $"'{candidate.DisplayName}' is already in the Up Next wait-list.";
                return false;
            }

            if (isAutoTrigger &&
                !string.IsNullOrWhiteSpace(_lastAutoCompletedItemKey) &&
                string.Equals(_lastAutoCompletedItemKey, candidate.ItemKey, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - _lastAutoCompletedAtUtc).TotalSeconds < 60.0)
            {
                rejectReason = $"'{candidate.DisplayName}' just completed an automatic run; suppressing immediate back-to-back auto-queue.";
                return false;
            }

            _pendingRuns.Add(ClonePendingRun(candidate));
            positionOneBased = _pendingRuns.Count;
            rejectReason = "";
            return true;
        }

        private List<string> ExtractPendingQueueIdsLocked() =>
            _pendingRuns
                .Where(p => string.Equals(p.RunKind, "Queue", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.TargetId)
                .ToList();

        private void StampQueueLastRunUtc(string? queueId, bool isDryRun)
        {
            if (isDryRun || string.IsNullOrWhiteSpace(queueId))
                return;

            var stampUtc = DateTime.UtcNow;
            _configService.MutateAndSave(cfg =>
            {
                var targetQ = cfg.NamedQueues.FirstOrDefault(q =>
                    string.Equals(q.Id, queueId, StringComparison.OrdinalIgnoreCase));
                if (targetQ != null)
                {
                    targetQ.LastRunUtc = stampUtc;
                }
            });
        }

        private PendingPipelineRunDto? DequeueNextPendingRunOnFinishLocked(bool isMidShutdownAbort)
        {
            if (isMidShutdownAbort || _userRequestedExplicitStop || _disposed || _pendingRuns.Count == 0)
                return null;

            var next = ClonePendingRun(_pendingRuns[0]);
            _pendingRuns.RemoveAt(0);
            _isTransitioningPendingRun = true;
            _transitioningItemKey = next.ItemKey;
            return next;
        }

        private bool DispatchDequeuedPendingRun(PendingPipelineRunDto initialNext)
        {
            PendingPipelineRunDto? current = initialNext;
            try
            {
                while (current != null)
                {
                    bool launched;
                    string msg;
                    if (string.Equals(current.RunKind, "SingleJob", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("HEADER", $"=== [Auto-Chain] Auto-launching next queued job: '{current.DisplayName}' ===");
                        var res = StartOrEnqueueSingleJobRun(
                            current.TargetId,
                            current.IsDryRun,
                            current.ForceCloudResync,
                            triggerSource: $"AutoChained:{current.TriggerSource}",
                            enqueueIfBusy: false,
                            isTransitionHandoff: true);
                        launched = res.Started;
                        msg = res.Message;
                    }
                    else
                    {
                        var nextQ = _configService.Config.NamedQueues.FirstOrDefault(q =>
                            string.Equals(q.Id, current.TargetId, StringComparison.OrdinalIgnoreCase));
                        if (nextQ == null)
                        {
                            launched = false;
                            msg = $"Queued job queue '{current.DisplayName}' ({current.TargetId}) no longer exists.";
                        }
                        else
                        {
                            Log("HEADER", $"=== [Auto-Chain] Auto-launching next queued job queue: '{nextQ.Name}' ===");
                            var res = StartOrEnqueueQueueRun(
                                nextQ.Id,
                                current.IsDryRun,
                                triggerSource: "AutoChainedQueue",
                                forceCloudResync: current.ForceCloudResync,
                                enqueueIfBusy: false,
                                isTransitionHandoff: true);
                            launched = res.Started;
                            msg = res.Message;
                        }
                    }

                    if (launched)
                        return true;

                    Log("WARN", $"[Auto-Chain] Skipped queued item '{current.DisplayName}': {msg}");

                    lock (_stateLock)
                    {
                        if (_userRequestedExplicitStop || _disposed || _pendingRuns.Count == 0)
                        {
                            current = null;
                        }
                        else
                        {
                            current = ClonePendingRun(_pendingRuns[0]);
                            _pendingRuns.RemoveAt(0);
                            _transitioningItemKey = current.ItemKey;
                        }
                    }
                }
                return false;
            }
            finally
            {
                lock (_stateLock)
                {
                    _isTransitioningPendingRun = false;
                    _transitioningItemKey = null;
                }
                BroadcastState();
            }
        }

        public List<PendingPipelineRunDto> GetPendingRuns()
        {
            lock (_stateLock)
            {
                return _pendingRuns.Select(ClonePendingRun).ToList();
            }
        }

        public bool RemovePendingRun(string itemKeyOrTargetId)
        {
            if (string.IsNullOrWhiteSpace(itemKeyOrTargetId))
                return false;

            string needle = itemKeyOrTargetId.Trim();
            string? removedName = null;
            lock (_stateLock)
            {
                int idx = _pendingRuns.FindIndex(p =>
                    string.Equals(p.ItemKey, needle, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.TargetId, needle, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    removedName = _pendingRuns[idx].DisplayName;
                    _pendingRuns.RemoveAt(idx);
                }
            }

            if (removedName == null)
                return false;

            PersistActiveCheckpointToDisk();
            Log("INFO", $"[Up Next] Removed '{removedName}' from pending execution queue.");
            BroadcastState();
            return true;
        }

        public int ClearPendingRuns()
        {
            int count;
            lock (_stateLock)
            {
                count = _pendingRuns.Count;
                _pendingRuns.Clear();
            }

            if (count > 0)
            {
                PersistActiveCheckpointToDisk();
                Log("INFO", $"[Up Next] Cleared {count} pending queued run(s).");
                BroadcastState();
            }
            return count;
        }

        private void BroadcastState()
        {
            _lastStateBroadcastUtc = DateTime.UtcNow;
            try
            {
                OnStateChanged?.Invoke(GetStateSnapshot());
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        private void ThrottledStateBroadcast(bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastStateBroadcastUtc).TotalMilliseconds < 120)
                return;
            BroadcastState();
        }

        public (bool Started, string Message) ResumeInterruptedRun(bool restartFullQueue = false)
        {
            InterruptedRunCheckpoint? cp;
            lock (_stateLock)
            {
                if (_isRunning)
                    return (false, "Wait for the active pipeline run to finish before resuming.");
                cp = CloneCheckpoint(_recoverableInterruptedRun);
            }

            if (cp == null)
                return (false, "No interrupted mid-shutdown run is waiting for recovery.");

            // Re-enqueue any FIFO scheduled or manual runs that were waiting behind the interrupted run
            if (cp.PendingRuns.Count > 0 || cp.PendingChainedQueueIds.Count > 0)
            {
                var snapshotForNames = _configService.Config;
                lock (_stateLock)
                {
                    if (cp.PendingRuns.Count > 0)
                    {
                        foreach (var pr in cp.PendingRuns)
                        {
                            if (pr != null && !string.IsNullOrWhiteSpace(pr.TargetId))
                            {
                                TryEnqueuePendingRunLocked(pr, isAutoTrigger: false, out _, out _);
                            }
                        }
                    }
                    else
                    {
                        foreach (string qid in cp.PendingChainedQueueIds)
                        {
                            if (string.IsNullOrWhiteSpace(qid)) continue;
                            var qObj = snapshotForNames.NamedQueues.FirstOrDefault(q =>
                                string.Equals(q.Id, qid, StringComparison.OrdinalIgnoreCase));
                            TryEnqueuePendingRunLocked(
                                new PendingPipelineRunDto
                                {
                                    ItemKey = BuildPendingItemKey("Queue", qid),
                                    RunKind = "Queue",
                                    TargetId = qid,
                                    DisplayName = qObj?.Name ?? qid,
                                    TriggerSource = "RecoveredPendingQueue",
                                    EnqueuedAtUtc = DateTime.UtcNow
                                },
                                isAutoTrigger: false,
                                out _,
                                out _);
                        }
                    }
                }
            }

            if (string.Equals(cp.RunKind, "HashVerifier", StringComparison.OrdinalIgnoreCase))
            {
                return StartHashSidecarVerifier(
                    cp.ActiveJobId,
                    cp.VerifierSourceModule ?? ModuleId.RomLauncherGenerator,
                    cp.VerifierMode ?? SidecarVerifyMode.FastHeaderAndHeal);
            }

            if (string.Equals(cp.RunKind, "ComplianceScan", StringComparison.OrdinalIgnoreCase))
            {
                return StartComplianceScan(cp.ActiveJobId);
            }

            if (string.Equals(cp.RunKind, "SingleJob", StringComparison.OrdinalIgnoreCase))
            {
                string targetJobId = cp.ActiveJobId ?? cp.InitialJobIds.FirstOrDefault() ?? "";
                if (string.IsNullOrWhiteSpace(targetJobId))
                {
                    DismissInterruptedRun();
                    return (false, "Interrupted single job ID could not be resolved.");
                }
                return StartSingleJobRun(targetJobId, cp.IsDryRun, cp.ForceCloudResync);
            }

            // Default: Queue recovery (either full queue restart or resume from interrupted job!)
            var snapshotCfg = _configService.Config.DeepClone();
            var queue = string.IsNullOrWhiteSpace(cp.QueueId)
                ? snapshotCfg.GetActiveQueue()
                : snapshotCfg.NamedQueues.FirstOrDefault(q => string.Equals(q.Id, cp.QueueId, StringComparison.OrdinalIgnoreCase));

            if (queue == null)
            {
                DismissInterruptedRun();
                return (false, $"Queue '{cp.QueueName ?? cp.QueueId}' no longer exists.");
            }

            var enabledJobs = queue.Jobs.Where(j => j.Enabled).ToList();
            if (!restartFullQueue && cp.CompletedJobIds.Count > 0)
            {
                var doneSet = new HashSet<string>(cp.CompletedJobIds, StringComparer.OrdinalIgnoreCase);
                var remaining = enabledJobs.Where(j => !doneSet.Contains(j.JobId)).ToList();
                if (remaining.Count > 0)
                {
                    return LaunchPipelineInternal(
                        snapshotCfg,
                        queue.Id,
                        queue.Name,
                        remaining,
                        cp.IsDryRun,
                        $"ResumedMidShutdown:{queue.Name}",
                        cp.ForceCloudResync,
                        priorCompletedJobIds: doneSet.ToList());
                }
            }

            return LaunchPipelineInternal(
                snapshotCfg,
                queue.Id,
                queue.Name,
                enabledJobs,
                cp.IsDryRun,
                $"RestartedMidShutdown:{queue.Name}",
                cp.ForceCloudResync);
        }

        public (bool Started, string Message) StartQueueRun(string? queueId = null, bool isDryRun = false, string triggerSource = "Manual", bool forceCloudResync = false)
        {
            var res = StartOrEnqueueQueueRun(queueId, isDryRun, triggerSource, forceCloudResync, enqueueIfBusy: false);
            return (res.Started, res.Message);
        }

        public (bool Accepted, bool Started, bool Queued, string Message) StartOrEnqueueQueueRun(
            string? queueId = null,
            bool isDryRun = false,
            string triggerSource = "Manual",
            bool forceCloudResync = false,
            bool enqueueIfBusy = true,
            bool isTransitionHandoff = false)
        {
            var snapshotCfg = _configService.Config.DeepClone();
            var queue = string.IsNullOrWhiteSpace(queueId)
                ? snapshotCfg.GetActiveQueue()
                : snapshotCfg.NamedQueues.FirstOrDefault(q => string.Equals(q.Id, queueId, StringComparison.OrdinalIgnoreCase));

            if (queue == null)
                return (false, false, false, $"Queue '{queueId}' was not found.");

            var enabledJobs = queue.Jobs.Where(j => j.Enabled).ToList();
            var runnableJobs = enabledJobs.Where(j => snapshotCfg.IsModuleAppWideEnabled(j.ModuleType)).ToList();
            if (runnableJobs.Count == 0)
            {
                var disabledJobs = enabledJobs.Where(j => !snapshotCfg.IsModuleAppWideEnabled(j.ModuleType)).ToList();
                string errMsg = disabledJobs.Count > 0
                    ? $"Selected job(s) could not run because their module(s) are disabled app-wide: {string.Join(", ", disabledJobs.Select(j => j.Name))}."
                    : "No enabled jobs found to run in this queue.";
                return (false, false, false, errMsg);
            }

            bool effectiveDryRun = isDryRun || ForceDryRun;
            bool shouldEnqueue = false;
            int queuedPos = 0;
            string rejectReason = "";

            lock (_stateLock)
            {
                if (_isRunning || (_isTransitioningPendingRun && !isTransitionHandoff))
                {
                    if (!enqueueIfBusy)
                    {
                        return (false, false, false, "A Housekeeping pipeline or scan is already running.");
                    }

                    shouldEnqueue = TryEnqueuePendingRunLocked(
                        new PendingPipelineRunDto
                        {
                            ItemKey = BuildPendingItemKey("Queue", queue.Id),
                            RunKind = "Queue",
                            TargetId = queue.Id,
                            DisplayName = queue.Name,
                            IsDryRun = effectiveDryRun,
                            ForceCloudResync = forceCloudResync,
                            TriggerSource = triggerSource,
                            EnqueuedAtUtc = DateTime.UtcNow
                        },
                        isAutoTrigger: false,
                        out queuedPos,
                        out rejectReason);

                    if (!shouldEnqueue)
                    {
                        return (false, false, false, rejectReason);
                    }
                }
            }

            if (shouldEnqueue)
            {
                PersistActiveCheckpointToDisk();
                string queuedMsg = $"Queued '{queue.Name}'{(effectiveDryRun ? " [DRY RUN]" : "")} to execute next (#{queuedPos} in Up Next wait-list).";
                Log("INFO", $"[Up Next] {queuedMsg}");
                BroadcastState();
                return (true, false, true, queuedMsg);
            }

            var (started, startMsg) = LaunchPipelineInternal(snapshotCfg, queue.Id, queue.Name, enabledJobs, isDryRun, triggerSource, forceCloudResync);
            return (started, started, false, startMsg);
        }

        public (bool Started, string Message) StartSingleJobRun(string jobId, bool isDryRun = false, bool forceCloudResync = false)
        {
            var res = StartOrEnqueueSingleJobRun(jobId, isDryRun, forceCloudResync, triggerSource: null, enqueueIfBusy: false);
            return (res.Started, res.Message);
        }

        public (bool Accepted, bool Started, bool Queued, string Message) StartOrEnqueueSingleJobRun(
            string jobId,
            bool isDryRun = false,
            bool forceCloudResync = false,
            string? triggerSource = null,
            bool enqueueIfBusy = true,
            bool isTransitionHandoff = false)
        {
            var snapshotCfg = _configService.Config.DeepClone();
            var job = snapshotCfg.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                      ?? snapshotCfg.NamedQueues.SelectMany(q => q.Jobs).FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));

            if (job == null)
                return (false, false, false, $"Job '{jobId}' was not found.");

            if (!snapshotCfg.IsModuleAppWideEnabled(job.ModuleType))
            {
                return (false, false, false, $"Selected job(s) could not run because their module(s) are disabled app-wide: {job.Name}.");
            }

            string effectiveTrigger = string.IsNullOrWhiteSpace(triggerSource) ? $"ManualJob:{job.Name}" : triggerSource!;
            bool effectiveDryRun = isDryRun || ForceDryRun;
            bool shouldEnqueue = false;
            int queuedPos = 0;
            string rejectReason = "";

            lock (_stateLock)
            {
                if (_isRunning || (_isTransitioningPendingRun && !isTransitionHandoff))
                {
                    if (!enqueueIfBusy)
                    {
                        return (false, false, false, "A Housekeeping pipeline or scan is already running.");
                    }

                    shouldEnqueue = TryEnqueuePendingRunLocked(
                        new PendingPipelineRunDto
                        {
                            ItemKey = BuildPendingItemKey("SingleJob", job.JobId),
                            RunKind = "SingleJob",
                            TargetId = job.JobId,
                            DisplayName = job.Name,
                            IsDryRun = effectiveDryRun,
                            ForceCloudResync = forceCloudResync,
                            TriggerSource = effectiveTrigger,
                            EnqueuedAtUtc = DateTime.UtcNow
                        },
                        isAutoTrigger: false,
                        out queuedPos,
                        out rejectReason);

                    if (!shouldEnqueue)
                    {
                        return (false, false, false, rejectReason);
                    }
                }
            }

            if (shouldEnqueue)
            {
                PersistActiveCheckpointToDisk();
                string queuedMsg = $"Queued job '{job.Name}'{(effectiveDryRun ? " [DRY RUN]" : "")} to execute next (#{queuedPos} in Up Next wait-list).";
                Log("INFO", $"[Up Next] {queuedMsg}");
                BroadcastState();
                return (true, false, true, queuedMsg);
            }

            var (started, startMsg) = LaunchPipelineInternal(snapshotCfg, null, $"Single Job: {job.Name}", new List<HousekeepingJob> { job }, isDryRun, effectiveTrigger, forceCloudResync);
            return (started, started, false, startMsg);
        }

        private (bool Started, string Message) LaunchPipelineInternal(
            AppConfig immutableRunConfig,
            string? queueId,
            string queueName,
            List<HousekeepingJob> requestedJobs,
            bool isDryRun,
            string triggerSource,
            bool forceCloudResync,
            List<string>? priorCompletedJobIds = null)
        {
            isDryRun = isDryRun || ForceDryRun;

            lock (_stateLock)
            {
                if (_isRunning)
                {
                    return (false, "A Housekeeping pipeline or scan is already running.");
                }
            }

            var disabledJobs = requestedJobs.Where(j => !immutableRunConfig.IsModuleAppWideEnabled(j.ModuleType)).ToList();
            foreach (var dj in disabledJobs)
            {
                Log("WARN", $"[Skip] Job '{dj.Name}' skipped: Module '{dj.ModuleTypeBadgeText}' is disabled app-wide in Modules.");
            }

            var jobsToRun = requestedJobs.Where(j => immutableRunConfig.IsModuleAppWideEnabled(j.ModuleType)).ToList();
            if (jobsToRun.Count == 0)
            {
                return disabledJobs.Count > 0
                    ? (false, $"Selected job(s) could not run because their module(s) are disabled app-wide: {string.Join(", ", disabledJobs.Select(j => j.Name))}.")
                    : (false, "No enabled jobs found to run in this queue.");
            }

            lock (_stateLock)
            {
                if (_isRunning)
                {
                    return (false, "A Housekeeping pipeline or scan is already running.");
                }

                _isRunning = true;
                _isPaused = false;
                _isDryRun = isDryRun;
                _userRequestedExplicitStop = false;
                _recoverableInterruptedRun = null;
                _activeQueueId = queueId;
                _activeQueueName = queueName;
                _activeRunCts = new CancellationTokenSource();
                _currentJobIndex = 0;
                _totalJobs = jobsToRun.Count;
                _activeJobId = jobsToRun[0].JobId;
                _activeJobName = jobsToRun[0].Name;
                _activeModuleBadge = jobsToRun[0].ModuleTypeBadgeText;
                _progressPercent = 0;
                _subprocessStatusText = "Starting pipeline execution...";

                var initialIds = priorCompletedJobIds != null
                    ? priorCompletedJobIds.Concat(jobsToRun.Select(j => j.JobId)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : jobsToRun.Select(j => j.JobId).ToList();

                _activeCheckpoint = new InterruptedRunCheckpoint
                {
                    RunKind = string.IsNullOrWhiteSpace(queueId) ? "SingleJob" : "Queue",
                    QueueId = queueId,
                    QueueName = queueName,
                    InitialJobIds = initialIds,
                    CompletedJobIds = priorCompletedJobIds != null ? new List<string>(priorCompletedJobIds) : new List<string>(),
                    ActiveJobId = jobsToRun[0].JobId,
                    ActiveJobName = jobsToRun[0].Name,
                    PendingChainedQueueIds = ExtractPendingQueueIdsLocked(),
                    PendingRuns = _pendingRuns.Select(ClonePendingRun).ToList(),
                    IsDryRun = isDryRun,
                    ForceCloudResync = forceCloudResync,
                    LastProgressPercent = 0,
                    LastStatusText = "Starting pipeline execution...",
                    StartedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                foreach (var j in jobsToRun)
                {
                    _jobStates[j.JobId] = new JobLiveStatusDto
                    {
                        JobId = j.JobId,
                        JobName = j.Name,
                        State = "Queued",
                        ProgressPercent = 0,
                        DetailText = "Queued..."
                    };
                }

                _activeExecutionTask = Task.Run(() => ExecutePipelineAsync(immutableRunConfig, queueId, queueName, jobsToRun, isDryRun, triggerSource, forceCloudResync, _activeRunCts.Token));
            }

            StampQueueLastRunUtc(queueId, isDryRun);
            PersistActiveCheckpointToDisk();
            BroadcastState();
            return (true, $"Started {(isDryRun ? "Dry Run" : "Run")} for '{queueName}' ({jobsToRun.Count} job(s)).");
        }

        private async Task ExecutePipelineAsync(
            AppConfig immutableRunConfig,
            string? queueId,
            string queueName,
            List<HousekeepingJob> initialJobs,
            bool isDryRun,
            string triggerSource,
            bool forceCloudResync,
            CancellationToken ct)
        {
            NativeProcessSuspend.PreventWindowsSleep(true);
            _telemetryService.SetExecutionState(true, initialJobs[0].Name, "Starting pipeline execution...", 0, queueId, queueName, 1, initialJobs.Count);

            string runsDir = Path.Combine(_configService.ActiveLogsDir, "runs");
            Directory.CreateDirectory(runsDir);
            string cleanQueueName = string.Concat(queueName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string logFileName = $"Run_{cleanQueueName}_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            string logFilePath = Path.Combine(runsDir, logFileName);

            try
            {
                var writer = new StreamWriter(logFilePath, append: true, Encoding.UTF8) { AutoFlush = true };
                _logBuffer.AttachRunWriter(writer);
            }
            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx5.Message}"); }

            var startTime = DateTime.Now;
            int totalProcessed = 0;
            int totalSkipped = 0;
            int totalErrors = 0;
            string runStatus = isDryRun ? "Dry Run Completed" : "Completed";
            var executedJobNames = new List<string>();

            Log("HEADER", "================================================================================");
            Log("HEADER", $"Starting Housekeeping {(isDryRun ? "[DRY RUN] " : "")}({triggerSource}) — {initialJobs.Count} job(s) in '{queueName}'");
            Log("HEADER", "================================================================================");

            _ = _scheduler.NotifyQueueStartedAsync(queueName, initialJobs.Count, isDryRun);

            // Dynamic execution queue supporting OnSuccessTriggerJobId post-job chaining with cycle guard
            var executionQueue = new Queue<HousekeepingJob>(initialJobs.Select(j => j.DeepClone()));
            var visitedJobIds = new HashSet<string>(initialJobs.Select(j => j.JobId), StringComparer.OrdinalIgnoreCase);

            try
            {
                int currentJobIdx = 0;
                while (executionQueue.Count > 0)
                {
                    var job = executionQueue.Dequeue();
                    currentJobIdx++;

                    lock (_stateLock)
                    {
                        _currentJobIndex = currentJobIdx;
                        _totalJobs = currentJobIdx + executionQueue.Count;
                        _activeJobId = job.JobId;
                        _activeJobName = job.Name;
                        _activeModuleBadge = job.ModuleTypeBadgeText;
                        _progressPercent = 2;
                        _subprocessStatusText = $"[{job.ModuleTypeBadgeText}] Starting {job.Name}...";
                        _footerStatusText = $"Running {job.Name}...";
                        if (_activeCheckpoint != null)
                        {
                            _activeCheckpoint.ActiveJobId = job.JobId;
                            _activeCheckpoint.ActiveJobName = job.Name;
                            _activeCheckpoint.LastProgressPercent = 2;
                            _activeCheckpoint.LastStatusText = _subprocessStatusText;
                        }
                        _jobStates[job.JobId] = new JobLiveStatusDto
                        {
                            JobId = job.JobId,
                            JobName = job.Name,
                            State = "Running",
                            ProgressPercent = 2,
                            DetailText = "Starting job..."
                        };
                    }
                    PersistActiveCheckpointToDisk();
                    BroadcastState();

                    ct.ThrowIfCancellationRequested();
                    using var linkedJobCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lock (_stateLock) { _activeJobCts = linkedJobCts; }
                    var jobCt = linkedJobCts.Token;

                    int totalPlannedNow;
                    lock (_stateLock) { totalPlannedNow = _totalJobs; }
                    _telemetryService.SetExecutionState(true, job.Name, $"Running {job.Name}...", 0, queueId, queueName, currentJobIdx, totalPlannedNow);

                    var jobStartTime = DateTime.Now;
                    executedJobNames.Add(job.Name);
                    _ = _scheduler.NotifyJobStartedAsync(job.Name, job.ModuleTypeBadgeText, queueName);
                    Log("HEADER", $"--- Job: {job.Name} ({job.ModuleTypeBadgeText}) ---");

                    // Snapshot manifest file fingerprints before Module 8 runs
                    var preManifestFingerprints = CaptureCandidateManifestFingerprints(job);

                    ModuleExecutionContext? ctx = null;
                    ctx = new ModuleExecutionContext
                    {
                        Config = immutableRunConfig,
                        ActiveJob = job,
                        ConfigService = _configService,
                        Dependencies = _dependencyService,
                        NetworkSafety = _networkSafety,
                        ProcessRunner = _processRunner,
                        IsDryRun = isDryRun,
                        WaitIfQueuePausedAsync = WaitIfQueuePausedAsync,
                        Log = Log,
                        UpdateModuleProgress = (pct, detail) =>
                        {
                            double clamped = Math.Clamp(pct, 0, 100);
                            string badge = job.ModuleTypeBadgeText;
                            lock (_stateLock)
                            {
                                _progressPercent = clamped;
                                _subprocessStatusText = $"[{badge}] {detail}";
                                if (TerminalLogBufferService.ShouldUpdateFooter("WORK", immutableRunConfig))
                                {
                                    _footerStatusText = $"[{badge} | {job.Name}] {detail}";
                                }
                                if (_jobStates.TryGetValue(job.JobId, out var js))
                                {
                                    js.ProgressPercent = clamped;
                                    js.DetailText = detail;
                                    if (ctx != null)
                                    {
                                        js.Processed = ctx.Stats.Processed;
                                        js.Skipped = ctx.Stats.Skipped;
                                        js.Errors = ctx.Stats.Errors;
                                    }
                                }
                            }
                            ThrottledProgressBroadcast(clamped, $"[{badge}] {detail}");
                        }
                    };

                    ctx.Stats.OnStatsDelta = (p, s, e) =>
                    {
                        lock (_stateLock)
                        {
                            _sessionProcessed += p;
                            _sessionSkipped += s;
                            _sessionErrors += e;
                            if (_jobStates.TryGetValue(job.JobId, out var js))
                            {
                                js.Processed = ctx.Stats.Processed;
                                js.Skipped = ctx.Stats.Skipped;
                                js.Errors = ctx.Stats.Errors;
                            }
                        }
                        ThrottledStateBroadcast();
                    };

                    bool jobCompletedCleanly = false;
                    try
                    {
                        await WaitIfQueuePausedAsync(jobCt).ConfigureAwait(false);
                        if (CustomModuleExecutor != null)
                        {
                            await CustomModuleExecutor(ctx, jobCt).ConfigureAwait(false);
                        }
                        else
                        {
                            switch (job.ModuleType)
                            {
                                case ModuleId.ProxyGenerator:
                                    await ProxyGeneratorModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.ArchiveCompressor:
                                    await ArchiveCompressorModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.MediaTranscoder:
                                    await MediaTranscoderModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.RomLauncherGenerator:
                                    await RomLauncherGeneratorModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.CloudSync:
                                    await CloudSyncModule.RunAsync(ctx, forceCloudResync, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.HumbleSync:
                                    await HumbleSyncModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.CustomScripts:
                                    await CustomScriptsModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                                case ModuleId.CollectionManifest:
                                    await CollectionManifestModule.RunAsync(ctx, jobCt).ConfigureAwait(false);
                                    break;
                            }
                        }
                        jobCompletedCleanly = !jobCt.IsCancellationRequested;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        if (executionQueue.Count == 0 && initialJobs.Count == 1)
                            runStatus = "Cancelled";
                        Log("WARN", $"[Stop Job] Job '{job.Name}' cancelled — advancing queue ({executionQueue.Count} remaining in active queue)...");
                    }
                    catch (Exception ex) when (!jobCt.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        _ = _scheduler.NotifyErrorAsync(job.Name, ex.Message);
                        throw;
                    }
                    finally
                    {
                        totalProcessed += ctx.Stats.Processed;
                        totalSkipped += ctx.Stats.Skipped;
                        totalErrors += ctx.Stats.Errors;

                        _ = _scheduler.NotifyJobFinishedAsync(job.Name, job.ModuleTypeBadgeText, ctx.Stats.Processed, ctx.Stats.Skipped, ctx.Stats.Errors, DateTime.Now - jobStartTime);

                        lock (_stateLock)
                        {
                            if (ReferenceEquals(_activeJobCts, linkedJobCts))
                                _activeJobCts = null;
                            if (_jobStates.TryGetValue(job.JobId, out var js))
                            {
                                js.Processed = ctx.Stats.Processed;
                                js.Skipped = ctx.Stats.Skipped;
                                js.Errors = ctx.Stats.Errors;
                                if (jobCt.IsCancellationRequested || ct.IsCancellationRequested)
                                {
                                    js.State = "Cancelled";
                                    js.DetailText = "Cancelled by user";
                                }
                                else
                                {
                                    js.ProgressPercent = 100;
                                    js.State = ctx.Stats.Errors > 0 ? "CompletedWithErrors" : "Completed";
                                    js.DetailText = $"{ctx.Stats.Processed} processed • {ctx.Stats.Skipped} skipped • {ctx.Stats.Errors} err";
                                }
                            }
                        }
                        BroadcastState();
                    }

                    if (jobCompletedCleanly)
                    {
                        lock (_stateLock)
                        {
                            if (_activeCheckpoint != null &&
                                !_activeCheckpoint.CompletedJobIds.Contains(job.JobId, StringComparer.OrdinalIgnoreCase))
                            {
                                _activeCheckpoint.CompletedJobIds.Add(job.JobId);
                            }
                        }
                        PersistActiveCheckpointToDisk();
                    }

                    // 1. Post-Manifest Greenroom Auto-Sync (only when manifest files actually changed!)
                    if (jobCompletedCleanly && !isDryRun &&
                        (job.ModuleType == ModuleId.CollectionManifest || job.ModuleType == ModuleId.RomLauncherGenerator))
                    {
                        bool perJobSyncEnabled = job.ModuleType == ModuleId.CollectionManifest
                            ? (job.CollectionManifest ?? immutableRunConfig.CollectionManifest).AutoSyncGreenroomOnManifestChange
                            : (job.RomLauncherGenerator ?? immutableRunConfig.RomLauncherGenerator).AutoSyncGreenroomOnManifestChange;

                        if (perJobSyncEnabled)
                        {
                            var postManifestFingerprints = CaptureCandidateManifestFingerprints(job);
                            var changedManifestPaths = ComputeChangedManifestPaths(preManifestFingerprints, postManifestFingerprints);
                            await _greenroomSync.TriggerPostManifestSyncAsync(
                                job.ModuleType,
                                changedManifestPaths,
                                isDryRun: false,
                                log: Log,
                                forceManual: false,
                                ct: ct).ConfigureAwait(false);
                        }
                    }

                    // 2. General Post-Job Chaining (OnSuccessTriggerJobId with cycle guard)
                    if (jobCompletedCleanly && !string.IsNullOrWhiteSpace(job.OnSuccessTriggerJobId))
                    {
                        string followUpId = job.OnSuccessTriggerJobId.Trim();
                        if (!visitedJobIds.Add(followUpId))
                        {
                            Log("WARN", $"[Post-Job Chain] Skipping follow-up job '{followUpId}' from '{job.Name}' to prevent a circular trigger loop.");
                        }
                        else
                        {
                            var followUpJob = immutableRunConfig.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, followUpId, StringComparison.OrdinalIgnoreCase))
                                              ?? immutableRunConfig.NamedQueues.SelectMany(q => q.Jobs).FirstOrDefault(j => string.Equals(j.JobId, followUpId, StringComparison.OrdinalIgnoreCase));

                            if (followUpJob == null)
                            {
                                Log("WARN", $"[Post-Job Chain] Follow-up job ID '{followUpId}' configured on '{job.Name}' no longer exists.");
                            }
                            else if (!immutableRunConfig.IsModuleAppWideEnabled(followUpJob.ModuleType))
                            {
                                Log("WARN", $"[Post-Job Chain] Follow-up job '{followUpJob.Name}' skipped because module '{followUpJob.ModuleTypeBadgeText}' is disabled app-wide.");
                            }
                            else
                            {
                                Log("HEADER", $"[Post-Job Chain] Job '{job.Name}' succeeded -> chaining follow-up job '{followUpJob.Name}' ({followUpJob.ModuleTypeBadgeText})!");
                                executionQueue.Enqueue(followUpJob.DeepClone());
                                lock (_stateLock)
                                {
                                    _totalJobs = currentJobIdx + executionQueue.Count;
                                    if (_activeCheckpoint != null &&
                                        !_activeCheckpoint.InitialJobIds.Contains(followUpJob.JobId, StringComparer.OrdinalIgnoreCase))
                                    {
                                        _activeCheckpoint.InitialJobIds.Add(followUpJob.JobId);
                                    }
                                    _jobStates[followUpJob.JobId] = new JobLiveStatusDto
                                    {
                                        JobId = followUpJob.JobId,
                                        JobName = followUpJob.Name,
                                        State = "Queued",
                                        ProgressPercent = 0,
                                        DetailText = $"Chained by {job.Name}..."
                                    };
                                }
                                PersistActiveCheckpointToDisk();
                                BroadcastState();
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                runStatus = "Cancelled";
                Log("WARN", "Housekeeping pipeline cancelled by user.");
            }
            catch (Exception ex)
            {
                runStatus = "Failed";
                totalErrors++;
                Log("ERROR", $"Unhandled pipeline exception: {ex.Message}");
            }
            finally
            {
                NativeProcessSuspend.PreventWindowsSleep(false);
                _telemetryService.SetExecutionState(false, null, "Ready — Pipeline idle", 0, null, null, 0, 0);

                var entry = new RunHistoryEntry
                {
                    StartedAt = startTime,
                    CompletedAt = DateTime.Now,
                    TriggerSource = isDryRun ? "DryRun" : triggerSource,
                    ModulesSummary = string.Join(" → ", executedJobNames.Count > 0 ? executedJobNames : initialJobs.Select(j => j.Name)),
                    ItemsProcessed = totalProcessed,
                    ItemsSkipped = totalSkipped,
                    ErrorsCount = totalErrors,
                    Status = totalErrors > 0 && runStatus == "Completed" ? "Completed with Errors" : runStatus,
                    LogFilePath = logFilePath
                };

                _configService.AddHistoryEntry(entry);
                _complianceReports.Load();

                Log("HEADER", $"=== Finished: {entry.Status} | Processed: {totalProcessed}, Skipped: {totalSkipped}, Errors: {totalErrors} ({entry.DurationText}) ===");
                _logBuffer.DetachRunWriter();

                bool isMidShutdownAbort;
                bool wasUserStopped;
                lock (_stateLock)
                {
                    wasUserStopped = _userRequestedExplicitStop;
                    isMidShutdownAbort = (ct.IsCancellationRequested || _disposed) && !_userRequestedExplicitStop;
                }
                FinalizeCheckpointAfterRun(ct.IsCancellationRequested || _disposed);

                if (!isMidShutdownAbort && !wasUserStopped)
                {
                    StampQueueLastRunUtc(queueId, isDryRun);
                }

                bool wasAutoTriggered =
                    triggerSource.StartsWith("Schedule_", StringComparison.OrdinalIgnoreCase) ||
                    triggerSource.StartsWith("AutoChained", StringComparison.OrdinalIgnoreCase);

                PendingPipelineRunDto? nextPending = null;
                lock (_stateLock)
                {
                    if (!isMidShutdownAbort && !wasUserStopped && wasAutoTriggered)
                    {
                        if (!string.IsNullOrWhiteSpace(queueId))
                        {
                            _lastAutoCompletedItemKey = BuildPendingItemKey("Queue", queueId);
                            _lastAutoCompletedAtUtc = DateTime.UtcNow;
                        }
                        else if (initialJobs.Count == 1)
                        {
                            _lastAutoCompletedItemKey = BuildPendingItemKey("SingleJob", initialJobs[0].JobId);
                            _lastAutoCompletedAtUtc = DateTime.UtcNow;
                        }
                    }

                    _isRunning = false;
                    _isPaused = false;
                    _isDryRun = false;
                    _activeQueueId = null;
                    _activeQueueName = null;
                    _activeJobId = null;
                    _activeJobName = null;
                    _activeModuleBadge = null;
                    _currentJobIndex = 0;
                    _totalJobs = 0;
                    _progressPercent = 0;
                    _subprocessStatusText = "Ready — Pipeline idle";
                    _footerStatusText = $"Last run finished at {DateTime.Now:HH:mm:ss} ({entry.Status}).";
                    _isWaitingForNetwork = false;
                    _networkWaitMessage = "";
                    try { _activeRunCts?.Dispose(); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx6.Message}"); }
                    _activeRunCts = null;

                    nextPending = DequeueNextPendingRunOnFinishLocked(isMidShutdownAbort);
                }

                BroadcastState();
                await _scheduler.NotifyRunCompletedAsync(entry).ConfigureAwait(false);

                if (_pendingSwapService.Count > 0)
                {
                    _ = Task.Run(() =>
                    {
                        if (Interlocked.CompareExchange(ref _isSweepingPendingSwaps, 1, 0) != 0) return;
                        try
                        {
                            _pendingSwapService.SweepAndCommitAll(Log);
                            BroadcastState();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _isSweepingPendingSwaps, 0);
                        }
                    });
                }

                bool launchedChained = nextPending != null && DispatchDequeuedPendingRun(nextPending);
                if (!launchedChained)
                {
                    string postAction = _configService.Config.PostRunPowerAction;
                    if (!isDryRun && !_configService.IsTestMode && runStatus != "Cancelled" &&
                        (string.Equals(postAction, "Sleep", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(postAction, "Shutdown", StringComparison.OrdinalIgnoreCase)))
                    {
                        Log("WARN", $"Executing post-run power action: {postAction}...");
                        NativeProcessSuspend.ExecutePostRunPowerAction(postAction);
                    }
                }
            }
        }

        private Dictionary<string, string> CaptureCandidateManifestFingerprints(HousekeepingJob job)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (job.ModuleType == ModuleId.CollectionManifest)
                {
                    var man = job.CollectionManifest ?? _configService.Config.CollectionManifest;
                    string outDir = string.IsNullOrWhiteSpace(man.UnifiedManifestOutputFolder)
                        ? Path.Combine(_configService.DataDirectory, "manifests")
                        : (Path.IsPathRooted(man.UnifiedManifestOutputFolder)
                            ? man.UnifiedManifestOutputFolder
                            : Path.GetFullPath(Path.Combine(_configService.DataDirectory, man.UnifiedManifestOutputFolder)));

                    if (Directory.Exists(outDir))
                    {
                        foreach (var f in Directory.GetFiles(outDir, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            string ext = Path.GetExtension(f);
                            if (ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
                                candidates.Add(f);
                        }
                    }

                    foreach (var tf in man.TargetFolders.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.FolderPath)))
                    {
                        if (Directory.Exists(tf.FolderPath))
                        {
                            foreach (var f in Directory.GetFiles(tf.FolderPath, "*Manifest*.*", SearchOption.TopDirectoryOnly))
                                candidates.Add(f);
                        }
                    }
                }
                else if (job.ModuleType == ModuleId.RomLauncherGenerator)
                {
                    var rom = job.RomLauncherGenerator ?? _configService.Config.RomLauncherGenerator;
                    if (!string.IsNullOrWhiteSpace(rom.LaunchersOutputRootPath) && !string.IsNullOrWhiteSpace(rom.ManifestCsvFileName))
                        candidates.Add(Path.Combine(rom.LaunchersOutputRootPath, rom.ManifestCsvFileName));
                }

                foreach (string path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    try
                    {
                        string full = Path.GetFullPath(path);
                        using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        byte[] sha = SHA256.HashData(fs);
                        map[full] = $"{fs.Length}:{Convert.ToHexString(sha)}";
                    }
                    catch (System.Exception __suppressedEx7) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx7.Message}"); }
                }
            }
            catch (System.Exception __suppressedEx8) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx8.Message}"); }

            return map;
        }

        private static List<string> ComputeChangedManifestPaths(
            Dictionary<string, string> before,
            Dictionary<string, string> after)
        {
            var changed = new List<string>();
            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out string? prevSig) || !string.Equals(prevSig, kvp.Value, StringComparison.OrdinalIgnoreCase))
                {
                    changed.Add(kvp.Key);
                }
            }
            return changed;
        }

        public async Task<int> RetryPendingSwapsAsync()
        {
            Log("INFO", "[PendingSwap] Manually triggering retry swap for all pending files...");
            int committed = await Task.Run(() => _pendingSwapService.SweepAndCommitAll(Log)).ConfigureAwait(false);
            if (committed > 0)
            {
                Log("SUCCESS", $"[PendingSwap] Successfully committed {committed} pending file(s)!");
            }
            else if (_pendingSwapService.Count > 0)
            {
                Log("WARN", "[PendingSwap] Destination files are still locked by another process (e.g. Plex/SMB). Housekeeping will continue retrying automatically.");
            }
            BroadcastState();
            return committed;
        }

        public void DiscardPendingSwaps()
        {
            if (_pendingSwapService.Count == 0) return;
            _pendingSwapService.DiscardAll(Log);
            BroadcastState();
        }

        public (bool Started, string Message) StartComplianceScan(string? jobId = null)
        {
            lock (_stateLock)
            {
                if (_isRunning)
                    return (false, "Wait for the active pipeline run to finish before starting a compliance scan.");
            }

            var rom = ResolveRomConfig(jobId);
            rom.EnsureArchiveTemplatesInitialized();
            var existingCols = rom.Collections
                .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ArchivesPath) && Directory.Exists(c.ArchivesPath))
                .ToList();

            if (existingCols.Count == 0)
                return (false, "No enabled ROM Console Collection directories exist on disk to scan.");

            _activeExecutionTask = Task.Run(() => RunComplianceScanAsync(jobId));
            return (true, $"Started Archive Structure Compliance Scan across {existingCols.Count} console collection(s). Watch live progress in the Terminal Drawer.");
        }

        public async Task<(bool Success, int FlaggedCount, string Message)> RunComplianceScanAsync(string? jobId = null)
        {
            lock (_stateLock)
            {
                if (_isRunning)
                    return (false, 0, "Wait for the active pipeline run to finish before starting a compliance scan.");
                _isRunning = true;
                _userRequestedExplicitStop = false;
                _recoverableInterruptedRun = null;
                _activeRunCts = new CancellationTokenSource();
                _activeQueueName = "Archive Compliance Scan";
                _subprocessStatusText = "Scanning archive compliance...";
                _activeCheckpoint = new InterruptedRunCheckpoint
                {
                    RunKind = "ComplianceScan",
                    QueueName = "Archive Compliance Scan",
                    ActiveJobId = jobId,
                    PendingChainedQueueIds = ExtractPendingQueueIdsLocked(),
                    PendingRuns = _pendingRuns.Select(ClonePendingRun).ToList(),
                    LastProgressPercent = 0,
                    LastStatusText = "Scanning archive compliance...",
                    StartedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
            PersistActiveCheckpointToDisk();
            BroadcastState();

            var ct = _activeRunCts!.Token;
            var rom = ResolveRomConfig(jobId);
            rom.EnsureArchiveTemplatesInitialized();

            var existingCols = rom.Collections
                .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ArchivesPath) && Directory.Exists(c.ArchivesPath))
                .ToList();

            if (existingCols.Count == 0)
            {
                ClearInterruptedCheckpoint();
                lock (_stateLock)
                {
                    _isRunning = false;
                    _activeQueueName = null;
                    _subprocessStatusText = "Ready — Pipeline idle";
                    _activeRunCts?.Dispose();
                    _activeRunCts = null;
                }
                BroadcastState();
                return (false, 0, "No enabled ROM Console Collection directories exist on disk to scan.");
            }

            string sevenZip = _dependencyService.ResolveSevenZip();
            Log("HEADER", "================================================================================");
            Log("HEADER", $"Starting Zero-Decompression Archive Structure Compliance Scan ({existingCols.Count} console collection(s))");
            Log("HEADER", "================================================================================");

            int totalFindings = 0;
            try
            {
                for (int colIdx = 0; colIdx < existingCols.Count; colIdx++)
                {
                    var col = existingCols[colIdx];
                    ct.ThrowIfCancellationRequested();
                    double colPct = Math.Clamp((colIdx * 100.0) / existingCols.Count, 0, 100);
                    string colStatus = $"Scanning archive compliance ({colIdx + 1}/{existingCols.Count}): {col.ConsoleName}...";
                    lock (_stateLock)
                    {
                        _progressPercent = colPct;
                        _subprocessStatusText = colStatus;
                        _footerStatusText = colStatus;
                        if (_activeCheckpoint != null)
                        {
                            _activeCheckpoint.LastProgressPercent = colPct;
                            _activeCheckpoint.LastStatusText = colStatus;
                        }
                    }
                    PersistActiveCheckpointToDisk();
                    BroadcastState();

                    var colReports = await Task.Run(() => ArchiveComplianceService.ScanConsoleCollectionAsync(
                        col,
                        rom,
                        sevenZip,
                        _complianceReports,
                        msg => Log("INFO", msg),
                        ct,
                        onStatsDelta: (p, s, e) =>
                        {
                            lock (_stateLock)
                            {
                                _sessionProcessed += p;
                                _sessionSkipped += s;
                                _sessionErrors += e;
                            }
                            ThrottledStateBroadcast();
                        }), ct).ConfigureAwait(false);
                    totalFindings += colReports.Count;
                }

                string doneMsg = $"Archive compliance scan complete — {totalFindings} archive/folder issue(s) flagged across {existingCols.Count} console(s).";
                Log("SUCCESS", $"[Compliance Scan Complete] {doneMsg}");
                return (true, totalFindings, doneMsg);
            }
            catch (OperationCanceledException)
            {
                Log("WARN", "[Compliance Scan] Cancelled by user.");
                return (false, totalFindings, "Archive compliance scan cancelled.");
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[Compliance Scan] Error: {ex.Message}");
                return (false, totalFindings, ex.Message);
            }
            finally
            {
                bool isMidShutdownAbort;
                lock (_stateLock)
                {
                    isMidShutdownAbort = (ct.IsCancellationRequested || _disposed) && !_userRequestedExplicitStop;
                }
                FinalizeCheckpointAfterRun(ct.IsCancellationRequested || _disposed);
                PendingPipelineRunDto? nextPending = null;
                lock (_stateLock)
                {
                    _isRunning = false;
                    _activeQueueName = null;
                    _progressPercent = 0;
                    _subprocessStatusText = "Ready — Pipeline idle";
                    try { _activeRunCts?.Dispose(); } catch (System.Exception __suppressedEx10) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx10.Message}"); }
                    _activeRunCts = null;
                    nextPending = DequeueNextPendingRunOnFinishLocked(isMidShutdownAbort);
                }
                BroadcastState();
                if (nextPending != null)
                {
                    DispatchDequeuedPendingRun(nextPending);
                }
            }
        }

        public (bool Started, string Message) StartHashSidecarVerifier(
            string? jobId,
            ModuleId sourceModule,
            SidecarVerifyMode verifyMode)
        {
            lock (_stateLock)
            {
                if (_isRunning)
                    return (false, "Wait for the active pipeline run to finish before starting .hash verification.");
            }

            List<string> dirs;
            if (sourceModule == ModuleId.RomLauncherGenerator)
            {
                var rom = ResolveRomConfig(jobId);
                dirs = rom.Collections
                    .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ArchivesPath) && Directory.Exists(c.ArchivesPath))
                    .Select(c => c.ArchivesPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                var man = ResolveManifestConfig(jobId);
                dirs = man.TargetFolders
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.FolderPath) && Directory.Exists(f.FolderPath))
                    .Select(f => f.FolderPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (dirs.Count == 0)
                return (false, "No enabled target directories exist on disk for .hash verification.");

            _activeExecutionTask = Task.Run(() => RunHashSidecarVerifierAsync(jobId, sourceModule, verifyMode));
            return (true, $"Started .hash Integrity Verifier ({verifyMode}) across {dirs.Count} folder(s). Watch live progress in the Terminal Drawer.");
        }

        public async Task<(bool Success, SidecarVerificationReport? Report, string Message)> RunHashSidecarVerifierAsync(
            string? jobId,
            ModuleId sourceModule,
            SidecarVerifyMode verifyMode)
        {
            lock (_stateLock)
            {
                if (_isRunning)
                    return (false, null, "Wait for the active pipeline run to finish before starting .hash verification.");
                _isRunning = true;
                _userRequestedExplicitStop = false;
                _recoverableInterruptedRun = null;
                _activeRunCts = new CancellationTokenSource();
                _activeQueueName = ".hash Integrity Verifier";
                _subprocessStatusText = $"Running .hash Verifier ({verifyMode})...";
                _activeCheckpoint = new InterruptedRunCheckpoint
                {
                    RunKind = "HashVerifier",
                    QueueName = ".hash Integrity Verifier",
                    ActiveJobId = jobId,
                    VerifierSourceModule = sourceModule,
                    VerifierMode = verifyMode,
                    PendingChainedQueueIds = ExtractPendingQueueIdsLocked(),
                    PendingRuns = _pendingRuns.Select(ClonePendingRun).ToList(),
                    LastProgressPercent = 0,
                    LastStatusText = $"Running .hash Verifier ({verifyMode})...",
                    StartedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
            }
            PersistActiveCheckpointToDisk();
            BroadcastState();

            var ct = _activeRunCts!.Token;
            List<string> dirs;
            bool expectInnerRom;
            HashSidecarMode targetPlacement;
            AuditStructureMode structureMode;
            string scratchDir;
            string sourceLabel;

            if (sourceModule == ModuleId.RomLauncherGenerator)
            {
                var rom = ResolveRomConfig(jobId);
                dirs = rom.Collections
                    .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.ArchivesPath) && Directory.Exists(c.ArchivesPath))
                    .Select(c => c.ArchivesPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                expectInnerRom = rom.DeepInspectAndHashInnerRoms;
                targetPlacement = rom.HashFilePlacement;
                structureMode = rom.StructureMode;
                scratchDir = rom.TempExtractionScratchPath;
                sourceLabel = "Module 4: ROM Launchers";
            }
            else
            {
                var man = ResolveManifestConfig(jobId);
                dirs = man.TargetFolders
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.FolderPath) && Directory.Exists(f.FolderPath))
                    .Select(f => f.FolderPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                expectInnerRom = man.DeepInspectAndHashInnerRoms;
                targetPlacement = man.PackageHashIntoArchives
                    ? HashSidecarMode.PackageInsideArchive
                    : (man.GenerateHashSidecars ? HashSidecarMode.SidecarBeside : HashSidecarMode.None);
                structureMode = man.StructureMode;
                scratchDir = Path.GetTempPath();
                sourceLabel = "Module 8: Collection Manifest";
            }

            try
            {
                if (dirs.Count == 0)
                    return (false, null, "No enabled target directories exist on disk for verification.");

                string sevenZip = _dependencyService.ResolveSevenZip();
                Log("HEADER", $"Starting .hash Integrity & Legacy Outer-Archive Verifier ({sourceLabel} - {verifyMode})");

                void VerifierLog(string tag, string message)
                {
                    Log(tag, message);
                    if (message != null && message.Contains("Progress [", StringComparison.Ordinal))
                    {
                        int openIdx = message.IndexOf("Progress [", StringComparison.Ordinal);
                        int slashIdx = openIdx >= 0 ? message.IndexOf('/', openIdx + 10) : -1;
                        int closeIdx = slashIdx > 0 ? message.IndexOf(']', slashIdx + 1) : -1;
                        double pct = 0;
                        if (openIdx >= 0 && slashIdx > openIdx && closeIdx > slashIdx &&
                            int.TryParse(message.AsSpan(openIdx + 10, slashIdx - (openIdx + 10)), out int cur) &&
                            int.TryParse(message.AsSpan(slashIdx + 1, closeIdx - (slashIdx + 1)), out int tot) &&
                            tot > 0)
                        {
                            pct = Math.Clamp((cur * 100.0) / tot, 0, 100);
                        }

                        lock (_stateLock)
                        {
                            _progressPercent = pct;
                            _subprocessStatusText = message;
                            _footerStatusText = message;
                        }
                        ThrottledProgressBroadcast(pct, message);
                    }
                }

                var report = await Task.Run(() => HashSidecarService.VerifyAndHealDirectorySidecarsAsync(
                    dirs,
                    sevenZip,
                    expectInnerRom,
                    targetPlacement,
                    structureMode,
                    verifyMode,
                    scratchDir,
                    VerifierLog,
                    ct,
                    onStatsDelta: (p, s, e) =>
                    {
                        lock (_stateLock)
                        {
                            _sessionProcessed += p;
                            _sessionSkipped += s;
                            _sessionErrors += e;
                        }
                        ThrottledStateBroadcast();
                    }), ct).ConfigureAwait(false);

                foreach (var item in report.Items)
                {
                    if (item.Status != SidecarVerificationStatus.Valid && !item.WasHealed)
                    {
                        bool isDir = Directory.Exists(item.TargetPath);
                        var sev = item.Status is SidecarVerificationStatus.CryptographicMismatch or SidecarVerificationStatus.Error
                            ? ComplianceSeverity.Error
                            : ComplianceSeverity.Warning;
                        string detail = string.IsNullOrWhiteSpace(item.DetailMessage)
                            ? $".hash verification status: {item.Status}"
                            : $"{item.Status}: {item.DetailMessage}";
                        _complianceReports.UpsertHashVerificationFinding(sourceLabel, item.TargetPath, isDir, detail, sev);
                    }
                    else if (item.WasHealed || item.Status == SidecarVerificationStatus.Valid)
                    {
                        string id = TargetComplianceEvaluation.BuildReportId(sourceLabel, item.TargetPath);
                        _complianceReports.MarkResolved(new[] { id });
                    }
                }

                string msg = $".hash Verification complete — {report.ValidCount} valid, {report.HealedCount} healed, {report.ErrorCount} errors.";
                Log("SUCCESS", msg);
                return (true, report, msg);
            }
            catch (OperationCanceledException)
            {
                Log("WARN", "[.hash Verifier] Cancelled by user.");
                return (false, null, ".hash Verification cancelled.");
            }
            catch (Exception ex)
            {
                Log("ERROR", $"[.hash Verifier] Error: {ex.Message}");
                return (false, null, ex.Message);
            }
            finally
            {
                bool isMidShutdownAbort;
                lock (_stateLock)
                {
                    isMidShutdownAbort = (ct.IsCancellationRequested || _disposed) && !_userRequestedExplicitStop;
                }
                FinalizeCheckpointAfterRun(ct.IsCancellationRequested || _disposed);
                PendingPipelineRunDto? nextPending = null;
                lock (_stateLock)
                {
                    _isRunning = false;
                    _activeQueueName = null;
                    _progressPercent = 0;
                    _subprocessStatusText = "Ready — Pipeline idle";
                    try { _activeRunCts?.Dispose(); } catch (System.Exception __suppressedEx11) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx11.Message}"); }
                    _activeRunCts = null;
                    nextPending = DequeueNextPendingRunOnFinishLocked(isMidShutdownAbort);
                }
                BroadcastState();
                if (nextPending != null)
                {
                    DispatchDequeuedPendingRun(nextPending);
                }
            }
        }

        public RomLauncherGeneratorConfig ResolveRomConfig(string? jobId = null)
        {
            var cfg = _configService.Config;
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                var job = cfg.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                if (job?.RomLauncherGenerator != null)
                    return job.RomLauncherGenerator;
            }

            var firstRomJob = cfg.DefinedJobs.FirstOrDefault(j => j.ModuleType == ModuleId.RomLauncherGenerator && j.RomLauncherGenerator != null);
            return firstRomJob?.RomLauncherGenerator ?? cfg.RomLauncherGenerator;
        }

        public CollectionManifestConfig ResolveManifestConfig(string? jobId = null)
        {
            var cfg = _configService.Config;
            if (!string.IsNullOrWhiteSpace(jobId))
            {
                var job = cfg.DefinedJobs.FirstOrDefault(j => string.Equals(j.JobId, jobId, StringComparison.OrdinalIgnoreCase));
                if (job?.CollectionManifest != null)
                    return job.CollectionManifest;
            }

            var firstManJob = cfg.DefinedJobs.FirstOrDefault(j => j.ModuleType == ModuleId.CollectionManifest && j.CollectionManifest != null);
            return firstManJob?.CollectionManifest ?? cfg.CollectionManifest;
        }

        public void EvaluateQueueSchedules()
        {
            var nowUtc = DateTime.UtcNow;
            if (_scheduler.DeferredUntilUtc.HasValue && nowUtc < _scheduler.DeferredUntilUtc.Value)
            {
                // Schedules temporarily deferred by external coordinator (e.g. Mother v1.0 disk arbitration)
                return;
            }

            bool currentlyRunning;
            lock (_stateLock)
            {
                currentlyRunning = _isRunning || _isTransitioningPendingRun;
            }

            string? guardedProc = null;
            bool isGuarded = !currentlyRunning && _scheduler.GetCachedGuardedProcessRunning(out guardedProc);

            var queuesSnapshot = _configService.Config.NamedQueues.ToList();
            foreach (var queue in queuesSnapshot)
            {
                if (!queue.EnableSchedule)
                    continue;

                if (isGuarded)
                {
                    _scheduler.ShouldTriggerQueueNow(queue, nowUtc, isDeferredByGuard: true);
                    var nextDue = _scheduler.GetNextOccurrenceUtc(queue, nowUtc);
                    if (nextDue.HasValue &&
                        nextDue.Value <= nowUtc.AddMinutes(1) &&
                        (nowUtc - _lastGuardDeferLogUtc).TotalMinutes >= 5.0)
                    {
                        _lastGuardDeferLogUtc = nowUtc;
                        Log("WARN", $"[Scheduler] Queue '{queue.Name}' due, but guarded process '{guardedProc}' is running. Deferring run until cleared.");
                    }
                    continue;
                }

                if (_scheduler.ShouldTriggerQueueNow(queue, nowUtc, isDeferredByGuard: false))
                {
                    var triggeredAtUtc = DateTime.UtcNow;
                    queue.LastRunUtc = triggeredAtUtc;
                    _configService.MutateAndSave(cfg =>
                    {
                        var targetQ = cfg.NamedQueues.FirstOrDefault(q => string.Equals(q.Id, queue.Id, StringComparison.OrdinalIgnoreCase));
                        if (targetQ != null)
                        {
                            targetQ.LastRunUtc = triggeredAtUtc;
                        }
                    });

                    lock (_stateLock)
                    {
                        currentlyRunning = _isRunning || _isTransitioningPendingRun;
                    }

                    if (currentlyRunning)
                    {
                        bool enqueuedNew;
                        int queuedPos;
                        string rejectReason;
                        lock (_stateLock)
                        {
                            enqueuedNew = TryEnqueuePendingRunLocked(
                                new PendingPipelineRunDto
                                {
                                    ItemKey = BuildPendingItemKey("Queue", queue.Id),
                                    RunKind = "Queue",
                                    TargetId = queue.Id,
                                    DisplayName = queue.Name,
                                    IsDryRun = ForceDryRun,
                                    ForceCloudResync = false,
                                    TriggerSource = $"Schedule_{queue.Name}",
                                    EnqueuedAtUtc = triggeredAtUtc
                                },
                                isAutoTrigger: true,
                                out queuedPos,
                                out rejectReason);
                        }

                        if (enqueuedNew)
                        {
                            PersistActiveCheckpointToDisk();
                            Log("INFO", $"[Scheduler] Queue '{queue.Name}' scheduled run queued once in FIFO order (#{queuedPos} in Up Next, waiting for active run to finish).");
                            BroadcastState();
                        }
                        else
                        {
                            Log("WARN", $"[Scheduler] Queue '{queue.Name}' scheduled trigger skipped: {rejectReason}");
                        }
                    }
                    else
                    {
                        Log("HEADER", $"=== [Scheduler] Launching Scheduled Queue: '{queue.Name}' ===");
                        StartQueueRun(queue.Id, isDryRun: false, triggerSource: $"Schedule_{queue.Name}");
                        return;
                    }
                }
            }

            if (!currentlyRunning && !isGuarded)
            {
                PendingPipelineRunDto? nextPending = null;
                lock (_stateLock)
                {
                    if (!_isRunning && !_isTransitioningPendingRun && _pendingRuns.Count > 0)
                    {
                        nextPending = ClonePendingRun(_pendingRuns[0]);
                        _pendingRuns.RemoveAt(0);
                        _isTransitioningPendingRun = true;
                        _transitioningItemKey = nextPending.ItemKey;
                    }
                }

                if (nextPending != null)
                {
                    DispatchDequeuedPendingRun(nextPending);
                }
            }
        }

        private async Task WaitIfQueuePausedAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                bool paused;
                lock (_stateLock) { paused = _isPaused; }
                if (!paused) break;
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }

        private void HandleNetworkWaitStateChanged(bool isWaiting, string message)
        {
            lock (_stateLock)
            {
                _isWaitingForNetwork = isWaiting;
                _networkWaitMessage = isWaiting ? message : "";
            }
            if (isWaiting && _configService.Config.NotifyOnNetworkDrop)
            {
                OnTrayNotificationRequested?.Invoke("Housekeeping — Network Drop Detected", message, true);
            }
            BroadcastState();
        }

        private void HandleSubprocessProgress(ProcessProgressInfo info)
        {
            double pct = Math.Clamp(info.Percent, 0, 100);
            string display;
            string? activeJob;
            string? activeQId;
            string? activeQName;
            int idx;
            int total;

            lock (_stateLock)
            {
                _progressPercent = pct;
                string prefix = !string.IsNullOrEmpty(_activeModuleBadge) ? $"[{_activeModuleBadge}] " : "";
                display = $"{prefix}{info.StatusText}";
                _subprocessStatusText = display;
                activeJob = _activeJobName;
                activeQId = _activeQueueId;
                activeQName = _activeQueueName;
                idx = _currentJobIndex;
                total = _totalJobs;

                if (_activeJobId != null && _jobStates.TryGetValue(_activeJobId, out var js))
                {
                    js.ProgressPercent = pct;
                    js.DetailText = info.StatusText;
                }
            }

            _telemetryService.SetExecutionState(true, activeJob, info.StatusText, pct, activeQId, activeQName, idx, total);
            ThrottledProgressBroadcast(pct, display);
        }

        private void ThrottledProgressBroadcast(double pct, string statusText)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckpointProgressFlushUtc).TotalSeconds >= 8.0)
            {
                bool shouldPersist = false;
                lock (_stateLock)
                {
                    if (_activeCheckpoint != null)
                    {
                        _lastCheckpointProgressFlushUtc = now;
                        _activeCheckpoint.LastProgressPercent = pct;
                        _activeCheckpoint.LastStatusText = statusText;
                        shouldPersist = true;
                    }
                }
                if (shouldPersist)
                {
                    PersistActiveCheckpointToDisk();
                }
            }

            if ((now - _lastProgressBroadcastUtc).TotalMilliseconds < 90 && pct < 100.0)
                return;

            _lastProgressBroadcastUtc = now;
            try
            {
                OnProgressChanged?.Invoke(new ProcessProgressInfo
                {
                    Percent = pct,
                    StatusText = statusText
                });
            }
            catch (System.Exception __suppressedEx12) { System.Diagnostics.Debug.WriteLine($"[PipelineOrchestratorService] Non-fatal error ignored: {__suppressedEx12.Message}"); }
        }
    }
}
