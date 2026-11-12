export type ModuleId =
  | 'ProxyGenerator'
  | 'ArchiveCompressor'
  | 'MediaTranscoder'
  | 'RomLauncherGenerator'
  | 'CloudSync'
  | 'HumbleSync'
  | 'CustomScripts'
  | 'CollectionManifest';

export type SourceDetectionMode = 'FilenameTagMatching' | 'MediaInfoResolutionScan';
export type VerbosityLevel = 'AlertsOnly' | 'Quiet' | 'ProgressAndStatus' | 'Everything' | 'Custom';
export type FooterVerbosityLevel = 'AlertsOnly' | 'Quiet' | 'ProgressAndStatus' | 'Everything' | 'Custom';
export type HashSidecarMode =
  | 'DatabaseOnly'
  | 'None'
  | 'SidecarBeside'
  | 'PackageInsideArchive'
  | 'SidecarAndArchive';
export type AuditStructureMode = 'Auto' | 'FilesOnly' | 'FolderDumpsOnly';
export type MasterScheduleMode = 'DailyTime' | 'IntervalCadence' | 'SpecificTimes';
export type RemoteConnectionType = 'WindowsUncShare' | 'OpenSsh' | 'WinRm';
export type TerminalSplitMode = 'Single' | 'SplitHorizontal' | 'SplitVertical';
export type ManifestInspectTarget =
  | 'FileName'
  | 'FileNameWithoutExtension'
  | 'ParentFolderName'
  | 'RelativePath'
  | 'FullPath';
export type ManifestMatchMode = 'RegexNamedCapture' | 'RegexPattern' | 'TextContains' | 'SplitToken';
export type SidecarVerifyMode = 'FastHeaderAndHeal' | 'DeepCryptographicAndHeal' | 'DryRunReportOnly';

export interface CustomVerbosityConfig {
  TerminalTags: string[];
  FooterTags: string[];
}

export interface ManifestDropdownCatalogConfig {
  LocationIds: string[];
  Labels: string[];
  Platforms: string[];
  ExtensionsFilters: string[];
}

export interface WebServerConfig {
  Port: number;
  BindLanAndTailnet: boolean;
  AdminPasswordHash: string;
  AdminPasswordSalt: string;
  NodeApiToken: string;
  TrustTailscaleServeHeaders: boolean;
  RestrictRawShellToLocalhost: boolean;
  DefaultDesktopLaunchMode: 'Browser' | 'StandaloneWindow' | string;
  ThemePreference: 'Dark' | 'Light' | 'System' | string;
  UiDensity: 'Compact' | 'Comfortable' | 'Touch' | string;
}

export interface GreenroomIntegrationConfig {
  Enabled: boolean;
  CompanionBaseUrl: string;
  CompanionPort: number;
  CompanionBearerToken: string;
  GreenroomRootPath: string;
  AutoSyncAfterRomManifestChanges: boolean;
  AutoSyncAfterCollectionManifestChanges: boolean;
}

export interface ToolPathsConfig {
  HandBrakeCliPath: string;
  SevenZipPath: string;
  RclonePath: string;
  HumbleDownloaderPath: string;
}

export interface HandBrakeEncodingProfile {
  ProfileId: string;
  Name: string;
  ContainerExtension: string;
  VideoEncoder: string;
  EncoderPreset: string;
  QualityRf: number;
  MaxWidth: number;
  MaxHeight: number;
  AudioEncoder: string;
  AudioBitrateKbps: number;
  AudioLanguageCode: string;
  SubtitleMode: string;
  UseGuiPresetOverride: boolean;
  GuiPresetName: string;
  GuiPresetFilePath: string;
  ExtraCliFlags: string;
}

export interface FolderTargetItem {
  Enabled: boolean;
  Path: string;
}

export interface ProxyGeneratorConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  HandBrakeProfileId: string;
  ProxySuffixTag: string;
  Extensions: string[];
  Folders: FolderTargetItem[];
}

export interface ArchiveMappingItem {
  Enabled: boolean;
  Name: string;
  SourcePath: string;
  ArchiveOutputPath: string;
}

