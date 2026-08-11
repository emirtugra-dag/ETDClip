using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ETDClip.Services
{
    public class FileCacheManager
    {
        private readonly string _cacheDirectory;

        public FileCacheManager()
        {
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ETDClip",
                "Cache"
            );

            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }

        public string GetCacheDirectory() => _cacheDirectory;

        public async Task<List<string>> CacheFilesAsync(string itemId, List<string> filePaths, int maxSingleFileSizeMB)
        {
            return await Task.Run(() =>
            {
                var cachedPaths = new List<string>();
                if (filePaths == null || !filePaths.Any()) return cachedPaths;

                string itemCacheDir = Path.Combine(_cacheDirectory, itemId);
                if (!Directory.Exists(itemCacheDir))
                {
                    Directory.CreateDirectory(itemCacheDir);
                }

                long maxSingleByteLimit = (long)maxSingleFileSizeMB * 1024 * 1024;

                foreach (var filePath in filePaths)
                {
                    try
                    {
                        if (!File.Exists(filePath)) continue;

                        var fileInfo = new FileInfo(filePath);
                        // Exclude files exceeding per-file size limit (e.g. > 50 MB)
                        if (fileInfo.Length > maxSingleByteLimit) continue;

                        string destination = Path.Combine(itemCacheDir, fileInfo.Name);
                        File.Copy(filePath, destination, overwrite: true);
                        cachedPaths.Add(destination);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Dosya önbellekleme hatası: {ex.Message}");
                    }
                }

                return cachedPaths;
            });
        }

        public List<string> GetAvailableFilePaths(Models.ClipboardItem item)
        {
            var result = new List<string>();
            if (item == null || item.Type != Models.ClipboardItemType.File) return result;

            // 1. Check original file paths first
            foreach (var path in item.FilePaths)
            {
                if (File.Exists(path))
                {
                    result.Add(path);
                }
            }

            // 2. If original files missing/deleted, check cached files
            if (!result.Any() && item.IsCached && item.CachedFilePaths != null)
            {
                foreach (var cachedPath in item.CachedFilePaths)
                {
                    if (File.Exists(cachedPath))
                    {
                        result.Add(cachedPath);
                    }
                }
            }

            return result;
        }

        public void DeleteCacheItem(Models.ClipboardItem item)
        {
            if (item == null) return;

            Task.Run(() =>
            {
                try
                {
                    // 1. Delete item cached files folder (Cache/<itemId>/)
                    string itemCacheDir = Path.Combine(_cacheDirectory, item.Id);
                    if (Directory.Exists(itemCacheDir))
                    {
                        Directory.Delete(itemCacheDir, recursive: true);
                    }

                    // 2. Delete item thumbnail if it is an image
                    if (item.Type == Models.ClipboardItemType.Image && !string.IsNullOrEmpty(item.ImagePath))
                    {
                        if (File.Exists(item.ImagePath))
                        {
                            File.Delete(item.ImagePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Item cache delete error: {ex.Message}");
                }
            });
        }

        public void ClearCache()
        {
            Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(_cacheDirectory))
                    {
                        Directory.Delete(_cacheDirectory, recursive: true);
                        Directory.CreateDirectory(_cacheDirectory);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Önbellek temizleme hatası: {ex.Message}");
                }
            });
        }
    }
}
