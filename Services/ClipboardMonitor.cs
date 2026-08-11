using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ETDClip.Models;

namespace ETDClip.Services
{
    public class ClipboardMonitor
    {
        private readonly HistoryManager _historyManager;
        private readonly FileCacheManager _cacheManager;
        private AppSettings _settings;
        private bool _isInternalCopy;
        private string _lastProcessedText = string.Empty;

        public event EventHandler<ClipboardItem>? ItemCopied;

        public ClipboardMonitor(HistoryManager historyManager, FileCacheManager cacheManager, AppSettings settings)
        {
            _historyManager = historyManager;
            _cacheManager = cacheManager;
            _settings = settings;
        }

        public void UpdateSettings(AppSettings settings) => _settings = settings;

        /// <summary>
        /// ClipboardMonitor artık kendi HwndSource hook'unu oluşturmuyor.
        /// WindowMessageSink.ClipboardChanged olayına abone olarak çalışıyor.
        /// </summary>
        public void OnClipboardChanged()
        {
            if (!_isInternalCopy)
                ProcessClipboardChange();
        }

        public void CopyItemToClipboard(ClipboardItem item)
        {
            _isInternalCopy = true;
            try
            {
                if (item.Type == ClipboardItemType.Text && !string.IsNullOrEmpty(item.TextContent))
                {
                    System.Windows.Clipboard.SetText(item.TextContent);
                }
                else if (item.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(item.ImagePath);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    System.Windows.Clipboard.SetImage(bitmap);
                }
                else if (item.Type == ClipboardItemType.File)
                {
                    var validPaths = _cacheManager.GetAvailableFilePaths(item);
                    if (validPaths.Any())
                    {
                        var collection = new System.Collections.Specialized.StringCollection();
                        collection.AddRange(validPaths.ToArray());
                        System.Windows.Clipboard.SetFileDropList(collection);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CopyItemToClipboard error: {ex.Message}");
            }
            finally
            {
                // Reset internal-copy flag after a short delay so WM_CLIPBOARDUPDATE is ignored
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(600)
                };
                timer.Tick += (s, e) =>
                {
                    _isInternalCopy = false;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private async void ProcessClipboardChange()
        {
            // Give source app time to finish writing then retry up to 3x
            await Task.Delay(80);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { await ProcessClipboardChangeCore(); return; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard attempt {attempt + 1} failed: {ex.Message}");
                    if (attempt < 2) await Task.Delay(150 * (attempt + 1));
                }
            }
        }

        private async Task ProcessClipboardChangeCore()
        {
            try
            {
                // 1. File drop list
                if (System.Windows.Clipboard.ContainsFileDropList())
                {
                    var dropList = System.Windows.Clipboard.GetFileDropList();
                    if (dropList != null && dropList.Count > 0)
                    {
                        var filePaths = dropList.Cast<string>().Where(File.Exists).ToList();
                        if (filePaths.Any())
                        {
                            long totalSize = 0;
                            foreach (var p in filePaths)
                                try { totalSize += new FileInfo(p).Length; } catch { }

                            var newItem = new ClipboardItem
                            {
                                Type = ClipboardItemType.File,
                                FilePaths = filePaths,
                                TotalSizeBytes = totalSize,
                                Timestamp = DateTime.Now
                            };

                            if (_settings.AutoCacheFiles)
                            {
                                var cached = await _cacheManager.CacheFilesAsync(newItem.Id, filePaths, _settings.MaxSingleFileSizeMB);
                                if (cached.Any())
                                {
                                    newItem.IsCached = true;
                                    newItem.CachedFilePaths = cached;
                                }
                            }

                            var added = _historyManager.AddOrUpdateItem(newItem, _settings.MaxHistoryItems);
                            ItemCopied?.Invoke(this, added);
                            return;
                        }
                    }
                }

                // 2. Image
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var imageSource = System.Windows.Clipboard.GetImage();
                    if (imageSource != null)
                    {
                        string itemId = Guid.NewGuid().ToString();
                        string savedPath = SaveBitmapToPng(imageSource, itemId);
                        if (!string.IsNullOrEmpty(savedPath))
                        {
                            long imageSize = new FileInfo(savedPath).Length;
                            var newItem = new ClipboardItem
                            {
                                Id = itemId,
                                Type = ClipboardItemType.Image,
                                ImagePath = savedPath,
                                TotalSizeBytes = imageSize,
                                Timestamp = DateTime.Now
                            };

                            var added = _historyManager.AddOrUpdateItem(newItem, _settings.MaxHistoryItems);
                            ItemCopied?.Invoke(this, added);
                            return;
                        }
                    }
                }

                // 3. Text
                if (System.Windows.Clipboard.ContainsText())
                {
                    string text = System.Windows.Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(text) && text != _lastProcessedText)
                    {
                        _lastProcessedText = text;
                        var newItem = new ClipboardItem
                        {
                            Type = ClipboardItemType.Text,
                            TextContent = text,
                            TotalSizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
                            Timestamp = DateTime.Now
                        };

                        var added = _historyManager.AddOrUpdateItem(newItem, _settings.MaxHistoryItems);
                        ItemCopied?.Invoke(this, added);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessClipboardChangeCore error: {ex.Message}");
                throw; // let retry loop handle it
            }
        }

        private string SaveBitmapToPng(System.Windows.Media.Imaging.BitmapSource bitmapSource, string itemId)
        {
            try
            {
                string imagesDir = Path.Combine(_cacheManager.GetCacheDirectory(), "Thumbnails");
                if (!Directory.Exists(imagesDir)) Directory.CreateDirectory(imagesDir);

                string imagePath = Path.Combine(imagesDir, $"{itemId}.png");
                using var fileStream = new FileStream(imagePath, FileMode.Create);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                encoder.Save(fileStream);
                return imagePath;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
