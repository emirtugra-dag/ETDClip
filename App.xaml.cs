using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using ETDClip.Models;
using ETDClip.Services;
// WPF vs WinForms ambiguity aliases
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ETDClip
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private AppSettings _settings = new();
        private NotifyIcon? _trayIcon;
        private readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ETDClip",
            "settings.json"
        );

        public AppSettings Settings => _settings;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            SetupCrashLogging();

            // Check for --uninstall command line argument
            if (e.Args.Length > 0 && e.Args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                PerformUninstallation();
                Shutdown();
                return;
            }

            // Load settings first so we know the correct language
            LoadSettings();

            const string mutexName = "Global\\ETDClip_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                string msg = _settings.Language == "EN"
                    ? "ETDClip is already running. You can open it by pressing your shortcut hotkey (e.g. Alt+V)."
                    : "ETDClip zaten çalışıyor. Kısayol tuşunuza (örn: Alt+V) basarak açabilirsiniz.";
                System.Windows.MessageBox.Show(
                    msg,
                    "ETDClip",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow(_settings);
            Current.MainWindow = mainWindow;

            SetupTrayIcon();

            bool isAutoStart    = e.Args.Any(a => a.Equals("--autostart",    StringComparison.OrdinalIgnoreCase));
            bool isOpenSettings = e.Args.Any(a => a.Equals("--open-settings", StringComparison.OrdinalIgnoreCase));

            if (!isAutoStart)
            {
                mainWindow.Show();
                mainWindow.Activate();
            }
            else
            {
                Win32Api.TrimProcessMemory();
            }

            if (isOpenSettings)
            {
                mainWindow.OpenSettingsModal();
            }
        }

        public void SetupTrayIcon()
        {
            // Load icon from embedded resource or file
            if (_trayIcon == null)
            {
                Icon? trayIco = null;
                try
                {
                    var asm = Assembly.GetExecutingAssembly();
                    using var s = asm.GetManifestResourceStream("ETDClip.Assets.app.ico");
                    if (s != null) trayIco = new Icon(s);
                }
                catch { }

                if (trayIco == null)
                {
                    try
                    {
                        string icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                        if (File.Exists(icoPath)) trayIco = new Icon(icoPath);
                    }
                    catch { }
                }

                _trayIcon = new NotifyIcon
                {
                    Icon    = trayIco ?? SystemIcons.Application,
                    Visible = true
                };

                // Double-click opens the window
                _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
            }

            var lang = _settings.Language;
            _trayIcon.Text = lang == "TR" ? "ETDClip - Pano Yöneticisi" : "ETDClip - Clipboard Manager";

            // Clean up old menu if exists
            var oldMenu = _trayIcon.ContextMenuStrip;
            if (oldMenu != null)
            {
                _trayIcon.ContextMenuStrip = null;
                oldMenu.Dispose();
            }

            var ctxMenu = new ContextMenuStrip();

            var miOpen = ctxMenu.Items.Add(LocalizationManager.GetString("TrayShow", lang));
            miOpen.Click += (s, e) => ShowMainWindow();

            var miClear = ctxMenu.Items.Add(LocalizationManager.GetString("TrayClear", lang));
            miClear.Click += (s, e) =>
            {
                if (Current.MainWindow is MainWindow mw)
                    mw.ClearHistoryPrompt();
            };

            ctxMenu.Items.Add(new ToolStripSeparator());

            var miExit = ctxMenu.Items.Add(LocalizationManager.GetString("TrayExit", lang));
            miExit.Click += (s, e) =>
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                Shutdown();
            };

            _trayIcon.ContextMenuStrip = ctxMenu;
        }

        private void SetupCrashLogging()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LogCrash(ex);
                }
            };
        }

        private void LogCrash(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETDClip");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string logFile = Path.Combine(dir, "crash.log");
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EXCEPTION: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n\n";
                File.AppendAllText(logFile, logContent);
            }
            catch { }
        }

        public void SaveSettings(AppSettings settings)
        {
            _settings = settings;
            try
            {
                string dir = Path.GetDirectoryName(_settingsPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ayarlar kaydedilemedi: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null) _settings = loaded;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ayarlar yüklenemedi: {ex.Message}");
                _settings = AppSettings.CreateDefault();
            }
        }

        public void ShowMainWindow()
        {
            if (Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.Topmost = true;
                if (!mainWindow.IsVisible) mainWindow.Show();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
                mainWindow.Focus();

                var handle = new System.Windows.Interop.WindowInteropHelper(mainWindow).EnsureHandle();
                Win32Api.ShowWindow(handle, Win32Api.SW_RESTORE);
                Win32Api.BringWindowToTop(handle);
                Win32Api.SetForegroundWindow(handle);

                mainWindow.Topmost = true;
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                _trayIcon?.Dispose();
                _mutex?.ReleaseMutex();
            }
            catch { }
        }

        private static void PerformUninstallation()
        {
            var res = System.Windows.MessageBox.Show(
                "ETDClip bilgisayarınızdan kaldırılıyor. Devam etmek istiyor musunuz?\n\nAre you sure you want to uninstall ETDClip?",
                "ETDClip Kaldırma Sihirbazı / Uninstaller",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (res != MessageBoxResult.Yes) return;

            try
            {
                string desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ETDClip.lnk");
                if (File.Exists(desktopShortcut)) File.Delete(desktopShortcut);

                string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "ETDClip.lnk");
                if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut);

                using (var runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    runKey?.DeleteValue("ETDClip", false);
                }

                using (var uninstallKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true))
                {
                    uninstallKey?.DeleteSubKeyTree("ETDClip", false);
                }

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETDClip");
                if (Directory.Exists(appData)) Directory.Delete(appData, recursive: true);

                string currentDir = AppContext.BaseDirectory;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 1 /nobreak & rmdir /s /q \"{currentDir.TrimEnd('\\')}\"",
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                System.Windows.MessageBox.Show("ETDClip başarıyla kaldırıldı / ETDClip was uninstalled successfully.", "ETDClip", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Kaldırma hatası / Uninstall error: {ex.Message}", "Hata / Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
