using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Housekeeping.Services
{
    public class PendingSwapItem
    {
        public string WorkingPath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
        public string Title { get; set; } = string.Empty;
    }

    public class PendingSwapService
    {
        private readonly string _storagePath;
        private readonly List<PendingSwapItem> _items = new();
        private readonly object _lock = new();

        public event Action? Changed;

        public PendingSwapService(string? storagePath = null)
        {
            _storagePath = storagePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pending_swaps.json");
            Load();
        }

        public IReadOnlyList<PendingSwapItem> GetAll()
        {
            lock (_lock)
            {
                return _items.ToList();
            }
        }

        public int Count
        {
            get
            {
                lock (_lock) return _items.Count;
            }
        }

        public bool IsPending(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            lock (_lock)
            {
                return _items.Any(i =>
                    string.Equals(i.WorkingPath, path, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.TargetPath, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        public PendingSwapItem? FindByTarget(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return null;
            lock (_lock)
            {
                return _items.FirstOrDefault(i => string.Equals(i.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void AddOrUpdate(string workingPath, string targetPath, string? title = null)
        {
            lock (_lock)
            {
                var existing = _items.FirstOrDefault(i =>
                    string.Equals(i.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));

                long size = 0;
                try
                {
                    if (File.Exists(workingPath))
                        size = new FileInfo(workingPath).Length;
                }
                catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[PendingSwapService] Non-fatal error ignored: {__suppressedEx1.Message}"); }

                if (existing != null)
                {
                    existing.WorkingPath = workingPath;
                    existing.FileSize = size;
                    existing.CompletedAtUtc = DateTime.UtcNow;
                    if (!string.IsNullOrWhiteSpace(title))
                        existing.Title = title;
                }
                else
                {
                    _items.Add(new PendingSwapItem
                    {
                        WorkingPath = workingPath,
                        TargetPath = targetPath,
                        FileSize = size,
                        CompletedAtUtc = DateTime.UtcNow,
                        Title = title ?? Path.GetFileName(targetPath)
                    });
                }

                Save();
            }
            Changed?.Invoke();
        }

        public bool RetrySwap(PendingSwapItem item, Action<string, string>? log = null)
        {
            lock (_lock)
            {
                if (!File.Exists(item.WorkingPath))
                {
                    // Working file was removed or already moved
                    _items.Remove(item);
                    Save();
                    Changed?.Invoke();
                    return false;
                }

                try
                {
                    string? dir = Path.GetDirectoryName(item.TargetPath);
                    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // Probe exclusive read/write access on WorkingPath first so a locked .partial file
                    // NEVER deletes or modifies an existing TargetPath.
                    try
                    {
                        File.SetAttributes(item.WorkingPath, FileAttributes.Normal);
                        using var workingProbe = new FileStream(item.WorkingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (Exception exWork)
                    {
                        log?.Invoke("NET", $"[PendingSwap] Working file '{Path.GetFileName(item.WorkingPath)}' is still locked ({exWork.Message}).");
                        return false;
                    }

                    if (File.Exists(item.TargetPath))
                    {
                        try
                        {
                            File.SetAttributes(item.TargetPath, FileAttributes.Normal);
                            using var targetProbe = new FileStream(item.TargetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                        catch (Exception exTarget)
                        {
                            log?.Invoke("NET", $"[PendingSwap] Destination '{Path.GetFileName(item.TargetPath)}' is still locked ({exTarget.Message}).");
                            return false;
                        }
                    }

                    File.Move(item.WorkingPath, item.TargetPath, overwrite: true);
                    _items.Remove(item);
                    Save();
                    Changed?.Invoke();
                    log?.Invoke("SUCCESS", $"[PendingSwap] Successfully committed locked file: {Path.GetFileName(item.TargetPath)}");
                    return true;
                }
                catch (Exception ex)
                {
                    log?.Invoke("NET", $"[PendingSwap] Move to '{Path.GetFileName(item.TargetPath)}' failed: {ex.Message}");
                    return false;
                }
            }
        }

        public int SweepAndCommitAll(Action<string, string>? log = null)
        {
            List<PendingSwapItem> copy;
            lock (_lock)
            {
                copy = _items.ToList();
            }

            if (copy.Count == 0) return 0;

            int committed = 0;
            foreach (var item in copy)
            {
                if (RetrySwap(item, log))
                {
                    committed++;
                }
            }

            if (committed > 0)
            {
                log?.Invoke("INFO", $"[PendingSwap] Swept and committed {committed} locked file(s).");
            }

            return committed;
        }

        public void Discard(PendingSwapItem item, Action<string, string>? log = null)
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(item.WorkingPath))
                    {
                        File.Delete(item.WorkingPath);
                        log?.Invoke("WARN", $"[PendingSwap] Discarded working file: {Path.GetFileName(item.WorkingPath)}");
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke("ERROR", $"[PendingSwap] Could not delete '{item.WorkingPath}': {ex.Message}");
                }

                _items.Remove(item);
                Save();
                Changed?.Invoke();
            }
        }

        public void DiscardAll(Action<string, string>? log = null)
        {
            List<PendingSwapItem> copy;
            lock (_lock) { copy = _items.ToList(); }
            foreach (var item in copy)
            {
                Discard(item, log);
            }
        }

        private void Load()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_storagePath))
                    {
                        string json = File.ReadAllText(_storagePath);
                        var loaded = JsonSerializer.Deserialize<List<PendingSwapItem>>(json);
                        if (loaded != null)
                        {
                            _items.Clear();
                            _items.AddRange(loaded);
                        }
                    }
                }
                catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[PendingSwapService] Non-fatal error ignored: {__suppressedEx2.Message}"); }
            }
        }

        private void Save()
        {
            lock (_lock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                    ConfigService.AtomicWriteAllText(_storagePath, json);
                }
                catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[PendingSwapService] Non-fatal error ignored: {__suppressedEx3.Message}"); }
            }
        }
    }
}
