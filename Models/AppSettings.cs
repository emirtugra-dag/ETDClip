namespace ETDClip.Models
{
    public class AppSettings
    {
        public string Language { get; set; } = "TR";
        public string Hotkey { get; set; } = "Alt+V";
        public int MaxHistoryItems { get; set; } = 10;
        public int MaxSingleFileSizeMB { get; set; } = 50;
        public bool AutoCacheFiles { get; set; } = true;
        public bool AutoStartWithWindows { get; set; } = false;
        public bool DarkMode { get; set; } = true;
        public string ThemeMode { get; set; } = "System"; // "System", "Dark", "Light"

        public static AppSettings CreateDefault() => new();
    }
}
