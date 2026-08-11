using System;
using System.Collections.Generic;

namespace ETDClip.Services
{
    public static class LocalizationManager
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
        {
            ["TR"] = new Dictionary<string, string>
            {
                ["AppSubtitle"] = "Pano Yöneticisi",
                ["SearchPlaceholder"] = "Pano geçmişinde ara...",
                ["TabAll"] = "Tümü",
                ["TabText"] = "Metin",
                ["TabImage"] = "Resimler",
                ["TabFile"] = "Dosyalar",
                ["TabPinned"] = "Sabitlenenler",
                ["StatusFormat"] = "Varsayılan {0} Öğeden {1} Öğe Gösteriliyor",
                ["ClearAll"] = "Tümünü Temizle",
                ["SettingsTitle"] = "ETDClip Ayarları",
                ["GlobalHotkey"] = "Global Kısayol Tuşu:",
                ["RecordHotkey"] = "Tuş Kaydet",
                ["RecordingHotkey"] = "Kısayol Tuşuna Basın...",
                ["RecordingState"] = "Kayıt Yapılıyor...",
                ["MaxHistoryItems"] = "Gösterilecek Öğe Sayısı (Maks 50):",
                ["MaxFileSizeMB"] = "Önbellek Limiti (MB, Maks 1024):",
                ["MaxFileSizeHint"] = "* Belirtilen MB limitinden küçük dosyalar otomatik diske yedeklenir. Büyük dosyalar panoda kalır.",
                ["AutoCacheFiles"] = "Küçük dosyaları arka planda otomatik önbelleğe al",
                ["AutoStartWindows"] = "Windows başlangıcında otomatik çalıştır",
                ["ThemeLabel"] = "Uygulama Teması:",
                ["ThemeSystem"] = "Sistem Varsayılanı",
                ["ThemeDark"] = "Koyu Tema",
                ["ThemeLight"] = "Açık Tema",
                ["Language"] = "Dil / Language:",
                ["SaveSettings"] = "Ayarları Kaydet",
                ["AuthorLicense"] = "Yapımcı: Emir Tuğra Dağ | Lisans: MIT",
                ["EmptyHistory"] = "Pano geçmişinde henüz bir öğe yok.\nHerhangi bir metin, resim veya dosya kopyaladığınızda burada görünecektir.",
                ["ItemCopiedToast"] = "\"{0}\" Panoya Kopyalandı!",
                ["ClearPromptTitle"] = "Geçmişi Temizle",
                ["ClearPromptMsg"] = "Tüm pano geçmişini temizlemek istediğinizden emin misiniz?\n(Sabitlenmiş öğeler korunacaktır)",
                ["SavedToast"] = "Ayarlar başarıyla kaydedildi!",
                ["TrayShow"] = "ETDClip'i Aç",
                ["TraySettings"] = "Ayarlar",
                ["TrayClear"] = "Geçmişi Temizle",
                ["TrayExit"] = "Çıkış",
                ["TooltipCopy"] = "Panoya Tekrardan Kopyala",
                ["TooltipPin"] = "En Üste Sabitle",
                ["TooltipUnpin"] = "Sabitlemeyi Kaldır",
                ["TooltipDelete"] = "Geçmişten Sil",
                ["BadgeText"] = "METİN",
                ["BadgeImage"] = "RESİM",
                ["BadgeFile"] = "DOSYA",
                ["StatusCached"] = "Önbellekte Güvende",
                ["StatusOriginal"] = "Orijinal Konumunda",
                ["StatusDeleted"] = "Orijinal Silindi"
            },
            ["EN"] = new Dictionary<string, string>
            {
                ["AppSubtitle"] = "Clipboard Manager",
                ["SearchPlaceholder"] = "Search clipboard history...",
                ["TabAll"] = "All",
                ["TabText"] = "Text",
                ["TabImage"] = "Images",
                ["TabFile"] = "Files",
                ["TabPinned"] = "Pinned",
                ["StatusFormat"] = "Showing {1} of default {0} items",
                ["ClearAll"] = "Clear All",
                ["SettingsTitle"] = "ETDClip Settings",
                ["GlobalHotkey"] = "Global Shortcut Hotkey:",
                ["RecordHotkey"] = "Record Key",
                ["RecordingHotkey"] = "Press Hotkey Combination...",
                ["RecordingState"] = "Recording...",
                ["MaxHistoryItems"] = "Max Items to Display (Max 50):",
                ["MaxFileSizeMB"] = "Per-File Cache Limit (MB, Max 1024):",
                ["MaxFileSizeHint"] = "* Files smaller than the MB limit are cached to disk. Larger files remain in clipboard.",
                ["AutoCacheFiles"] = "Auto-cache small files in background",
                ["AutoStartWindows"] = "Launch automatically on Windows boot",
                ["ThemeLabel"] = "Application Theme:",
                ["ThemeSystem"] = "System Default",
                ["ThemeDark"] = "Dark Theme",
                ["ThemeLight"] = "Light Theme",
                ["Language"] = "Language / Dil:",
                ["SaveSettings"] = "Save Settings",
                ["AuthorLicense"] = "Developer: Emir Tuğra Dağ | License: MIT",
                ["EmptyHistory"] = "No items in clipboard history yet.\nWhen you copy text, images, or files, they will appear here.",
                ["ItemCopiedToast"] = "\"{0}\" Copied to Clipboard!",
                ["ClearPromptTitle"] = "Clear History",
                ["ClearPromptMsg"] = "Are you sure you want to clear all clipboard history?\n(Pinned items will be preserved)",
                ["SavedToast"] = "Settings saved successfully!",
                ["TrayShow"] = "Open ETDClip",
                ["TraySettings"] = "Settings",
                ["TrayClear"] = "Clear History",
                ["TrayExit"] = "Exit",
                ["TooltipCopy"] = "Copy to Clipboard Again",
                ["TooltipPin"] = "Pin to Top",
                ["TooltipUnpin"] = "Unpin Item",
                ["TooltipDelete"] = "Delete from History",
                ["BadgeText"] = "TEXT",
                ["BadgeImage"] = "IMAGE",
                ["BadgeFile"] = "FILE",
                ["StatusCached"] = "Safely Cached",
                ["StatusOriginal"] = "In Original Path",
                ["StatusDeleted"] = "Original Deleted"
            }
        };

        public static string GetString(string key, string lang = "TR")
        {
            string currentLang = lang == "EN" ? "EN" : "TR";
            if (Translations.TryGetValue(currentLang, out var dict) && dict.TryGetValue(key, out var val))
            {
                return val;
            }
            return key;
        }
    }
}