export interface ArchiveCompressorConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  HandBrakeProfileId: string;
  OutputExtension: string;
  SkipFilesContainingTag: string;
  InputExtensions: string[];
  Mappings: ArchiveMappingItem[];
}

export interface SourceQualityTierRule {
  Enabled: boolean;
  TierName: string;
  SourceTagsCsv: string;
  MaxSourceHeightPx: number;
  Profile144p: string;
  Profile240p: string;
  Profile360p: string;
  Profile480p: string;
  Profile720p: string;
  Profile1080p: string;
  Profile4K: string;
}

export interface LanguageEditionRule {
  Enabled: boolean;
  LanguageCode: string;
  LanguageFolderPrefix: string;
  TargetResolutionTag: string;
  HandBrakeProfileId: string;
}

export interface MediaTranscoderConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  DetectionMode: SourceDetectionMode;
  MasterFileTag: string;
  RemoteModeSkipMediaInfo: boolean;
  ReEncodeNonHevcOutputs: boolean;
  DriveSubfolderRelativePath: string;
  DrivesToScan: string[];
  CustomFoldersToScan: FolderTargetItem[];
  Extensions: string[];
  SourceTiers: SourceQualityTierRule[];
  LanguageEditionRules: LanguageEditionRule[];
  Enable144p: boolean;
  Enable240p: boolean;
  Enable360p: boolean;
  Enable480p: boolean;
  Enable720p: boolean;
  Enable1080p: boolean;
  Enable4K: boolean;
}

export interface GameSyncPathItem {
  Enabled: boolean;
  Label: string;
  LocalPath: string;
  RemoteTarget: string;
  SyncMode: string;
}

export interface ConsoleCollectionRule {
  Enabled: boolean;
  ConsoleName: string;
  ArchivesPath: string;
  EmulatorRunnerType: string;
  EmulatorExecutablePath: string;
  CoreOrCustomArgs: string;
  ExtensionsCsv: string;
  SubFolder: string;
  OverrideJobSyncPaths: boolean;
  ComplianceTemplateId: string;
  CustomSyncPaths: GameSyncPathItem[];
}

export interface ArchiveComplianceTemplate {
  TemplateId: string;
  DisplayName: string;
  Category: 'Community' | 'OfficialArchival' | 'Custom';
  IsBuiltIn: boolean;
  Description: string;
  UseConsoleRuleExtensionsAsPayloads: boolean;
  AllowedPayloadExtensionsCsv: string;
  AllowedCompanionExtensionsCsv: string;
  AllowedOuterContainerExtensionsCsv: string;
  RequireArchivedContainer: boolean;
  AllowUnpackedFolderTargets: boolean;
  AllowSingleWrapperFolder: boolean;
  MaxPrimaryRomsPerArchive: number;
  RequireCueOrGdiForRawBinTracks: boolean;
  ValidateCueGdiM3uReferences: boolean;
  FlagOrphanTrackBins: boolean;
  ValidateMultiDiscSequenceContinuity: boolean;
  RequireM3uForMultiDiscSet: boolean;
  FlagNestedArchives: boolean;
  ValidateFolderDumpBootAnchors: boolean;
  FlagPs3UpdatePupBloat: boolean;
}

export interface RomLauncherGeneratorConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  AutoGenerateM3uForMultiDisc: boolean;
  AutoCompressUnarchivedRomsWith7z: boolean;
  DeleteUncompressedOriginalsAfter7z: boolean;
  UseFast7zListInspection: boolean;
  ExtractArchiveOnLaunch: boolean;
  CreateStartMenuShortcuts: boolean;
  CreateLocalLauncherNextToArchive: boolean;
  RefreshExistingLaunchers: boolean;
  LaunchersOutputRootPath: string;
  TempExtractionScratchPath: string;
  StartMenuShortcutsPath: string;
  EnablePrePostLaunchSyncCommands: boolean;
  UseCustomPrePostLaunchCommands: boolean;
  SaveSyncLocalFolder: string;
  SaveSyncCloudRemote: string;
  SaveSyncMode: string;
  PreLaunchCommand: string;
  PostLaunchCommand: string;
  DefaultSyncPaths: GameSyncPathItem[];
  EnableCollectionManifestCsv: boolean;
  ManifestCsvFileName: string;
  DatFilesDirectory: string;
  AutoDownloadMissingDats: boolean;
  ForceRehashExistingManifestEntries: boolean;
  EnableSecondaryManifestStorage: boolean;
  SecondaryManifestDirectory: string;
  HashFilePlacement: HashSidecarMode;
  DeepInspectAndHashInnerRoms: boolean;
  TrustExistingHashFiles: boolean;
  UseFastArchiveHeaderCrc32Audit: boolean;
  StructureMode: AuditStructureMode;
  EnableArchiveTemplateComplianceScan: boolean;
  AutoSyncGreenroomOnManifestChange: boolean;
  ArchiveTemplates: ArchiveComplianceTemplate[];
  Collections: ConsoleCollectionRule[];
}

