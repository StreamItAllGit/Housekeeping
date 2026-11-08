import * as signalR from '@microsoft/signalr';
import type {
  PipelineRuntimeStateDto,
  TelemetrySnapshotDto,
  TerminalLogEntry,
} from '../types/models';

export interface SignalRCallbacks {
  onTerminalBatch: (entries: TerminalLogEntry[]) => void;
  onTerminalCleared: () => void;
  onPipelineState: (state: PipelineRuntimeStateDto) => void;
  onSubprocessProgress: (progress: { Percent: number; StatusText: string }) => void;
  onTelemetrySnapshot: (snap: TelemetrySnapshotDto) => void;
  onComplianceSummary: (summary: { OpenCount: number; TotalCount: number }) => void;
  onConnectionChanged: (connected: boolean) => void;
}

export class HousekeepingSignalRClient {
  private connection: signalR.HubConnection | null = null;
  private targetNodeId = 'local';

  async connect(targetNodeId: string, callbacks: SignalRCallbacks): Promise<void> {
    await this.disconnect();
    this.targetNodeId = targetNodeId || 'local';

    const url =
      this.targetNodeId !== 'local'
        ? `/hubs/housekeeping?targetNodeId=${encodeURIComponent(this.targetNodeId)}`
        : '/hubs/housekeeping';

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(url, {
        withCredentials: true,
      })
      .withAutomaticReconnect([500, 1500, 3000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    conn.on('TerminalLogBatch', (batch: TerminalLogEntry[]) => {
      if (Array.isArray(batch) && batch.length > 0) {
        callbacks.onTerminalBatch(batch);
      }
    });

    conn.on('TerminalCleared', () => {
      callbacks.onTerminalCleared();
    });

    conn.on('PipelineState', (state: PipelineRuntimeStateDto) => {
      if (state) callbacks.onPipelineState(state);
    });

    conn.on('SubprocessProgress', (progress: { Percent: number; StatusText: string }) => {
      if (progress) callbacks.onSubprocessProgress(progress);
    });

    conn.on('TelemetrySnapshot', (snap: TelemetrySnapshotDto) => {
      if (snap) callbacks.onTelemetrySnapshot(snap);
    });

    conn.on('ComplianceSummary', (summary: { OpenCount: number; TotalCount: number }) => {
      if (summary) callbacks.onComplianceSummary(summary);
    });

    conn.onreconnecting(() => callbacks.onConnectionChanged(false));
    conn.onreconnected(() => callbacks.onConnectionChanged(true));
    conn.onclose(() => callbacks.onConnectionChanged(false));

    this.connection = conn;
    try {
      await conn.start();
      callbacks.onConnectionChanged(true);
    } catch {
      callbacks.onConnectionChanged(false);
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      const prev = this.connection;
      this.connection = null;
      try {
        await prev.stop();
      } catch {
        // ignore stop errors
      }
    }
  }
}
