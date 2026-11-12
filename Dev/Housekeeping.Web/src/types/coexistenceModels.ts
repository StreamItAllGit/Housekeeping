// @module: Housekeeping.Web/types/coexistenceModels
// @purpose: Canonical Standard 18 camelCase wire DTOs for 4-pillar sister-app coexistence, UI layout persistence, and System Doctor.
// @depends-on: (none — pure TypeScript interfaces)
// @exports: UiLayoutConfig, TerminalSizePreset, CoexistencePresetId, CoexistenceManagedRootDto, CoexistencePeerSummaryDto, CoexistenceConflictDto, CoexistenceStatusDto, ApplyCoexistencePresetRequest
export type TerminalSizePreset = 'small' | 'medium' | 'large' | 'custom';

export type CoexistencePresetId =
  | 'delegate_to_shadowlibrary'
  | 'co_audit_zero_rewrite'
  | 'align_cloud_sync'
  | 'align_ustation_saves_and_launchers';

export interface UiLayoutConfig {
  TerminalHeightPx: number;
  TerminalSizePreset: TerminalSizePreset;
  TerminalOpen: boolean;
  TerminalWordWrap: boolean;
  SidebarCollapsed: boolean;
}

export interface CoexistenceManagedRootDto {
  path: string;
  normalizedPath: string;
  jobId: string;
  jobName: string;
  moduleType: string;
  writesHashSidecars: boolean;
  hashFilePlacement: string;
  deepHashInnerRoms: boolean;
  writesCollectionManifestCsv: boolean;
  delegatesRomVaultsToShadowLibrary: boolean;
  syncPostInjectionContainerHashJson: boolean;
  cleanDisallowedHashFilesOnMigrate: boolean;
}

export interface CoexistencePeerSummaryDto {
  appId: string;
  name: string;
  port: number;
  isOnline: boolean;
  version: string;
  sharedRoots: string[];
}

export interface CoexistenceConflictDto {
  conflictId: string;
  sisterAppId: string;
  sisterAppName: string;
  sharedPath: string;
  jobId: string;
  jobName: string;
  moduleType: string;
  severity: 'warning' | 'info' | 'error' | string;
  summary: string;
  recommendedPresets: string[];
}

export interface CoexistenceStatusDto {
  appId: string;
  suiteId: string;
  schemaVersion: number;
  version: string;
  port: number;
  updatedAtUtc: string;
  shouldAutoAlignSisterApps: boolean;
  hasActiveConflicts: boolean;
  activeHkLocksCount: number;
  coexistenceMode: string;
  managedRoots: CoexistenceManagedRootDto[];
  peers: CoexistencePeerSummaryDto[];
  detectedConflicts: CoexistenceConflictDto[];
}

export interface ApplyCoexistencePresetRequest {
  jobId?: string | null;
  preset: string;
  enableAutoAlign?: boolean;
}

export interface GreenroomStatusDto {
  Enabled: boolean;
  CompanionOnline: boolean;
  CompanionBaseUrl: string;
  BrowserCompanionUrl: string;
  CliFallbackAvailable: boolean;
  CliScriptPath: string;
  StatusMessage: string;
  LastAutoSyncUtc?: string | null;
  LastAutoSyncSummary: string;
}

export interface TailscalePwaStatusDto {
  TailscaleAvailable: boolean;
  BackendState: string;
  DnsName: string;
  TailscaleIp: string;
  HttpsCertsEnabled: boolean;
  ServeConfigured: boolean;
  HttpsPwaUrl: string;
  PendingAuthUrl: string;
  ServeSummary: string;
  StatusMessage: string;
}

export interface FsDriveItem {
  name: string;
  driveLetter: string;
  driveType: string;
  isReady: boolean;
  volumeLabel: string;
  freeGb: number;
  totalGb: number;
}

export interface FsBrowseResponse {
  currentPath: string;
  parentPath?: string | null;
  directories: { name: string; fullPath: string; lastModifiedUtc: string }[];
  files: { name: string; fullPath: string; sizeBytes: number; extension: string }[];
}

export interface DependencyStatusItem {
  ToolName: string;
  Description: string;
  IsAvailable: boolean;
  SourceLabel: string;
  ResolvedPath: string;
}

export interface RegexPlaygroundResultDto {
  SampleInput: string;
  FileName: string;
  RelativePath: string;
  FullPath: string;
  ExtractedFields: Record<string, string>;
  ValidationErrors: string[];
}

export interface RunHistoryEntry {
  StartedAt: string;
  CompletedAt: string;
  TriggerSource: string;
  ModulesSummary: string;
  ItemsProcessed: number;
  ItemsSkipped: number;
  ErrorsCount: number;
  Status: string;
  LogFilePath: string;
}