export interface CloudSyncJobItem {
  Enabled: boolean;
  JobName: string;
  SyncMode: string;
  LocalPath: string;
  RemoteTarget: string;
  Flags: string;
}

export interface CloudSyncConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  Jobs: CloudSyncJobItem[];
}

export interface HumbleSyncConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  SessionCookie: string;
  LibraryPath: string;
  AdditionalArgs: string;
}

export interface CustomTaskItem {
  Enabled: boolean;
  Name: string;
  CommandOrScriptPath: string;
  Arguments: string;
  WorkingDirectory: string;
  RequiredPathCheck: string;
  RunInExternalCmd: boolean;
}

export interface CustomScriptsConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  Tasks: CustomTaskItem[];
}

export interface ManifestFieldParseRule {
  Id: string;
  Enabled: boolean;
  ColumnName: string;
  FileExtensionFilter: string;
  InspectTarget: ManifestInspectTarget;
  MatchMode: ManifestMatchMode;
  Pattern: string;
  AssignedValue: string;
  SplitDelimiter: string;
  SplitIndex: number;
}

export interface ManifestTargetFolderRule {
  Enabled: boolean;
  LocationId: string;
  Label: string;
  FolderPath: string;
  IncludeSubdirectories: boolean;
  MaxSubdirectoryDepth: number;
  SystemSelection: string;
  StructureMode: AuditStructureMode;
  TreatSubfoldersAsGameUnits: boolean;
  ExtensionsFilter: string;
  ExcludedFolderNames: string;
  MinimumFileSizeBytes: number;
}

export interface CollectionManifestConfig {
  Enabled: boolean;
  RunInExternalCmd: boolean;
  TargetFolders: ManifestTargetFolderRule[];
  FieldParseRules: ManifestFieldParseRule[];
  UnifiedMasterManifest: boolean;
  UnifiedManifestName: string;
  UnifiedManifestOutputFolder: string;
  EnableBitRotDetection: boolean;
  CreateMissingHashFiles: boolean;
  EnableBitRotAutoHeal: boolean;
  CheckInnerArchiveAndContainer: boolean;
  EnableFileHashing: boolean;
  HashAlgorithm: string;
  SkipExistingVerifiedHashes: boolean;
  CsvDelimiter: string;
  ExcludedExtensions: string;
  GenerateHashSidecars: boolean;
  PackageHashIntoArchives: boolean;
  HashFilePlacement: HashSidecarMode;
  DeepInspectAndHashInnerRoms: boolean;
  TrustExistingHashFiles: boolean;
  SkipShadowLibraryManagedRoots?: boolean;
  SyncPostInjectionContainerHashJson?: boolean;
  CleanDisallowedHashFilesOnMigrate?: boolean;
  StructureMode: AuditStructureMode;
  LogCadenceItems: number;
  LogCadenceSeconds: number;
  LargeFileNoticeThresholdBytes: number;
  ExportCsv: boolean;
  ExportJson: boolean;
  ExportHtmlReport: boolean;
  SecondaryMirrorFolder: string;
  AutoSyncGreenroomOnManifestChange: boolean;
}

