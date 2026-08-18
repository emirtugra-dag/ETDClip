using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using FontFamily = System.Windows.Media.FontFamily;
using ETDClip.Models;
using ETDClip.Services;

namespace ETDClip
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private readonly HistoryManager _historyManager;
        private readonly FileCacheManager _cacheManager;
        private readonly ClipboardMonitor _clipboardMonitor;
        private readonly HotkeyManager _hotkeyManager;
        private readonly WindowMessageSink _messageSink;

        private string _selectedCategory = "All";
        private string _currentSearchQuery = string.Empty;
        private bool _isRecordingHotkey = false;
        private bool _isInitialized = false;

        // ─── Theme color tokens (updated by ApplyTheme) ─────────────────────
        private Color _colorBackground    = Color.FromRgb(0x0F, 0x17, 0x2A);
        private Color _colorSurface       = Color.FromRgb(0x1E, 0x29, 0x3B);
        private Color _colorSurfaceHover  = Color.FromRgb(0x28, 0x38, 0x52);
        private Color _colorBorder        = Color.FromRgb(0x33, 0x47, 0x55);
        private Color _colorTextPrimary   = Color.FromRgb(0xF8, 0xFA, 0xFC);
        private Color _colorTextSecondary = Color.FromRgb(0x94, 0xA3, 0xB8);
        private Color _colorAccent        = Color.FromRgb(0x3B, 0x82, 0xF6);

        public MainWindow(AppSettings settings)
        {
            _settings = settings;

            _messageSink = new WindowMessageSink();
            _cacheManager = new FileCacheManager();
            _historyManager = new HistoryManager();
            _clipboardMonitor = new ClipboardMonitor(_historyManager, _cacheManager, _settings);
            _hotkeyManager = new HotkeyManager();

            _historyManager.HistoryUpdated += (s, e) => Dispatcher.InvokeAsync(RenderHistoryItems);
            _historyManager.ItemRemoved += (s, item) => _cacheManager.DeleteCacheItem(item);
            _hotkeyManager.HotKeyPressed += (s, e) => Dispatcher.InvokeAsync(ToggleWindowVisibility);
            _messageSink.ClipboardChanged += () => Dispatcher.InvokeAsync(_clipboardMonitor.OnClipboardChanged);

            InitializeComponent();
            _isInitialized = true;

            // Immediately create Win32 HWND and attach clipboard & hotkey hooks
            // so the app monitors clipboard and responds to Alt+V right from Windows boot
            InitializeHandleAndHooks();
            LoadAssetsSafely();
            ApplyThemeMode();
            ApplyLocalization();
        }

        private bool _isHooked = false;

        public void InitializeHandleAndHooks()
        {
            if (_isHooked) return;

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                var handle = helper.EnsureHandle();

                _messageSink.Attach(this);
                RegisterCurrentHotkey();

                // Hook for system settings changes (theme changes)
                var hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
                hwndSource?.AddHook(HwndSourceHook);

                _isHooked = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeHandleAndHooks error: {ex.Message}");
            }
        }

        // SourceInitialized fires when HWND is ready
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            InitializeHandleAndHooks();
        }

        private IntPtr HwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SETTINGCHANGE = 0x001A;
            if (msg == WM_SETTINGCHANGE && _settings.ThemeMode == "System")
            {
                string? area = System.Runtime.InteropServices.Marshal.PtrToStringAuto(lParam);
                if (area == "ImmersiveColorSet" || area == "ImmersiveColor")
                {
                    Dispatcher.InvokeAsync(() => ApplyThemeMode());
                }
            }
            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAssetsSafely();
            ApplyThemeMode();
            ApplyLocalization();
            RenderHistoryItems();
        }

        public void ApplyThemeMode()
        {
            bool isDark = true;
            if (_settings.ThemeMode == "Dark")
            {
                isDark = true;
            }
            else if (_settings.ThemeMode == "Light")
            {
                isDark = false;
            }
            else // "System"
            {
                isDark = IsSystemDarkTheme();
            }
            ApplyTheme(isDark);
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                if (val is int lightThemeVal)
                {
                    return lightThemeVal == 0;
                }
            }
            catch { }
            return true; // Default to dark mode
        }

        private void LoadAssetsSafely()
        {
            // Try to load icon + logo from embedded resources first,
            // then fall back to AppContext.BaseDirectory/Assets/
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                // Embedded resource name: ETDClip.Assets.app.ico
                using var icoStream = asm.GetManifestResourceStream("ETDClip.Assets.app.ico");
                if (icoStream != null)
                {
                    Icon = new BitmapImage();
                    // WPF Icon expects BitmapImage or IconBitmapDecoder
                    // Use IconBitmapDecoder for .ico
                    var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                        icoStream,
                        BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad);
                    Icon = decoder.Frames[0];
                }
            }
            catch { }

            // Fallback: file system
            if (Icon == null)
            {
                try
                {
                    string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                    if (File.Exists(iconPath))
                    {
                        var iconDecoder = new System.Windows.Media.Imaging.IconBitmapDecoder(
                            new Uri(iconPath), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        Icon = iconDecoder.Frames[0];
                    }
                }
                catch { }
            }

            // Logo image
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var logoStream = asm.GetManifestResourceStream("ETDClip.Assets.etdclip_logo.png");
                if (logoStream != null)
                {
                    var logoBmp = new BitmapImage();
                    logoBmp.BeginInit();
                    logoBmp.StreamSource = logoStream;
                    logoBmp.DecodePixelHeight = 72;
                    logoBmp.CacheOption = BitmapCacheOption.OnLoad;
                    logoBmp.EndInit();
                    logoBmp.Freeze();
                    ImgHeaderLogo.Source = logoBmp;
                }
            }
            catch { }

            if (ImgHeaderLogo.Source == null)
            {
                try
                {
                    string logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "etdclip_logo.png");
                    if (File.Exists(logoPath))
                    {
                        var logoBmp = new BitmapImage();
                        logoBmp.BeginInit();
                        logoBmp.UriSource = new Uri(logoPath);
                        logoBmp.DecodePixelHeight = 72;
                        logoBmp.CacheOption = BitmapCacheOption.OnLoad;
                        logoBmp.EndInit();
                        logoBmp.Freeze();
                        ImgHeaderLogo.Source = logoBmp;
                    }
                }
                catch { }
            }
        }

        // ─── THEME ───────────────────────────────────────────────────────────

        private void ApplyTheme(bool dark)
        {
            if (dark)
            {
                _colorBackground    = Color.FromRgb(0x0F, 0x17, 0x2A);
                _colorSurface       = Color.FromRgb(0x1E, 0x29, 0x3B);
                _colorSurfaceHover  = Color.FromRgb(0x28, 0x38, 0x52);
                _colorBorder        = Color.FromRgb(0x33, 0x47, 0x55);
                _colorTextPrimary   = Color.FromRgb(0xF8, 0xFA, 0xFC);
                _colorTextSecondary = Color.FromRgb(0x94, 0xA3, 0xB8);
                _colorAccent        = Color.FromRgb(0x3B, 0x82, 0xF6);
            }
            else
            {
                _colorBackground    = Color.FromRgb(0xF1, 0xF5, 0xF9);
                _colorSurface       = Color.FromRgb(0xFF, 0xFF, 0xFF);
                _colorSurfaceHover  = Color.FromRgb(0xE2, 0xE8, 0xF0);
                _colorBorder        = Color.FromRgb(0xCB, 0xD5, 0xE1);
                _colorTextPrimary   = Color.FromRgb(0x0F, 0x17, 0x2A);
                _colorTextSecondary = Color.FromRgb(0x47, 0x60, 0x82);
                _colorAccent        = Color.FromRgb(0x25, 0x63, 0xEB);
            }

            // Update C# resource dictionaries for all dynamic resource bindings
            Resources["BgBrush"]            = new SolidColorBrush(_colorBackground);
            Resources["SurfaceBrush"]       = new SolidColorBrush(_colorSurface);
            Resources["SurfaceHoverBrush"]  = new SolidColorBrush(_colorSurfaceHover);
            Resources["BorderBrush"]        = new SolidColorBrush(_colorBorder);
            Resources["TextPrimaryBrush"]   = new SolidColorBrush(_colorTextPrimary);
            Resources["TextSecondaryBrush"] = new SolidColorBrush(_colorTextSecondary);
            Resources["AccentBrush"]        = new SolidColorBrush(_colorAccent);
            Resources["AccentHoverBrush"]   = new SolidColorBrush(dark ? Color.FromRgb(0x60, 0xA5, 0xFA) : Color.FromRgb(0x3B, 0x82, 0xF6));
            Resources["ModalOverlayBrush"]  = new SolidColorBrush(dark ? Color.FromArgb(0xD0, 0x09, 0x0E, 0x17) : Color.FromArgb(0xD0, 0xE2, 0xE8, 0xF0));

            // Re-render history with updated colors
            if (_isInitialized) RenderHistoryItems();
        }

        // ─── WINDOW LIFECYCLE ─────────────────────────────────────────────────

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            HideWindowToTray();
        }

        protected override void OnClosed(EventArgs e)
        {
            _messageSink.Detach();
            base.OnClosed(e);
        }

        public void ToggleWindowVisibility()
        {
            if (IsVisible)
            {
                HideWindowToTray();
            }
            else
            {
                _historyManager.ValidateAndPurgeMissingFiles(_cacheManager);

                Topmost = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();

                var handle = new WindowInteropHelper(this).EnsureHandle();
                Win32Api.ShowWindow(handle, Win32Api.SW_RESTORE);
                Win32Api.BringWindowToTop(handle);
                Win32Api.SetForegroundWindow(handle);

                Topmost = true;

                RenderHistoryItems();
            }
        }

        private void HideWindowToTray()
        {
            Hide();
            Win32Api.TrimProcessMemory();
        }

        private void RegisterCurrentHotkey()
        {
            if (TxtHotkeyBadge != null) TxtHotkeyBadge.Text = _settings.Hotkey;
            bool success = _hotkeyManager.RegisterHotkey(_messageSink, _settings.Hotkey);
            if (!success)
                System.Diagnostics.Debug.WriteLine($"Hotkey kayıt başarısız: {_settings.Hotkey}");
        }

        // ─── LOCALIZATION ─────────────────────────────────────────────────────

        public void ApplyLocalization()
        {
            if (!_isInitialized) return;
            string lang = _settings.Language;

            if (TxtAppSubtitle != null)     TxtAppSubtitle.Text     = LocalizationManager.GetString("AppSubtitle", lang);
            if (TxtSearchPlaceholder != null) TxtSearchPlaceholder.Text = LocalizationManager.GetString("SearchPlaceholder", lang);

            if (TabAll   != null) TabAll.Content   = LocalizationManager.GetString("TabAll",   lang);
            if (TabText  != null) TabText.Content  = LocalizationManager.GetString("TabText",  lang);
            if (TabImage != null) TabImage.Content = LocalizationManager.GetString("TabImage", lang);
            if (TabFile  != null) TabFile.Content  = LocalizationManager.GetString("TabFile",  lang);
            if (TabPinned != null) TabPinned.Content = LocalizationManager.GetString("TabPinned", lang);

            if (TxtClearAllLabel  != null) TxtClearAllLabel.Text  = LocalizationManager.GetString("ClearAll",      lang);
            if (TxtSettingsTitle  != null) TxtSettingsTitle.Text  = LocalizationManager.GetString("SettingsTitle", lang);
            if (LblLanguage       != null) LblLanguage.Text       = LocalizationManager.GetString("Language",      lang);
            if (LblGlobalHotkey   != null) LblGlobalHotkey.Text   = LocalizationManager.GetString("GlobalHotkey",  lang);
            if (TxtRecordHotkeyLabel  != null) TxtRecordHotkeyLabel.Text  = LocalizationManager.GetString("RecordHotkey", lang);
            if (LblMaxHistoryItems != null) LblMaxHistoryItems.Text = LocalizationManager.GetString("MaxHistoryItems", lang);
            if (LblMaxFileSizeMB  != null) LblMaxFileSizeMB.Text  = LocalizationManager.GetString("MaxFileSizeMB", lang);
            if (TxtMaxFileSizeHint != null) TxtMaxFileSizeHint.Text = LocalizationManager.GetString("MaxFileSizeHint", lang);
            if (ChkAutoCache      != null) ChkAutoCache.Content   = LocalizationManager.GetString("AutoCacheFiles",  lang);
            if (ChkAutostart      != null) ChkAutostart.Content   = LocalizationManager.GetString("AutoStartWindows", lang);
            if (LblTheme          != null) LblTheme.Text          = LocalizationManager.GetString("ThemeLabel",      lang);
            if (CmbTheme != null && CmbTheme.Items.Count >= 3)
            {
                if (CmbTheme.Items[0] is ComboBoxItem itemSystem) itemSystem.Content = LocalizationManager.GetString("ThemeSystem", lang);
                if (CmbTheme.Items[1] is ComboBoxItem itemDark)   itemDark.Content   = LocalizationManager.GetString("ThemeDark",   lang);
                if (CmbTheme.Items[2] is ComboBoxItem itemLight)  itemLight.Content  = LocalizationManager.GetString("ThemeLight",  lang);
            }
            if (TxtSaveSettingsLabel != null) TxtSaveSettingsLabel.Text = LocalizationManager.GetString("SaveSettings", lang);
            if (TxtAuthorLicense  != null)
            {
                string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.2";
                string authorLicense = LocalizationManager.GetString("AuthorLicense", lang);
                TxtAuthorLicense.Text = $"ETDClip v{ver}  •  {authorLicense}";
            }
        }

        // ─── HISTORY RENDERING ────────────────────────────────────────────────

        private void RenderHistoryItems()
        {
            if (!_isInitialized || PnlHistoryList == null || _historyManager == null || _settings == null)
                return;

            PnlHistoryList.Children.Clear();
            string lang = _settings.Language;

            var items = _historyManager.GetItems(_settings.MaxHistoryItems, _currentSearchQuery, _selectedCategory);
            string fmt = LocalizationManager.GetString("StatusFormat", lang);
            if (TxtStatus != null) TxtStatus.Text = string.Format(fmt, _settings.MaxHistoryItems, items.Count);

            if (!items.Any())
            {
                var emptyBorder = new Border
                {
                    Background    = new SolidColorBrush(_colorSurface),
                    BorderBrush   = new SolidColorBrush(_colorBorder),
                    BorderThickness = new Thickness(1),
                    CornerRadius  = new CornerRadius(12),
                    Padding       = new Thickness(20),
                    Margin        = new Thickness(0, 10, 0, 0)
                };
                emptyBorder.Child = new TextBlock
                {
                    Text          = LocalizationManager.GetString("EmptyHistory", lang),
                    Foreground    = new SolidColorBrush(_colorTextSecondary),
                    TextAlignment = TextAlignment.Center,
                    FontSize      = 13,
                    LineHeight    = 22
                };
                PnlHistoryList.Children.Add(emptyBorder);
                return;
            }

            int index = 1;
            foreach (var item in items)
            {
                PnlHistoryList.Children.Add(CreateItemCard(item, index++));
            }

            Win32Api.TrimProcessMemory();
        }

        private UIElement CreateItemCard(ClipboardItem item, int index)
        {
            string lang = _settings.Language;

            var border = new Border
            {
                Background      = new SolidColorBrush(_colorSurface),
                BorderBrush     = item.IsPinned
                                  ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                                  : new SolidColorBrush(_colorBorder),
                BorderThickness = new Thickness(item.IsPinned ? 2 : 1),
                CornerRadius    = new CornerRadius(12),
                Margin          = new Thickness(0, 0, 0, 8),
                Padding         = new Thickness(12),
                Opacity         = 0
            };

            border.MouseEnter += (s, e) => border.Background = new SolidColorBrush(_colorSurfaceHover);
            border.MouseLeave += (s, e) => border.Background = new SolidColorBrush(_colorSurface);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150 + (index * 20)));
            border.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Index badge
            var badgeBorder = new Border
            {
                Width           = 28,
                Height          = 28,
                CornerRadius    = new CornerRadius(8),
                Background      = item.IsPinned
                                  ? new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))
                                  : new SolidColorBrush(Color.FromArgb(0x33, _colorAccent.R, _colorAccent.G, _colorAccent.B)),
                BorderBrush     = new SolidColorBrush(_colorBorder),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            badgeBorder.Child = new TextBlock
            {
                Text              = $"#{index}",
                Foreground        = item.IsPinned ? new SolidColorBrush(Colors.Black) : new SolidColorBrush(_colorAccent),
                FontWeight        = FontWeights.Bold,
                FontSize          = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badgeBorder, 0);
            grid.Children.Add(badgeBorder);

            // Content
            var contentStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var metaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

            string badgeLabel = item.Type switch
            {
                ClipboardItemType.Text  => LocalizationManager.GetString("BadgeText",  lang),
                ClipboardItemType.Image => LocalizationManager.GetString("BadgeImage", lang),
                ClipboardItemType.File  => LocalizationManager.GetString("BadgeFile",  lang),
                _ => ""
            };
            metaStack.Children.Add(new TextBlock
            {
                Text       = badgeLabel,
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(_colorTextSecondary),
                Margin     = new Thickness(0, 0, 8, 0)
            });
            metaStack.Children.Add(new TextBlock
            {
                Text       = item.FormattedTime,
                FontSize   = 10,
                Foreground = new SolidColorBrush(_colorTextSecondary)
            });

            if (item.Type == ClipboardItemType.File)
            {
                string statusText = (item.IsCached && item.CachedFilePaths?.Any(File.Exists) == true)
                    ? LocalizationManager.GetString("StatusCached",   lang)
                    : (item.OriginalFilesExist
                        ? LocalizationManager.GetString("StatusOriginal", lang)
                        : LocalizationManager.GetString("StatusDeleted",  lang));

                metaStack.Children.Add(new TextBlock
                {
                    Text       = $" • {statusText}",
                    FontSize   = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = item.IsCached
                                 ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
                                 : new SolidColorBrush(_colorTextSecondary),
                    Margin     = new Thickness(8, 0, 0, 0)
                });
            }
            contentStack.Children.Add(metaStack);

            if (item.Type == ClipboardItemType.Text)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text        = item.DisplayTitle,
                    Foreground  = new SolidColorBrush(_colorTextPrimary),
                    FontSize    = 13,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight   = 60,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontFamily  = new FontFamily("Segoe UI, Consolas")
                });
            }
            else if (item.Type == ClipboardItemType.Image && !string.IsNullOrEmpty(item.ImagePath) && File.Exists(item.ImagePath))
            {
                try
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Height             = 80,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin             = new Thickness(0, 4, 0, 4),
                        Stretch            = Stretch.Uniform
                    };
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource        = new Uri(item.ImagePath);
                    bmp.DecodePixelHeight = 80;
                    bmp.CacheOption      = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    img.Source = bmp;
                    contentStack.Children.Add(img);
                }
                catch
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text       = "[Görsel Yüklenemedi / Image Load Failed]",
                        Foreground = new SolidColorBrush(Colors.OrangeRed),
                        FontSize   = 12
                    });
                }
            }
            else if (item.Type == ClipboardItemType.File)
            {
                contentStack.Children.Add(new TextBlock
                {
                    Text       = item.DisplayTitle,
                    Foreground = new SolidColorBrush(_colorAccent),
                    FontWeight = FontWeights.SemiBold,
                    FontSize   = 13
                });
            }

            contentStack.Children.Add(new TextBlock
            {
                Text       = item.GetSubtitle(lang),
                FontSize   = 11,
                Foreground = new SolidColorBrush(_colorTextSecondary),
                Margin     = new Thickness(0, 4, 0, 0)
            });

            Grid.SetColumn(contentStack, 1);
            grid.Children.Add(contentStack);

            // Action buttons
            var actionStack = new StackPanel
            {
                Orientation       = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(10, 0, 0, 0)
            };

            // Create icon button using Segoe MDL2 Assets (no emojis)
            Button MakeActionBtn(string mdl2Char, string tooltipKey)
            {
                var btn = new Button
                {
                    Style   = (Style)FindResource("GlassButtonStyle"),
                    ToolTip = LocalizationManager.GetString(tooltipKey, lang),
                    Margin  = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(7, 5, 7, 5),
                    Content = new TextBlock
                    {
                        Text       = mdl2Char,
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize   = 13,
                        Foreground = new SolidColorBrush(_colorTextSecondary)
                    }
                };
                return btn;
            }

            // Copy: &#xE8C8;
            var btnCopy = MakeActionBtn("\uE8C8", "TooltipCopy");
            btnCopy.Click += (s, e) =>
            {
                _clipboardMonitor.CopyItemToClipboard(item);
                string fmt = LocalizationManager.GetString("ItemCopiedToast", lang);
                if (TxtStatus != null) TxtStatus.Text = string.Format(fmt, item.DisplayTitle);
            };
            actionStack.Children.Add(btnCopy);

            // Pin / Unpin: &#xE840; pinned, &#xE718; unpin
            var btnPin = MakeActionBtn(item.IsPinned ? "\uE77A" : "\uE840",
                                       item.IsPinned ? "TooltipUnpin" : "TooltipPin");
            btnPin.Click += (s, e) => _historyManager.TogglePin(item.Id);
            actionStack.Children.Add(btnPin);

            // Delete: &#xE74D;
            var btnDel = MakeActionBtn("\uE74D", "TooltipDelete");
            btnDel.Click += (s, e) => _historyManager.RemoveItem(item.Id);
            actionStack.Children.Add(btnDel);

            Grid.SetColumn(actionStack, 2);
            grid.Children.Add(actionStack);

            border.Child = grid;
            return border;
        }

        // ─── EVENT HANDLERS ───────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            _currentSearchQuery = TxtSearch.Text;
            if (TxtSearchPlaceholder != null)
                TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Visible : Visibility.Collapsed;
            if (BtnClearSearch != null)
                BtnClearSearch.Visibility = string.IsNullOrEmpty(_currentSearchQuery) ? Visibility.Collapsed : Visibility.Visible;
            RenderHistoryItems();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSearch != null) TxtSearch.Text = string.Empty;
        }

        private void CategoryTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (sender is RadioButton btn && btn.Tag is string tag)
            {
                _selectedCategory = tag;
                RenderHistoryItems();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => HideWindowToTray();

        public void ClearHistoryPrompt()
        {
            string lang = _settings.Language;
            var res = System.Windows.MessageBox.Show(
                LocalizationManager.GetString("ClearPromptMsg",   lang),
                LocalizationManager.GetString("ClearPromptTitle", lang),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _historyManager.ClearHistory(keepPinned: true);
                _cacheManager.ClearCache();
                RenderHistoryItems();
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e) => ClearHistoryPrompt();

        public void OpenSettingsModal()
        {
            if (!_isInitialized) return;
            if (TxtSettingHotkey      != null) TxtSettingHotkey.Text      = _settings.Hotkey;
            if (TxtSettingMaxItems    != null) TxtSettingMaxItems.Text    = _settings.MaxHistoryItems.ToString();
            if (TxtSettingMaxSingleFileMB != null) TxtSettingMaxSingleFileMB.Text = _settings.MaxSingleFileSizeMB.ToString();
            if (ChkAutoCache          != null) ChkAutoCache.IsChecked     = _settings.AutoCacheFiles;
            if (ChkAutostart          != null) ChkAutostart.IsChecked     = AutoStartManager.IsAutoStartEnabled();
            if (CmbLanguage           != null) CmbLanguage.SelectedIndex  = _settings.Language == "EN" ? 1 : 0;
            if (CmbTheme != null)
            {
                if (_settings.ThemeMode == "System") CmbTheme.SelectedIndex = 0;
                else if (_settings.ThemeMode == "Dark") CmbTheme.SelectedIndex = 1;
                else CmbTheme.SelectedIndex = 2; // "Light"
            }
            if (ModalSettings         != null) ModalSettings.Visibility   = Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            if (ModalSettings != null) ModalSettings.Visibility = Visibility.Collapsed;
            _isRecordingHotkey = false;
            if (TxtRecordHotkeyLabel != null)
                TxtRecordHotkeyLabel.Text = LocalizationManager.GetString("RecordHotkey", _settings.Language);
        }

        private void ModalSettings_BackgroundClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender) BtnCloseSettings_Click(sender, e);
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsModal();

        private void BtnRecordHotkey_Click(object sender, RoutedEventArgs e)
        {
            string lang = _settings.Language;
            _isRecordingHotkey = !_isRecordingHotkey;
            if (_isRecordingHotkey)
            {
                if (TxtRecordHotkeyLabel != null) TxtRecordHotkeyLabel.Text = LocalizationManager.GetString("RecordingHotkey", lang);
                if (TxtSettingHotkey    != null) TxtSettingHotkey.Text     = LocalizationManager.GetString("RecordingState",  lang);
                PreviewKeyDown += MainWindow_PreviewKeyDown;
            }
            else
            {
                if (TxtRecordHotkeyLabel != null) TxtRecordHotkeyLabel.Text = LocalizationManager.GetString("RecordHotkey", lang);
                PreviewKeyDown -= MainWindow_PreviewKeyDown;
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isRecordingHotkey) return;
            e.Handled = true;
            Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            var parts = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(key.ToString());

            string hotkeyStr = string.Join("+", parts);
            if (TxtSettingHotkey != null) TxtSettingHotkey.Text = hotkeyStr;
            _isRecordingHotkey = false;
            if (TxtRecordHotkeyLabel != null)
                TxtRecordHotkeyLabel.Text = LocalizationManager.GetString("RecordHotkey", _settings.Language);
            PreviewKeyDown -= MainWindow_PreviewKeyDown;
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSettingMaxItems != null && int.TryParse(TxtSettingMaxItems.Text, out int maxItems))
            {
                if (maxItems < 1) maxItems = 1;
                if (maxItems > 50) maxItems = 50;
                _settings.MaxHistoryItems = maxItems;
                TxtSettingMaxItems.Text = maxItems.ToString();
            }

            if (TxtSettingMaxSingleFileMB != null && int.TryParse(TxtSettingMaxSingleFileMB.Text, out int maxMB))
            {
                if (maxMB < 1) maxMB = 1;
                if (maxMB > 1024) maxMB = 1024; // 1GB limit
                _settings.MaxSingleFileSizeMB = maxMB;
                TxtSettingMaxSingleFileMB.Text = maxMB.ToString();
            }

            if (ChkAutoCache != null)  _settings.AutoCacheFiles = ChkAutoCache.IsChecked ?? true;
            if (TxtSettingHotkey != null) _settings.Hotkey = TxtSettingHotkey.Text;

            if (CmbLanguage != null && CmbLanguage.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string langTag)
                _settings.Language = langTag;

            bool autostart = ChkAutostart?.IsChecked ?? false;
            _settings.AutoStartWithWindows = autostart;
            AutoStartManager.SetAutoStart(autostart);

            if (CmbTheme != null && CmbTheme.SelectedItem is ComboBoxItem selectedTheme && selectedTheme.Tag is string themeTag)
            {
                _settings.ThemeMode = themeTag;
                _settings.DarkMode = themeTag == "Dark" || (themeTag == "System" && IsSystemDarkTheme());
            }

            var appInstance = System.Windows.Application.Current as App;
            appInstance?.SaveSettings(_settings);
            appInstance?.SetupTrayIcon();
            _clipboardMonitor.UpdateSettings(_settings);
            RegisterCurrentHotkey();
            ApplyThemeMode();
            ApplyLocalization();
            if (ModalSettings != null) ModalSettings.Visibility = Visibility.Collapsed;
            RenderHistoryItems();

            System.Windows.MessageBox.Show(
                LocalizationManager.GetString("SavedToast", _settings.Language),
                "ETDClip",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
