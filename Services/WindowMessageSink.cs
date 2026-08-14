using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ETDClip.Services
{
    /// <summary>
    /// Tek bir HwndSource hook üzerinden WM_CLIPBOARDUPDATE ve WM_HOTKEY mesajlarını
    /// dağıtan merkezi mesaj havuzu. Çift hook çakışmasını önler.
    /// </summary>
    public class WindowMessageSink : IDisposable
    {
        private HwndSource? _hwndSource;
        private readonly Dictionary<int, Action> _hotkeyCallbacks = new();
        
        public event Action? ClipboardChanged;
        public IntPtr Handle => _hwndSource?.Handle ?? IntPtr.Zero;

        public void Attach(Window window)
        {
            if (_hwndSource != null) return;

            var handle = new WindowInteropHelper(window).EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
            Win32Api.AddClipboardFormatListener(handle);
        }

        public void Detach()
        {
            if (_hwndSource == null) return;
            Win32Api.RemoveClipboardFormatListener(_hwndSource.Handle);
            foreach (var id in _hotkeyCallbacks.Keys)
                Win32Api.UnregisterHotKey(_hwndSource.Handle, id);
            _hotkeyCallbacks.Clear();
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        public bool RegisterHotkey(int id, uint modifiers, uint vk, Action callback)
        {
            if (_hwndSource == null) return false;
            Win32Api.UnregisterHotKey(_hwndSource.Handle, id);
            
            // Try with MOD_NOREPEAT first, and fallback to plain modifiers if needed
            bool ok = Win32Api.RegisterHotKey(_hwndSource.Handle, id, modifiers | Win32Api.MOD_NOREPEAT, vk);
            if (!ok)
            {
                ok = Win32Api.RegisterHotKey(_hwndSource.Handle, id, modifiers, vk);
            }
            
            if (ok) _hotkeyCallbacks[id] = callback;
            return ok;
        }

        public void UnregisterHotkey(int id)
        {
            if (_hwndSource == null) return;
            Win32Api.UnregisterHotKey(_hwndSource.Handle, id);
            _hotkeyCallbacks.Remove(id);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Api.WM_CLIPBOARDUPDATE)
            {
                ClipboardChanged?.Invoke();
                // not marking handled — let others see it too
            }
            else if (msg == Win32Api.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_hotkeyCallbacks.TryGetValue(id, out var callback))
                {
                    callback();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose() => Detach();
    }
}
