// @module: Housekeeping.Core/Services/PipelineOrchestratorService.CancellationAndShutdown
// @purpose: Three-tier cancellation (StopCurrentJob, StopActiveRun, InitiateGracefulShutdownAbort) and interrupted-run checkpoint lifecycle.
// @depends-on: System.Diagnostics, System.IO, System.Linq, System.Text, System.Text.Json, System.Threading
// @exports: PipelineOrchestratorService (partial)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Housekeeping.Services
{
    public sealed partial class PipelineOrchestratorService
    {
        private CancellationTokenSource? _activeJobCts;

        public bool StopCurrentJob()
        {
            CancellationTokenSource? jobCts;
            CancellationTokenSource? runCts;
            string activeName;
            lock (_stateLock)
            {
                if (!_isRunning)
                    return false;
                activeName = _activeJobName ?? _activeQueueName ?? "active job";
                jobCts = _activeJobCts;
                runCts = _activeRunCts;
                _isPaused = false;
            }

            Log("WARN", $"Stop Job requested — cancelling active job '{activeName}' and advancing queue...");
            _processRunner.ResumeActiveProcessIfSuspended();
            if (jobCts != null)
            {
                try { jobCts.Cancel(); }
                catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] StopCurrentJob cancel ignored: {ex.Message}"); }
            }
            else if (runCts != null)
            {
                try { runCts.Cancel(); }
                catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] StopCurrentJob fallback cancel ignored: {ex.Message}"); }
            }

            _processRunner.KillActiveProcess();
            BroadcastState();
            return true;
        }

        public void StopEntireQueue() => StopActiveRun();

        public void StopActiveRun()
        {
            CancellationTokenSource? cts;
            CancellationTokenSource? jobCts;
            int clearedPending;
            lock (_stateLock)
            {
                _userRequestedExplicitStop = true;
                _isPaused = false;
                clearedPending = _pendingRuns.Count;
                _pendingRuns.Clear();
                cts = _activeRunCts;
                jobCts = _activeJobCts;
            }

            ClearInterruptedCheckpoint();
            _processRunner.ResumeActiveProcessIfSuspended();

            if (cts != null)
            {
                string suffix = clearedPending > 0 ? $" (also cleared {clearedPending} pending Up Next run(s))" : "";
                Log("WARN", $"Stop requested — signalling active subprocesses and pipeline to halt{suffix}...");
                try { jobCts?.Cancel(); } catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] jobCts cancel ignored: {ex.Message}"); }
                try { cts.Cancel(); } catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] runCts cancel ignored: {ex.Message}"); }
                _processRunner.KillActiveProcess();
                BroadcastState();
            }
            else if (clearedPending > 0)
            {
                Log("WARN", $"Stop requested — cleared {clearedPending} pending Up Next run(s).");
                BroadcastState();
            }
        }

        public void InitiateGracefulShutdownAbort(string reason = "Graceful shutdown")
        {
            CancellationTokenSource? cts;
            CancellationTokenSource? jobCts;
            bool hasActiveRun;
            bool hasCheckpoint;
            lock (_stateLock)
            {
                _userRequestedExplicitStop = false;
                _isPaused = false;
                hasActiveRun = _isRunning;
                hasCheckpoint = _activeCheckpoint != null;
                cts = _activeRunCts;
                jobCts = _activeJobCts;
            }

            if (hasCheckpoint)
            {
                PersistActiveCheckpointToDisk();
            }

            _processRunner.ResumeActiveProcessIfSuspended();

            if (hasActiveRun || cts != null)
            {
                Log("WARN", $"[{reason}] Preserving interrupted run checkpoint & Up Next queue, and halting active worker...");
                try { jobCts?.Cancel(); } catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] Shutdown jobCts cancel ignored: {ex.Message}"); }
                try { cts?.Cancel(); } catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] Shutdown runCts cancel ignored: {ex.Message}"); }
                _processRunner.KillActiveProcess();
            }
        }

        public bool TogglePause()
        {
            bool paused;
            lock (_stateLock)
            {
                if (!_isRunning) return false;
                _isPaused = !_isPaused;
                paused = _isPaused;
            }
            Log(paused ? "WARN" : "INFO", paused ? "[Queue Paused] Current step will pause cleanly at the next checkpoint." : "[Queue Resumed] Execution continuing.");
            BroadcastState();
            return paused;
        }

        public bool ToggleSuspendProcess()
        {
            _processRunner.ToggleSuspendActiveProcess();
            BroadcastState();
            return _processRunner.IsSuspended;
        }

        private static InterruptedRunCheckpoint? CloneCheckpoint(InterruptedRunCheckpoint? src)
        {
            if (src == null) return null;
            return new InterruptedRunCheckpoint
            {
                RunKind = src.RunKind,
                QueueId = src.QueueId,
                QueueName = src.QueueName,
                InitialJobIds = new List<string>(src.InitialJobIds),
                CompletedJobIds = new List<string>(src.CompletedJobIds),
                ActiveJobId = src.ActiveJobId,
                ActiveJobName = src.ActiveJobName,
                PendingChainedQueueIds = new List<string>(src.PendingChainedQueueIds),
                PendingRuns = (src.PendingRuns ?? new List<PendingPipelineRunDto>()).Select(ClonePendingRun).ToList(),
                IsDryRun = src.IsDryRun,
                ForceCloudResync = src.ForceCloudResync,
                VerifierSourceModule = src.VerifierSourceModule,
                VerifierMode = src.VerifierMode,
                LastProgressPercent = src.LastProgressPercent,
                LastStatusText = src.LastStatusText,
                StartedAtUtc = src.StartedAtUtc,
                UpdatedAtUtc = src.UpdatedAtUtc
            };
        }

        private void LoadInterruptedCheckpointOnStartup()
        {
            try
            {
                string path = InterruptedCheckpointFilePath;
                if (!File.Exists(path)) return;
                var loaded = JsonSerializer.Deserialize<InterruptedRunCheckpoint>(File.ReadAllText(path, Encoding.UTF8), ConfigService.JsonOptions);
                if (loaded != null && !string.IsNullOrWhiteSpace(loaded.RunKind))
                {
                    lock (_stateLock) { _recoverableInterruptedRun = loaded; }
                    Log("WARN", $"[Recovery] Detected interrupted mid-shutdown run: '{loaded.QueueName ?? loaded.ActiveJobName ?? loaded.RunKind}' ({loaded.LastProgressPercent:F0}% — {loaded.LastStatusText}). Ready to resume.");
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] LoadInterruptedCheckpointOnStartup ignored: {ex.Message}"); }
        }

        private void PersistActiveCheckpointToDisk()
        {
            InterruptedRunCheckpoint? copy;
            lock (_stateLock)
            {
                if (_activeCheckpoint == null) return;
                _activeCheckpoint.UpdatedAtUtc = DateTime.UtcNow;
                _activeCheckpoint.PendingRuns = _pendingRuns.Select(ClonePendingRun).ToList();
                _activeCheckpoint.PendingChainedQueueIds = ExtractPendingQueueIdsLocked();
                copy = CloneCheckpoint(_activeCheckpoint);
            }

            if (copy == null) return;
            try
            {
                ConfigService.AtomicWriteAllText(InterruptedCheckpointFilePath, JsonSerializer.Serialize(copy, ConfigService.JsonOptions));
            }
            catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] PersistActiveCheckpointToDisk ignored: {ex.Message}"); }
        }

        private void ClearInterruptedCheckpoint()
        {
            lock (_stateLock)
            {
                _activeCheckpoint = null;
                _recoverableInterruptedRun = null;
            }
            try
            {
                string path = InterruptedCheckpointFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] ClearInterruptedCheckpoint ignored: {ex.Message}"); }
        }

        public void DismissInterruptedRun()
        {
            ClearInterruptedCheckpoint();
            Log("INFO", "[Recovery] Cleared interrupted run checkpoint.");
            BroadcastState();
        }

        private void FinalizeCheckpointAfterRun(bool cancelledOrDisposed)
        {
            bool preserve;
            lock (_stateLock)
            {
                preserve = cancelledOrDisposed && !_userRequestedExplicitStop && _activeCheckpoint != null;
            }

            if (preserve)
            {
                PersistActiveCheckpointToDisk();
                lock (_stateLock)
                {
                    _recoverableInterruptedRun = CloneCheckpoint(_activeCheckpoint);
                    _activeCheckpoint = null;
                }
            }
            else
            {
                ClearInterruptedCheckpoint();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            InitiateGracefulShutdownAbort("Service Dispose");
            try { _scheduleTimer?.Dispose(); } catch (Exception ex) { Debug.WriteLine($"[PipelineOrchestratorService] Timer dispose ignored: {ex.Message}"); }
        }
    }
}
