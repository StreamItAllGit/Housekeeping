// @module: Housekeeping.Web/constants/emulatorRunners
// @purpose: Fixed emulator runner vocabulary matching RomLauncherGeneratorModule.CommandBuilder.cs 1:1.
// @depends-on: ../components/ProfileSelect
// @exports: RUNNER_TYPES
import type { SelectOptionItem } from '../components/ProfileSelect';

export const RUNNER_TYPES: SelectOptionItem[] = [
  { value: 'RetroArch', label: 'RetroArch (-L core)' },
  { value: 'PCSX2', label: 'PCSX2 (PS2)' },
  { value: 'DuckStation', label: 'DuckStation (PS1)' },
  { value: 'RPCS3', label: 'RPCS3 (PS3)' },
  { value: 'PPSSPP', label: 'PPSSPP (PSP)' },
  { value: 'Dolphin', label: 'Dolphin (GC / Wii)' },
  { value: 'Flycast', label: 'Flycast (Dreamcast / Naomi)' },
  { value: 'Redream', label: 'Redream (Dreamcast)' },
  { value: 'Project64', label: 'Project64 (N64)' },
  { value: 'RMG', label: 'RMG — Rosalie Mupen GUI (N64)' },
  { value: 'melonDS', label: 'melonDS (NDS / DSi)' },
  { value: 'DeSmuME', label: 'DeSmuME (NDS)' },
  { value: 'Citra', label: 'Citra (3DS)' },
  { value: 'Azahar', label: 'Azahar (3DS)' },
  { value: 'Lime3DS', label: 'Lime3DS (3DS)' },
  { value: 'Cemu', label: 'Cemu (Wii U)' },
  { value: 'Ryujinx', label: 'Ryujinx (Switch)' },
  { value: 'Yuzu', label: 'Yuzu (Switch)' },
  { value: 'Vita3K', label: 'Vita3K (PS Vita)' },
  { value: 'Xemu', label: 'Xemu (Original Xbox)' },
  { value: 'Xenia', label: 'Xenia (Xbox 360)' },
  { value: 'MAME', label: 'MAME (Arcade)' },
  { value: 'ScummVM', label: 'ScummVM (Adventure)' },
  { value: 'DOSBox', label: 'DOSBox / DOSBox-Staging' },
  { value: 'PCNative', label: 'PCNative (Direct EXE)' },
  { value: 'Custom', label: 'Custom ({emu} {rom} template)' },
];
