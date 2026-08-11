using System;
using System.IO;
using Microsoft.Win32;

namespace ETDClip.Services
{
    public static class AutoStartManager
    {
        private const string AppName = "ETDClip";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var value = key?.GetValue(AppName);
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null) return;

                if (enable)
                {
                    string executablePath = Environment.ProcessPath 
                        ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName 
                        ?? Path.Combine(AppContext.BaseDirectory, "ETDClip.exe");

                    key.SetValue(AppName, $"\"{executablePath}\" --autostart");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Otomatik başlatma registry hatası: {ex.Message}");
            }
        }
    }
}
