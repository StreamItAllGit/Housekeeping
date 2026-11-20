// @module: Housekeeping.Core/Services/Modules/MediaTranscoderModule.CoexistenceGuards
// @purpose: Redroom .hklock/.redroom-part deferral, {edition-*} & .srt companion preservation, promoted_integrity.json CRC32 cross-trust, and MediaInfo probes.
// @depends-on: System.Diagnostics, System.IO, System.Text.Json, MediaInfoLib, Housekeeping.Services
// @exports: MediaTranscoderModule (partial)
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Housekeeping.Interop;
using MediaInfoLib;

namespace Housekeeping.Services.Modules
{
    public enum HevcProbeStatus
    {
        Hevc = 0,
        NonHevc = 1,
        SkippedTrusted = 2,
        IndeterminateProbeFailed = 3,
        UnavailableNativeLibrary = 4
    }

    public sealed record HevcProbeResult(
        HevcProbeStatus Status,
        bool IsConfirmedHevc,
        bool ProbeSucceeded,
        string Diagnostic)
    {
        public bool ShouldKeepExisting => Status is HevcProbeStatus.Hevc or HevcProbeStatus.SkippedTrusted;
    }

    public static partial class MediaTranscoderModule
    {
        public static bool ShouldDeferForSisterMediaLock(string directoryPath, Action<string, string>? log)
        {
            if (NetworkSafetyService.HasActiveDirectoryOrParentLock(directoryPath, "housekeeping", out string? owner, out string? lockFile, scanImmediateSubdirectories: false, log))
            {
                log?.Invoke("WARN", $"  -> [Coexistence Defer] Deferring media transcode in '{directoryPath}' — active lock '{lockFile}' held by '{owner}'.");
                return true;
            }
            return false;
        }

        public static bool TryTrustRedroomPromotedIntegrity(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                string? dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrWhiteSpace(dir)) return false;

                string manifestPath = Path.Combine(dir, "promoted_integrity.json");
                if (!File.Exists(manifestPath)) return false;

