using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ETDClip.Models;

namespace ETDClip.Services
{
    public class HistoryManager
    {
        private readonly string _storagePath;
        private readonly List<ClipboardItem> _items = new();
        private readonly object _lockObj = new();
        public event EventHandler? HistoryUpdated;
        public event EventHandler<ClipboardItem>? ItemRemoved;

        public HistoryManager()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ETDClip"
            );

            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            _storagePath = Path.Combine(appData, "history.json");

            LoadHistory();
        }

        public List<ClipboardItem> GetItems(int limit = 10, string searchQuery = "", string categoryFilter = "All")
        {
            lock (_lockObj)
            {
                IEnumerable<ClipboardItem> query = _items;

                if (categoryFilter == "Text")
                    query = query.Where(i => i.Type == ClipboardItemType.Text);
                else if (categoryFilter == "Image")
                    query = query.Where(i => i.Type == ClipboardItemType.Image);
                else if (categoryFilter == "File")
                    query = query.Where(i => i.Type == ClipboardItemType.File);
                else if (categoryFilter == "Pinned")
                    query = query.Where(i => i.IsPinned);

                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    string searchLower = searchQuery.ToLowerInvariant();
                    query = query.Where(i =>
                        (i.TextContent != null && i.TextContent.ToLowerInvariant().Contains(searchLower)) ||
                        (i.FilePaths != null && i.FilePaths.Any(f => Path.GetFileName(f).ToLowerInvariant().Contains(searchLower)))
                    );
                }

                var list = query.OrderByDescending(i => i.IsPinned)
                               .ThenByDescending(i => i.Timestamp);

                if (limit > 0 && string.IsNullOrWhiteSpace(searchQuery) && categoryFilter == "All")
                {
                    return list.Take(limit).ToList();
                }

                return list.ToList();
            }
        }

        public void ValidateAndPurgeMissingFiles(FileCacheManager cacheManager)
        {
            lock (_lockObj)
            {
                bool changed = false;
                var fileItems = _items.Where(i => i.Type == ClipboardItemType.File).ToList();

                foreach (var item in fileItems)
                {
                    var availablePaths = cacheManager.GetAvailableFilePaths(item);
                    if (!availablePaths.Any())
                    {
                        _items.Remove(item);
                        changed = true;
                    }
                }

                if (changed)
                {
                    SaveHistory();
                }
            }
        }

        public ClipboardItem AddOrUpdateItem(ClipboardItem newItem, int maxHistoryItems = 10)
        {
            lock (_lockObj)
            {
                var existingItem = _items.FirstOrDefault(existing => IsDuplicate(existing, newItem));

                if (existingItem != null)
                {
                    existingItem.Timestamp = DateTime.Now;

                    if (newItem.IsCached)
                    {
                        existingItem.IsCached = true;
                        existingItem.CachedFilePaths = newItem.CachedFilePaths;
                    }

                    _items.Remove(existingItem);
                    _items.Insert(0, existingItem);
                    SaveHistory();
                    HistoryUpdated?.Invoke(this, EventArgs.Empty);
                    return existingItem;
                }

                _items.Insert(0, newItem);
                TrimHistory(maxHistoryItems);

                SaveHistory();
                HistoryUpdated?.Invoke(this, EventArgs.Empty);
                return newItem;
            }
        }

        public void RemoveItem(string id)
        {
            lock (_lockObj)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item != null)
                {
                    _items.Remove(item);
                    SaveHistory();
                    ItemRemoved?.Invoke(this, item);
                    HistoryUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void TogglePin(string id)
        {
            lock (_lockObj)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item != null)
                {
                    item.IsPinned = !item.IsPinned;
                    SaveHistory();
                    HistoryUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void ClearHistory(bool keepPinned = true)
        {
            lock (_lockObj)
            {
                var removedList = new List<ClipboardItem>();
                if (keepPinned)
                {
                    var itemsToRemove = _items.Where(i => !i.IsPinned).ToList();
                    foreach (var item in itemsToRemove)
                    {
                        _items.Remove(item);
                        removedList.Add(item);
                    }
                }
                else
                {
                    removedList.AddRange(_items);
                    _items.Clear();
                }

                if (removedList.Any())
                {
                    SaveHistory();
                    foreach (var item in removedList)
                    {
                        ItemRemoved?.Invoke(this, item);
                    }
                    HistoryUpdated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private static bool IsDuplicate(ClipboardItem existing, ClipboardItem newItem)
        {
            if (existing.Type != newItem.Type) return false;

            return existing.Type switch
            {
                ClipboardItemType.Text => string.Equals(existing.TextContent?.Trim(), newItem.TextContent?.Trim(), StringComparison.Ordinal),
                ClipboardItemType.Image => existing.TotalSizeBytes == newItem.TotalSizeBytes && !string.IsNullOrEmpty(existing.ImagePath),
                ClipboardItemType.File => existing.FilePaths.SequenceEqual(newItem.FilePaths),
                _ => false
            };
        }

        private void TrimHistory(int maxHistoryItems)
        {
            if (maxHistoryItems <= 0) return;

            var unpinnedItems = _items.Where(i => !i.IsPinned).ToList();
            if (unpinnedItems.Count > maxHistoryItems)
            {
                var itemsToRemove = unpinnedItems.Skip(maxHistoryItems).ToList();
                foreach (var item in itemsToRemove)
                {
                    _items.Remove(item);
                    ItemRemoved?.Invoke(this, item);
                }
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    string json = File.ReadAllText(_storagePath);
                    var loaded = JsonSerializer.Deserialize<List<ClipboardItem>>(json);
                    if (loaded != null)
                    {
                        _items.Clear();
                        _items.AddRange(loaded);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Geçmiş yükleme hatası: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_items, options);
                File.WriteAllText(_storagePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Geçmiş kaydetme hatası: {ex.Message}");
            }
        }
    }
}
