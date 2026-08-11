using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ETDClip.Models
{
    public enum ClipboardItemType
    {
        Text,
        Image,
        File
    }

    public class ClipboardItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public ClipboardItemType Type { get; set; }
        public string TextContent { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new();
        public List<string> CachedFilePaths { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsPinned { get; set; }
        public bool IsCached { get; set; }
        public long TotalSizeBytes { get; set; }

        public string FormattedTime => Timestamp.ToString("HH:mm:ss - dd.MM.yyyy");

        public string DisplayTitle
        {
            get
            {
                return Type switch
                {
                    ClipboardItemType.Text => GetTruncatedText(TextContent, 80),
                    ClipboardItemType.Image => string.IsNullOrEmpty(ImagePath) ? "Görsel / Image" : Path.GetFileName(ImagePath),
                    ClipboardItemType.File => FilePaths.Count switch
                    {
                        0 => "Dosya Bulunamadı",
                        1 => Path.GetFileName(FilePaths[0]),
                        _ => $"{Path.GetFileName(FilePaths[0])} (+{FilePaths.Count - 1})"
                    },
                    _ => "Bilinmeyen İleti"
                };
            }
        }

        public string GetSubtitle(string lang = "TR")
        {
            bool isEn = lang == "EN";
            return Type switch
            {
                ClipboardItemType.Text => isEn 
                    ? $"{TextContent.Length} characters | {GetLineCount(TextContent)} lines" 
                    : $"{TextContent.Length} karakter | {GetLineCount(TextContent)} satır",
                ClipboardItemType.Image => FormattedSize,
                ClipboardItemType.File => isEn 
                    ? $"{FilePaths.Count} Files | Total: {FormattedSize}" 
                    : $"{FilePaths.Count} Dosya | Toplam: {FormattedSize}",
                _ => ""
            };
        }

        public string FormattedSize
        {
            get
            {
                if (TotalSizeBytes <= 0) return "0 KB";
                double kb = TotalSizeBytes / 1024.0;
                if (kb < 1024) return $"{kb:F1} KB";
                double mb = kb / 1024.0;
                if (mb < 1024) return $"{mb:F1} MB";
                double gb = mb / 1024.0;
                return $"{gb:F2} GB";
            }
        }

        public string TypeBadge => Type switch
        {
            ClipboardItemType.Text => "📝 METİN",
            ClipboardItemType.Image => "🖼️ RESİM",
            ClipboardItemType.File => "📁 DOSYA",
            _ => "ℹ️ BİLİNMEYEN"
        };

        public bool OriginalFilesExist
        {
            get
            {
                if (Type != ClipboardItemType.File || FilePaths.Count == 0) return true;
                return FilePaths.All(File.Exists);
            }
        }

        private static string GetTruncatedText(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string clean = text.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (clean.Length <= maxLength) return clean;
            return clean.Substring(0, maxLength) + "...";
        }

        private static int GetLineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return text.Split('\n').Length;
        }
    }
}