                var fi = new FileInfo(filePath);
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = doc.RootElement;
                string fileName = fi.Name;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryMatchPromotedEntry(root, fileName, fi.Length))
                        return true;
                    if (root.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in filesEl.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, fileName, StringComparison.OrdinalIgnoreCase) &&
                                HasValidCrcAndSize(prop.Value, fi.Length))
                                return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaTranscoderModule] promoted_integrity.json check skipped: {ex.Message}");
            }
            return false;
        }

        private static bool TryMatchPromotedEntry(JsonElement obj, string fileName, long expectedSize)
        {
            if (obj.TryGetProperty("fileName", out var fn) &&
                string.Equals(fn.GetString(), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return HasValidCrcAndSize(obj, expectedSize);
            }
            return false;
        }

        private static bool HasValidCrcAndSize(JsonElement entry, long expectedSize)
        {
            if (entry.TryGetProperty("size", out var szEl) && szEl.TryGetInt64(out long sz) && sz != expectedSize)
                return false;
            if (entry.TryGetProperty("crc32", out var crcEl) && crcEl.ValueKind == JsonValueKind.String)
                return !string.IsNullOrWhiteSpace(crcEl.GetString());
            return false;
        }

        public static void CopyCompanionSubtitlesForOutput(FileInfo masterFile, string encodedOutputPath, Action<string, string>? log)
        {
            try
            {
                string? sourceDir = masterFile.DirectoryName;
                string? targetDir = Path.GetDirectoryName(encodedOutputPath);
                if (string.IsNullOrWhiteSpace(sourceDir) || string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(sourceDir))
                    return;

                string masterStem = Path.GetFileNameWithoutExtension(masterFile.Name);
                string outputStem = Path.GetFileNameWithoutExtension(encodedOutputPath);
                if (string.Equals(masterStem, outputStem, StringComparison.OrdinalIgnoreCase))
                    return;

                foreach (string srtPath in Directory.EnumerateFiles(sourceDir, "*.srt", SearchOption.TopDirectoryOnly))
                {
                    string srtName = Path.GetFileName(srtPath);
                    if (!srtName.StartsWith(masterStem, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string suffix = srtName.Substring(masterStem.Length);
                    string destSrt = Path.Combine(targetDir, outputStem + suffix);
                    if (!File.Exists(destSrt))
                    {
                        File.Copy(srtPath, destSrt, overwrite: false);
                        log?.Invoke("INFO", $"  -> Copied companion subtitle: {Path.GetFileName(destSrt)}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaTranscoderModule] Subtitle companion copy skipped: {ex.Message}");
            }
        }

        public static bool EnsureMediaInfoAvailableForRun(Action<string, string>? log = null, string? dataDirectory = null)
        {
            if (MediaInfoNativeResolver.TryVerifyLoadable(dataDirectory, null, out string diagnostic, out _))
                return true;

            log?.Invoke("WARN", $"[MediaInfo] Native inspection unavailable: {diagnostic}");
            return false;
        }

        private static int GetVideoHeightPx(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) return 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] Stat check skipped: {ex.Message}"); return 0; }

            if (!MediaInfoNativeResolver.IsAvailable())
                return 0;

            var mediaInfo = new MediaInfo();
            if (!mediaInfo.IsHandleValid) return 0;
            try
            {
                if (mediaInfo.Open(filePath) == 0) return 0;
                string heightStr = mediaInfo.Get(StreamKind.Video, 0, "Height");
                return int.TryParse(heightStr, out int h) ? h : 0;
            }
            catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] GetVideoHeightPx fallback: {ex.Message}"); return 0; }
            finally
            {
                try { mediaInfo.Close(); } catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] MediaInfo.Close ignored: {ex.Message}"); }
            }
        }

        public static HevcProbeResult ProbeFileIsHevc(string filePath, bool isRemoteMode, string? dataDirectory = null)
        {
            if (isRemoteMode)
                return new HevcProbeResult(HevcProbeStatus.SkippedTrusted, true, true, "Skipped MediaInfo probe (Fast Network / Remote mode).");
            if (TryTrustRedroomPromotedIntegrity(filePath))
                return new HevcProbeResult(HevcProbeStatus.SkippedTrusted, true, true, "Trusted promoted_integrity.json verification.");

            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0)
                    return new HevcProbeResult(HevcProbeStatus.IndeterminateProbeFailed, false, false, $"Source file '{filePath}' is missing or empty.");
            }
            catch (Exception ex)
            {
                return new HevcProbeResult(HevcProbeStatus.IndeterminateProbeFailed, false, false, $"File stat check failed: {ex.Message}");
            }

            if (!MediaInfoNativeResolver.TryVerifyLoadable(dataDirectory, null, out string diag, out _))
                return new HevcProbeResult(HevcProbeStatus.UnavailableNativeLibrary, false, false, diag);

            var mediaInfo = new MediaInfo();
            if (!mediaInfo.IsHandleValid)
                return new HevcProbeResult(HevcProbeStatus.UnavailableNativeLibrary, false, false, "MediaInfo native handle allocation failed.");

            try
            {
                if (mediaInfo.Open(filePath) == 0)
                    return new HevcProbeResult(HevcProbeStatus.IndeterminateProbeFailed, false, false, $"MediaInfo.Open failed for '{Path.GetFileName(filePath)}'.");

                string videoStreams = mediaInfo.Get(StreamKind.General, 0, "VideoCount");
                if (int.TryParse(videoStreams, out int videoCount) && videoCount > 0)
                {
                    string format = mediaInfo.Get(StreamKind.Video, 0, "Format");
                    string codec = mediaInfo.Get(StreamKind.Video, 0, "CodecID");
                    bool isHevc = format.Equals("HEVC", StringComparison.OrdinalIgnoreCase) ||
                                  codec.Contains("hvc1", StringComparison.OrdinalIgnoreCase) ||
                                  codec.Contains("hevc", StringComparison.OrdinalIgnoreCase) ||
                                  codec.Contains("x265", StringComparison.OrdinalIgnoreCase) ||
                                  codec.Contains("hev1", StringComparison.OrdinalIgnoreCase);
                    return isHevc
                        ? new HevcProbeResult(HevcProbeStatus.Hevc, true, true, $"Confirmed HEVC stream ({format}/{codec}).")
                        : new HevcProbeResult(HevcProbeStatus.NonHevc, false, true, $"Stream codec is '{format}/{codec}', not HEVC.");
                }
                return new HevcProbeResult(HevcProbeStatus.NonHevc, false, true, "No video stream detected in container.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                return new HevcProbeResult(HevcProbeStatus.UnavailableNativeLibrary, false, false, $"MediaInfo native load error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return new HevcProbeResult(HevcProbeStatus.IndeterminateProbeFailed, false, false, $"MediaInfo inspection failed: {ex.Message}");
            }
            finally
            {
                try { mediaInfo.Close(); } catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] MediaInfo.Close ignored: {ex.Message}"); }
            }
        }

        private static bool CheckFileIsHevc(string filePath, bool isRemoteMode) =>
            ProbeFileIsHevc(filePath, isRemoteMode).ShouldKeepExisting;

        private static bool HasLanguageTrack(string filePath, string languageCode)
        {
            try
            {
                if (!File.Exists(filePath) || new FileInfo(filePath).Length == 0) return false;
            }
            catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] HasLanguageTrack stat fallback: {ex.Message}"); return false; }

            if (!MediaInfoNativeResolver.IsAvailable())
                return false;

            var mediaInfo = new MediaInfo();
            if (!mediaInfo.IsHandleValid) return false;
            try
            {
                if (mediaInfo.Open(filePath) == 0) return false;
                int audioTracks = int.TryParse(mediaInfo.Get(StreamKind.Audio, 0, "StreamCount"), out int tracks) ? tracks : 0;
                for (int i = 0; i < audioTracks; i++)
                {
                    string lang = mediaInfo.Get(StreamKind.Audio, i, "Language/String3");
                    if (lang.Equals(languageCode, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] HasLanguageTrack fallback: {ex.Message}"); return false; }
            finally
            {
                try { mediaInfo.Close(); } catch (Exception ex) { Debug.WriteLine($"[MediaTranscoderModule] MediaInfo.Close ignored: {ex.Message}"); }
            }
            return false;
        }
    }
}
