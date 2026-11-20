// @module: Housekeeping.Interop.MediaInfoNativeResolver
// @purpose: NativeLibrary DllImport resolver and Foundation ToolResolver probe for MediaInfo.dll without bundled binary copies.
// @depends-on: System, System.Collections.Generic, System.IO, System.Runtime.CompilerServices, System.Runtime.InteropServices, System.Threading, Foundation.Core
// @exports: Housekeeping.Interop.MediaInfoNativeResolver

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Foundation.Core;

namespace Housekeeping.Interop
{
    public static class MediaInfoNativeResolver
    {
        private static readonly string[] CandidateLibraryNames = ["MediaInfo.dll"];
        private static int _registered;
        private static string? _configuredDataDirectory;

        public static void EnsureRegistered(string? dataDirectory = null)
        {
            if (!string.IsNullOrWhiteSpace(dataDirectory))
                _configuredDataDirectory = Path.GetFullPath(dataDirectory);

            if (Interlocked.Exchange(ref _registered, 1) == 0)
                NativeLibrary.SetDllImportResolver(typeof(MediaInfoNativeResolver).Assembly, ResolveDllImport);
        }

        public static void ConfigureDataDirectory(string? dataDirectory)
        {
            _configuredDataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.GetFullPath(dataDirectory);
        }

        public static ToolResolutionResult Resolve(string? dataDirectory = null, string? explicitOverridePath = null)
        {
            EnsureRegistered(dataDirectory);
            string? overridePath = !string.IsNullOrWhiteSpace(explicitOverridePath)
                ? explicitOverridePath
                : Environment.GetEnvironmentVariable("HOUSEKEEPING_MEDIAINFO_PATH");

            string appBaseDir = AppContext.BaseDirectory;
            string[] extraDirs = BuildExtraSearchDirectories(appBaseDir);

            ToolResolutionResult appRes = ToolResolver.ResolveTool("MediaInfo.dll", CandidateLibraryNames, overridePath, appBaseDir, Array.Empty<string>());
            if (appRes.IsAvailable && appRes.Source is ToolResolutionSource.ExplicitOverride or ToolResolutionSource.BundledAppTools)
                return appRes;

            string? effectiveDataDir = ResolveEffectiveDataDirectory(dataDirectory, appBaseDir);
            if (!string.IsNullOrWhiteSpace(effectiveDataDir))
            {
                ToolResolutionResult dataRes = ToolResolver.ResolveTool("MediaInfo.dll", CandidateLibraryNames, null, effectiveDataDir, extraDirs);
                if (dataRes.IsAvailable)
                    return dataRes;
            }

            return ToolResolver.ResolveTool("MediaInfo.dll", CandidateLibraryNames, overridePath, appBaseDir, extraDirs);
        }

        public static bool IsAvailable(string? dataDirectory = null, string? explicitOverridePath = null) =>
            TryVerifyLoadable(dataDirectory, explicitOverridePath, out _, out _);

        public static string ResolveDiagnostic(string? dataDirectory = null, string? explicitOverridePath = null)
        {
            TryVerifyLoadable(dataDirectory, explicitOverridePath, out string diagnostic, out _);
            return diagnostic;
        }

        public static bool TryVerifyLoadable(
            string? dataDirectory,
            string? explicitOverridePath,
            out string diagnostic,
            out string? resolvedPath)
        {
            ToolResolutionResult res = Resolve(dataDirectory, explicitOverridePath);
            resolvedPath = res.ResolvedPath;
            if (!res.IsAvailable || string.IsNullOrWhiteSpace(resolvedPath))
            {
                diagnostic = res.DiagnosticMessage;
                return false;
            }

            if (NativeLibrary.TryLoad(resolvedPath, out IntPtr handle))
            {
                NativeLibrary.Free(handle);
                diagnostic = res.DiagnosticMessage;
                return true;
            }

            diagnostic = $"MediaInfo library found at '{resolvedPath}' ({res.Source}) but failed to load into the current process.";
            return false;
        }

        private static IntPtr ResolveDllImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, "MediaInfo.dll", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(libraryName, "MediaInfo", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            ToolResolutionResult res = Resolve(_configuredDataDirectory);
            if (res.IsAvailable && !string.IsNullOrWhiteSpace(res.ResolvedPath) &&
                NativeLibrary.TryLoad(res.ResolvedPath, out IntPtr handle))
            {
                return handle;
            }

            return IntPtr.Zero;
        }

        private static string? ResolveEffectiveDataDirectory(string? explicitDataDir, string appBaseDir)
        {
            if (!string.IsNullOrWhiteSpace(explicitDataDir))
                return Path.GetFullPath(explicitDataDir);

            if (!string.IsNullOrWhiteSpace(_configuredDataDirectory))
                return _configuredDataDirectory;

            foreach (string envName in new[] { "HOUSEKEEPING_DATA_DIR", "DATA_DIR", "HOUSEKEEPING_HOME", "APP_HOME" })
            {
                string? val = Environment.GetEnvironmentVariable(envName);
                if (!string.IsNullOrWhiteSpace(val))
                    return Path.GetFullPath(val.Trim());
            }

            string trimmed = Path.GetFullPath(appBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "app", StringComparison.OrdinalIgnoreCase) &&
                Directory.GetParent(trimmed) is { Exists: true } parent)
            {
                return Path.Combine(parent.FullName, "data");
            }

            return null;
        }

        private static string[] BuildExtraSearchDirectories(string appBaseDir)
        {
            var dirs = new List<string>
            {
                Path.Combine(appBaseDir, "runtimes", "win-x64", "native"),
                Path.Combine(appBaseDir, "runtimes", "win-arm64", "native")
            };

            string trimmed = Path.GetFullPath(appBaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(trimmed), "app", StringComparison.OrdinalIgnoreCase) &&
                Directory.GetParent(trimmed) is { Exists: true } parent)
                dirs.Add(Path.Combine(parent.FullName, "tools"));

            foreach (string? root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                if (!string.IsNullOrWhiteSpace(root))
                {
                    dirs.Add(Path.Combine(root, "MediaInfo"));
                    dirs.Add(Path.Combine(root, "MediaInfo CLI"));
                }
            }
            return dirs.ToArray();
        }
    }
}