export interface HousekeepingJob {
  JobId: string;
  Name: string;
  ModuleType: ModuleId;
  Enabled: boolean;
  RunInExternalCmd: boolean;
  OnSuccessTriggerJobId: string;
  ProxyGenerator?: ProxyGeneratorConfig | null;
  ArchiveCompressor?: ArchiveCompressorConfig | null;
  MediaTranscoder?: MediaTranscoderConfig | null;
  RomLauncherGenerator?: RomLauncherGeneratorConfig | null;
  CloudSync?: CloudSyncConfig | null;
  HumbleSync?: HumbleSyncConfig | null;
  CustomScripts?: CustomScriptsConfig | null;
  CollectionManifest?: CollectionManifestConfig | null;
}

export interface NamedJobQueue {
  Id: string;
  Name: string;
  Description: string;
  EnableSchedule: boolean;
  ScheduleMode: MasterScheduleMode;
  ScheduleDailyTime: string;
  ScheduleIntervalMinutes: number;
  ScheduleSpecificTimesCsv: string;
  ScheduleDays: string[];
  LastRunUtc?: string | null;
  Jobs: HousekeepingJob[];
}

export interface RemoteNodeConfig {
  Id: string;
  Name: string;
  HostOrIp: string;
  RemoteAppPath: string;
  ConnectionType: RemoteConnectionType;
  Port: number;
  Username: string;
  Password?: string;
  PrivateKeyPath?: string;
  HttpApiPort: number;
  ApiToken: string;
  PreferHttpApi: boolean;
  hasApiToken?: boolean;
  DetectedVersion?: string;
  IsReachable?: boolean;
  CanUpdateApp?: boolean;
  CanUpdateSettings?: boolean;
  StatusMessage?: string;
}

export interface AppConfig {
  ConfigVersion: number;
  DefaultUseBuiltInTerminal: boolean;
  EnableNetworkDropSafety: boolean;
  NetworkRetryIntervalSeconds: number;
  NetworkMaxRetries: number;
  EnableAtomicPartialWrites: boolean;
  CleanupPartialsOnStartup: boolean;
  EnableDistributedNetworkLocking: boolean;
  DistributedLockTimeoutMinutes: number;
  MinimizeToTrayOnClose: boolean;
  StartMinimizedToTray: boolean;
  StartWithWindows: boolean;
  ToolPaths: ToolPathsConfig;
  HandBrakeProfiles: HandBrakeEncodingProfile[];
  EnableMasterSchedule: boolean;
  MasterScheduleMode: MasterScheduleMode;
  MasterScheduleTime: string;
  MasterScheduleIntervalMinutes: number;
  MasterScheduleSpecificTimesCsv: string;
  MasterScheduleDays: string[];
  EnableProcessGuard: boolean;
  GuardedProcessNames: string[];
  EnableToastNotifications: boolean;
  NotifyOnNetworkDrop: boolean;
  EnableDiscordWebhook: boolean;
  DiscordWebhookUrl: string;
  DiscordNotifyQueueStart: boolean;
  DiscordNotifyQueueFinish: boolean;
  DiscordNotifyJobStart: boolean;
  DiscordNotifyJobFinish: boolean;
  DiscordNotifyTaskStart: boolean;
  DiscordNotifyErrors: boolean;
  DiscordCustomMachineName: string;
  TerminalMaxLines: number;
  EnableWatchdog: boolean;
  WatchdogIntervalMinutes: number;
  AutoRestartInterruptedRunsOnStartup?: boolean;
  AutoAlignWithSisterApps?: boolean;
  UiLayout?: import('./coexistenceModels').UiLayoutConfig;
  JobBuilderSortMode: string;
  JobBuilderFilterModule: string;
  JobBuilderViewMode: string;
  TerminalVerbosity: VerbosityLevel;
  FooterVerbosity: FooterVerbosityLevel;
  CustomVerbosity: CustomVerbosityConfig;
  ManifestDropdownCatalog: ManifestDropdownCatalogConfig;
  RemoteNodes: RemoteNodeConfig[];
  SplitMode: TerminalSplitMode;
  ActiveNodeIdPane1: string;
  ActiveNodeIdPane2: string;
  PostRunPowerAction: string;
  WebServer: WebServerConfig;
  GreenroomIntegration: GreenroomIntegrationConfig;
  NamedQueues: NamedJobQueue[];
  ActiveQueueId: string;
  DefinedJobs: HousekeepingJob[];
  ProxyGenerator: ProxyGeneratorConfig;
  ArchiveCompressor: ArchiveCompressorConfig;
  MediaTranscoder: MediaTranscoderConfig;
  RomLauncherGenerator: RomLauncherGeneratorConfig;
  CloudSync: CloudSyncConfig;
  HumbleSync: HumbleSyncConfig;
  CustomScripts: CustomScriptsConfig;
  CollectionManifest: CollectionManifestConfig;
}

