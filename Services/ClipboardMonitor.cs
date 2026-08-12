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
        private DateTime _lastProcessedTime = DateTime.MinValue;

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
            if (_isInternalCopy) return;

            // Immediate check (15ms) then 5-attempt retry loop
            await Task.Delay(15);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    bool success = false;
                    var app = System.Windows.Application.Current;
                    if (app != null)
                    {
                        await app.Dispatcher.InvokeAsync(async () =>
                        {
                            success = await ProcessClipboardChangeCore();
                        });
                    }
                    else
                    {
                        success = await ProcessClipboardChangeCore();
                    }

                    if (success) return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Clipboard attempt {attempt + 1} failed: {ex.Message}");
                }

                if (attempt < 4)
                {
                    await Task.Delay(100 * (attempt + 1));
                }
            }
        }

        private async Task<bool> ProcessClipboardChangeCore()
        {
            try
            {
                var dataObj = System.Windows.Clipboard.GetDataObject();

                // 1. Files
                if (dataObj != null && dataObj.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    var filesObj = dataObj.GetData(System.Windows.DataFormats.FileDrop);
                    List<string>? filePaths = null;

                    if (filesObj is string[] filesArray)
                    {
                        filePaths = filesArray.Where(File.Exists).ToList();
                    }
                    else if (filesObj is System.Collections.Specialized.StringCollection stringCol)
                    {
                        filePaths = stringCol.Cast<string>().Where(File.Exists).ToList();
                    }

                    if (filePaths != null && filePaths.Any())
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
                        if (added != null) ItemCopied?.Invoke(this, added);
                        return true;
                    }
                }

                // 2. Images (Try WPF, PNG Stream, Forms Clipboard, Win32 CF_BITMAP, CF_DIB memory)
                System.Windows.Media.Imaging.BitmapSource? bitmapSource = TryGetImageFromAllSources(dataObj);

                if (bitmapSource != null)
                {
                    string itemId = Guid.NewGuid().ToString();
                    string savedPath = SaveBitmapToPng(bitmapSource, itemId);
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
                        if (added != null) ItemCopied?.Invoke(this, added);
                        return true;
                    }
                }

                // 3. Text (UnicodeText, Text, StringFormat fallbacks)
                string? text = null;

                if (System.Windows.Clipboard.ContainsText())
                {
                    try { text = System.Windows.Clipboard.GetText(); } catch { }
                }

                if (string.IsNullOrEmpty(text) && dataObj != null && dataObj.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                {
                    try { text = dataObj.GetData(System.Windows.DataFormats.UnicodeText) as string; } catch { }
                }

                if (string.IsNullOrEmpty(text) && dataObj != null && dataObj.GetDataPresent(System.Windows.DataFormats.Text))
                {
                    try { text = dataObj.GetData(System.Windows.DataFormats.Text) as string; } catch { }
                }

                if (string.IsNullOrEmpty(text) && dataObj != null && dataObj.GetDataPresent(System.Windows.DataFormats.StringFormat))
                {
                    try { text = dataObj.GetData(System.Windows.DataFormats.StringFormat) as string; } catch { }
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Debounce: Ignore duplicate WM_CLIPBOARDUPDATE notifications if same text in last 800ms
                    if (text == _lastProcessedText && (DateTime.Now - _lastProcessedTime).TotalMilliseconds < 800)
                    {
                        return true; // Already processed
                    }

                    _lastProcessedText = text;
                    _lastProcessedTime = DateTime.Now;

                    var newItem = new ClipboardItem
                    {
                        Type = ClipboardItemType.Text,
                        TextContent = text,
                        TotalSizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
                        Timestamp = DateTime.Now
                    };

                    var added = _historyManager.AddOrUpdateItem(newItem, _settings.MaxHistoryItems);
                    if (added != null) ItemCopied?.Invoke(this, added);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProcessClipboardChangeCore error: {ex.Message}");
                throw;
            }

            return false;
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

        private System.Windows.Media.Imaging.BitmapSource? TryGetImageFromAllSources(System.Windows.IDataObject? dataObj)
        {
            // 1. WPF Clipboard GetImage
            try
            {
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var img = System.Windows.Clipboard.GetImage();
                    if (img != null) return img;
                }
            }
            catch { }

            // 2. PNG stream or Bitmap from WPF IDataObject
            if (dataObj != null)
            {
                try
                {
                    if (dataObj.GetDataPresent("PNG") && dataObj.GetData("PNG") is Stream pngStream)
                    {
                        var pngDecoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
                            pngStream,
                            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                        var frame = pngDecoder.Frames[0];
                        frame.Freeze();
                        return frame;
                    }
                }
                catch { }

                try
                {
                    if (dataObj.GetDataPresent(System.Windows.DataFormats.Bitmap))
                    {
                        if (dataObj.GetData(System.Windows.DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bs)
                        {
                            return bs;
                        }
                        else if (dataObj.GetData(System.Windows.DataFormats.Bitmap) is System.Drawing.Bitmap sysBmp)
                        {
                            IntPtr hBmp = sysBmp.GetHbitmap();
                            try
                            {
                                var gdiBs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    hBmp, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                gdiBs.Freeze();
                                return gdiBs;
                            }
                            finally
                            {
                                Win32Api.DeleteObject(hBmp);
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Windows Forms Clipboard GetImage (handles COM/OLE DIBs)
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsImage())
                {
                    var sysImg = System.Windows.Forms.Clipboard.GetImage();
                    if (sysImg != null)
                    {
                        using (sysImg)
                        {
                            using var ms = new MemoryStream();
                            sysImg.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;
                            var pngDecoder = new System.Windows.Media.Imaging.PngBitmapDecoder(
                                ms,
                                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                            var frame = pngDecoder.Frames[0];
                            frame.Freeze();
                            return frame;
                        }
                    }
                }
            }
            catch { }

            // 4. Native Win32 OpenClipboard + CF_BITMAP / CF_DIB (specifically for Lightshot / Snipping Tool)
            try
            {
                if (Win32Api.IsClipboardFormatAvailable(Win32Api.CF_BITMAP) ||
                    Win32Api.IsClipboardFormatAvailable(Win32Api.CF_DIB) ||
                    Win32Api.IsClipboardFormatAvailable(Win32Api.CF_DIBV5))
                {
                    if (Win32Api.OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            // 4a. CF_BITMAP GDI handle
                            IntPtr hBitmap = Win32Api.GetClipboardData(Win32Api.CF_BITMAP);
                            if (hBitmap != IntPtr.Zero)
                            {
                                var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                    hBitmap,
                                    IntPtr.Zero,
                                    System.Windows.Int32Rect.Empty,
                                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                                bs.Freeze();
                                return bs;
                            }

                            // 4b. CF_DIB or CF_DIBV5 global memory structure
                            IntPtr hDib = Win32Api.GetClipboardData(Win32Api.CF_DIB);
                            if (hDib == IntPtr.Zero) hDib = Win32Api.GetClipboardData(Win32Api.CF_DIBV5);

                            if (hDib != IntPtr.Zero)
                            {
                                IntPtr pDib = Win32Api.GlobalLock(hDib);
                                if (pDib != IntPtr.Zero)
                                {
                                    try
                                    {
                                        int size = (int)Win32Api.GlobalSize(hDib).ToUInt64();
                                        byte[] dibBytes = new byte[size];
                                        System.Runtime.InteropServices.Marshal.Copy(pDib, dibBytes, 0, size);

                                        int biSize = BitConverter.ToInt32(dibBytes, 0);
                                        int biBitCount = BitConverter.ToInt16(dibBytes, 14);
                                        int biClrUsed = BitConverter.ToInt32(dibBytes, 32);

                                        int colorTableSize = 0;
                                        if (biBitCount <= 8)
                                        {
                                            colorTableSize = (biClrUsed == 0 ? (1 << biBitCount) : biClrUsed) * 4;
                                        }

                                        int offBits = 14 + biSize + colorTableSize;
                                        int fileSize = 14 + size;

                                        byte[] bmpFileBytes = new byte[fileSize];
                                        bmpFileBytes[0] = (byte)'B';
                                        bmpFileBytes[1] = (byte)'M';
                                        BitConverter.GetBytes(fileSize).CopyTo(bmpFileBytes, 2);
                                        BitConverter.GetBytes(offBits).CopyTo(bmpFileBytes, 10);
                                        Array.Copy(dibBytes, 0, bmpFileBytes, 14, size);

                                        using var ms = new MemoryStream(bmpFileBytes);
                                        var decoder = new System.Windows.Media.Imaging.BmpBitmapDecoder(
                                            ms,
                                            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                                            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                                        var frame = decoder.Frames[0];
                                        frame.Freeze();
                                        return frame;
                                    }
                                    finally
                                    {
                                        Win32Api.GlobalUnlock(hDib);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Win32Api.CloseClipboard();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryGetImageFromAllSources Native Win32 error: {ex.Message}");
            }

            return null;
        }
    }
}
