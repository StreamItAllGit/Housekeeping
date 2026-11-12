// @module: Housekeeping.Tests.TestCleanupHelper
// @purpose: Logged best-effort disposal and temporary directory cleanup helpers for xUnit suites without bare catch blocks.
// @depends-on: System, System.Diagnostics, System.IO, System.Threading.Tasks
// @exports: Housekeeping.Tests.TestCleanupHelper

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Housekeeping.Tests
{
    internal static class TestCleanupHelper
    {
        public static void TryDeleteDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestCleanupHelper] Temporary directory cleanup skipped ({path}): {ex.Message}");
            }
        }

        public static void TryDispose(IDisposable? disposable, string context = "IDisposable")
        {
            if (disposable == null)
                return;

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestCleanupHelper] Dispose skipped ({context}): {ex.Message}");
            }
        }

        public static async Task TryAwaitAsync(Func<Task> action, string context = "AsyncCleanup")
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TestCleanupHelper] {context} skipped: {ex.Message}");
            }
        }
    }
}