export interface AuthStatusResponse {
  isAuthenticated: boolean;
  isDirectLoopback: boolean;
  isTailscaleServe: boolean;
  requiresInitialPasswordSetup: boolean;
  authMethod: string;
  identity: string;
  restrictRawShellToLocalhost: boolean;
  rawShellBlockedForCaller: boolean;
  nativeDialogsAvailable: boolean;
  machineName: string;
  version: string;
}

export interface ConsoleSystemEntry {
  CanonicalName: string;
  Category: string;
  PreferredDatSource: string;
  DefaultExtensions: string;
  Aliases: string[];
  IsDiscBased: boolean;
}

export interface ConfigBootstrapResponse {
  config: AppConfig;
  snapshotsCount: number;
  consoleCatalog: ConsoleSystemEntry[];
  archiveTemplatesBuiltIn: ArchiveComplianceTemplate[];
}

export interface ConfigSnapshotInfo {
  SnapshotId: string;
  CreatedAtUtc: string;
  SizeBytes: number;
  DefinedJobCount: number;
  QueueCount: number;
}

export interface JobLiveStatusDto {
  JobId: string;
  JobName: string;
  State: string;
  ProgressPercent: number;
  DetailText: string;
  Processed: number;
  Skipped: number;
  Errors: number;
}

export interface InterruptedRunCheckpoint {
  RunKind: 'Queue' | 'SingleJob' | 'HashVerifier' | 'ComplianceScan' | string;
  QueueId?: string | null;
  QueueName?: string | null;
  InitialJobIds: string[];
  CompletedJobIds: string[];
  ActiveJobId?: string | null;
  ActiveJobName?: string | null;
  PendingChainedQueueIds: string[];
  IsDryRun: boolean;
  ForceCloudResync: boolean;
  VerifierSourceModule?: ModuleId | null;
  VerifierMode?: SidecarVerifyMode | null;
  LastProgressPercent: number;
  LastStatusText: string;
  StartedAtUtc: string;
  UpdatedAtUtc: string;
}

export interface PipelineRuntimeStateDto {
  IsRunning: boolean;
  IsPaused: boolean;
  IsProcessSuspended: boolean;
  IsDryRun: boolean;
  ActiveQueueId?: string | null;
  ActiveQueueName?: string | null;
  ActiveJobId?: string | null;
  ActiveJobName?: string | null;
  ActiveModuleBadge?: string | null;
  CurrentJobIndex: number;
  TotalJobs: number;
  ProgressPercent: number;
  SubprocessStatusText: string;
  FooterStatusText: string;
  IsWaitingForNetwork: boolean;
  NetworkWaitMessage: string;
  SessionProcessed: number;
  SessionSkipped: number;
  SessionErrors: number;
  PendingSwapCount: number;
  PendingSwapSampleFile?: string | null;
  GlobalNextRunStatusText: string;
  GlobalNextRunUtc?: string | null;
  GlobalNextRunQueueName?: string | null;
  PostRunPowerAction: string;
  InterruptedRun?: InterruptedRunCheckpoint | null;
  PendingRuns?: PendingPipelineRunDto[];
  JobStates: Record<string, JobLiveStatusDto>;
}

export interface PendingPipelineRunDto {
  ItemKey: string;
  RunKind: 'Queue' | 'SingleJob' | string;
  TargetId: string;
  DisplayName: string;
  IsDryRun: boolean;
  ForceCloudResync: boolean;
  TriggerSource: string;
  EnqueuedAtUtc: string;
}

export interface DiskDriveMetricItem {
  DriveLetter: string;
  DriveType?: string;
  IsNetwork?: boolean;
  IsReady?: boolean;
  VolumeLabel?: string;
  RemotePath?: string;
  FreeGb: number;
  TotalGb: number;
  FreeText: string;
  SummaryText: string;
}

