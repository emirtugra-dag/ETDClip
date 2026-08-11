using System;
using System.Windows.Input;

namespace ETDClip.Services
{
    public class HotkeyManager
    {
        private const int HOTKEY_ID = 9001;
        private WindowMessageSink? _sink;
        public event EventHandler? HotKeyPressed;

        public bool RegisterHotkey(WindowMessageSink sink, string hotkeyStr)
        {
            _sink = sink;

            if (!ParseHotkey(hotkeyStr, out uint modifiers, out uint vk))
                return false;

            return _sink.RegisterHotkey(HOTKEY_ID, modifiers, vk, () =>
            {
                HotKeyPressed?.Invoke(this, EventArgs.Empty);
            });
        }

        public void Unregister()
        {
            _sink?.UnregisterHotkey(HOTKEY_ID);
        }

        public static bool ParseHotkey(string hotkeyStr, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(hotkeyStr)) return false;

            var parts = hotkeyStr.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= Win32Api.MOD_CONTROL; break;
                    case "ALT":
                        modifiers |= Win32Api.MOD_ALT; break;
                    case "SHIFT":
                        modifiers |= Win32Api.MOD_SHIFT; break;
                    case "WIN":
                    case "SUPER":
                        modifiers |= Win32Api.MOD_WIN; break;
                    default:
                        if (Enum.TryParse<Key>(part, true, out var key))
                            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        else if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                            vk = (uint)char.ToUpper(part[0]);
                        break;
                }
            }
            return vk != 0;
        }
    }
}
