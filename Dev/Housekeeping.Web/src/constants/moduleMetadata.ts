// @module: Housekeeping.Web/constants/moduleMetadata
// @purpose: Centralized icon, single-line badge, short label, and summary metadata for all 8 Housekeeping modules.
// @depends-on: ../types/models
// @exports: MODULE_METADATA, ModuleDisplayMeta
import type { ModuleId } from '../types/models';

export interface ModuleDisplayMeta {
  icon: string;
  label: string;
  badge: string;
  short: string;
  desc: string;
}

export const MODULE_METADATA: Record<ModuleId, ModuleDisplayMeta> = {
  ProxyGenerator: {
    icon: '🎬',
    label: 'Video Proxy Generator',
    badge: '🎬 Proxy Gen',
    short: 'Proxy Gen',
    desc: 'Creates lightweight editing proxies next to source footage for smooth NLE scrubbing.',
  },
  ArchiveCompressor: {
    icon: '📦',
    label: 'Video Archive Compressor',
    badge: '📦 Video Archiver',
    short: 'Video Archiver',
    desc: 'Compresses working video folders into space-efficient long-term HEVC/AV1 archives.',
  },
  MediaTranscoder: {
    icon: '📺',
    label: 'Multi-Tier Media Transcoder',
    badge: '📺 Media Transcoder',
    short: 'Media Transcoder',
    desc: 'Scans media drives for master sources and generates 144p–4K streaming tiers for Plex/Jellyfin.',
  },
  RomLauncherGenerator: {
    icon: '🎮',
    label: 'Game Launchers & Save Sync',
    badge: '🎮 Game Launchers',
    short: 'Game Launchers',
    desc: 'Builds .m3u playlists, 7z ROM packages, one-click emulator launchers, and pre/post save sync.',
  },
  CloudSync: {
    icon: '☁',
    label: 'Cloud Folder Sync (rclone)',
    badge: '☁ Cloud Sync',
    short: 'Cloud Sync',
    desc: 'Synchronizes local folders with cloud remotes (Google Drive, OneDrive, NAS) via rclone.',
  },
  HumbleSync: {
    icon: '📚',
    label: 'Humble Bundle Library Sync',
    badge: '📚 Humble Library',
    short: 'Humble Library',
    desc: 'Downloads and updates purchased Humble Bundle eBooks, audio, and DRM-free games via hbd.',
  },
  CustomScripts: {
    icon: '⚡',
    label: 'Custom Scripts & CLI Tasks',
    badge: '⚡ Custom Tasks',
    short: 'Custom Tasks',
    desc: 'Runs custom .bat, .cmd, .ps1 PowerShell, Python scripts, or command-line tools.',
  },
  CollectionManifest: {
    icon: '📋',
    label: 'Collection Manifest & Bit Rot Auditor',
    badge: '📋 Collection Manifest',
    short: 'Collection Manifest',
    desc: 'Audits video & game vaults with No-Intro/Redump DATs, 4-mode .hash sidecars, .archive_hashes.json & regex rules.',
  },
};