export interface SystemPerformanceStats {
  CpuUsagePercent: number;
  RamUsedGb: number;
  RamTotalGb: number;
  RamUsagePercent: number;
  DiskDriveLetter: string;
  DiskFreeGb: number;
  DiskTotalGb: number;
  DiskReadWriteMbps: number;
  NetworkUsageMbps: number;
  Timestamp: string;
  Disks: DiskDriveMetricItem[];
}

export interface TelemetrySnapshotDto {
  Telemetry: SystemPerformanceStats;
  OpenComplianceCount: number;
  GlobalNextRunText: string;
  GlobalNextRunUtc?: string | null;
  GlobalNextRunQueueName?: string | null;
  PendingSwapCount: number;
  PendingSwapSampleFile?: string | null;
  SessionProcessed?: number;
  SessionSkipped?: number;
  SessionErrors?: number;
  FooterStatusText?: string;
  InterruptedRun?: InterruptedRunCheckpoint | null;
}

export interface TerminalLogEntry {
  Id: number;
  TimestampUtc: string;
  TimestampText: string;
  Tag: string;
  Message: string;
  FormattedLine: string;
}

export interface ModuleCatalogItemDto {
  Id: ModuleId;
  ModuleKey: string;
  Name: string;
  Icon: string;
  Category: string;
  ShortSummary: string;
  LongDescription: string;
  Capabilities: string;
  RequiredTools: string;
  SetupGuide: string;
  BestPractices: string;
  IsAppWideEnabled: boolean;
  IsToolReady: boolean;
  ToolStatusText: string;
  DefinedJobCount: number;
}

export interface ComplianceIssueDetail {
  Kind: string;
  Severity: string;
  InternalPath: string;
  SizeBytes: number;
  Crc32: string;
  Message: string;
  IsSafeExtraFileDeletable: boolean;
}

export interface ArchiveEntryPreviewNode {
  InternalPath: string;
  SizeBytes: number;
  Crc32: string;
  RoleClassification: string;
  StatusBadge: string;
  CanClean: boolean;
  SelectedForCleanup: boolean;
}

export interface ComplianceReportItem {
  ReportId: string;
  ConsoleName: string;
  TargetFileName: string;
  TargetFullPath: string;
  IsDirectory: boolean;
  TemplateId: string;
  TemplateDisplayName: string;
  Severity: 'Clean' | 'Info' | 'Warning' | 'Error';
  ResolutionStatus: 'Open' | 'Resolved' | 'Ignored';
  SummaryCode: string;
  SummaryText: string;
  PrimaryCategory: string;
  IssueCount: number;
  ExtraDeletableCount: number;
  FirstDetectedUtc: string;
  LastEvaluatedUtc: string;
  Issues: ComplianceIssueDetail[];
  EntryNodes: ArchiveEntryPreviewNode[];
}

export interface ReportsQueryResponse {
  kpis: {
    openCount: number;
    cleanableCount: number;
    missingOrFormatCount: number;
    resolvedOrIgnoredCount: number;
    totalCount: number;
  };
  consoles: string[];
  items: ComplianceReportItem[];
}

export interface ArchiveCleanupDryRunResult {
  ArchivePath: string;
  IsAllowed: boolean;
  CommandPreview: string;
  BlockReason: string;
  FilesToDelete: ArchiveEntryPreviewNode[];
  ProtectedFilesKept: ArchiveEntryPreviewNode[];
  ReclaimedBytes: number;
}

export type {
  ApplyCoexistencePresetRequest,
  CoexistenceConflictDto,
  CoexistenceManagedRootDto,
  CoexistencePeerSummaryDto,
  CoexistencePresetId,
  CoexistenceStatusDto,
  DependencyStatusItem,
  FsBrowseResponse,
  FsDriveItem,
  GreenroomStatusDto,
  RegexPlaygroundResultDto,
  RunHistoryEntry,
  TailscalePwaStatusDto,
  TerminalSizePreset,
  UiLayoutConfig,
} from './coexistenceModels';
export { MODULE_METADATA, type ModuleDisplayMeta } from '../constants/moduleMetadata';
