using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ETDClipSetup
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            using (var langForm = new LanguageSelectionForm())
            {
                if (langForm.ShowDialog() == DialogResult.OK)
                {
                    Application.Run(new SetupForm(langForm.SelectedLanguage));
                }
            }
        }
    }

    // ─── Rounded panel helper ─────────────────────────────────────────────────
    public class RoundedPanel : Panel
    {
        public int Radius { get; set; } = 12;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var path = RoundRect(ClientRectangle, Radius);
            using var brush = new SolidBrush(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // suppress default — we draw our own
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }
        private static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
            path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
            path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ─── Modern flat button ───────────────────────────────────────────────────
    public class FlatBtn : Button
    {
        public Color HoverColor { get; set; }
        public Color NormalColor { get; set; }

        public FlatBtn()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { BackColor = HoverColor;  base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { BackColor = NormalColor; base.OnMouseLeave(e); }
    }

    // ─── Language Selection Form ──────────────────────────────────────────────
    public class LanguageSelectionForm : Form
    {
        public string SelectedLanguage { get; private set; } = "EN";

        public LanguageSelectionForm()
        {
            Text            = "ETDClip Setup / Kurulum";
            ClientSize      = new Size(360, 150);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox     = false;
            MaximizeBox     = false;
            BackColor       = Color.FromArgb(10, 15, 30);
            ForeColor       = Color.FromArgb(248, 250, 252);
            Font            = new Font("Segoe UI", 9.5f);

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("ETDClipSetup.app.ico");
                if (s != null) Icon = new Icon(s);
            }
            catch { }

            var lblPrompt = new Label
            {
                Text      = "Please select language / Lütfen dil seçiniz:",
                Location  = new Point(20, 20),
                Size      = new Size(320, 25),
                ForeColor = Color.FromArgb(148, 163, 184),
                Font      = new Font("Segoe UI", 10f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblPrompt);

            var btnTr = new FlatBtn
            {
                Text        = "Türkçe",
                Location    = new Point(20, 65),
                Size        = new Size(150, 45),
                BackColor   = Color.FromArgb(59, 130, 246),
                NormalColor = Color.FromArgb(59, 130, 246),
                HoverColor  = Color.FromArgb(37, 99, 235),
                ForeColor   = Color.White
            };
            btnTr.Click += (s, e) =>
            {
                SelectedLanguage = "TR";
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnTr);

            var btnEn = new FlatBtn
            {
                Text        = "English",
                Location    = new Point(190, 65),
                Size        = new Size(150, 45),
                BackColor   = Color.FromArgb(25, 40, 65),
                NormalColor = Color.FromArgb(25, 40, 65),
                HoverColor  = Color.FromArgb(18, 28, 48),
                ForeColor   = Color.FromArgb(248, 250, 252)
            };
            btnEn.Click += (s, e) =>
            {
                SelectedLanguage = "EN";
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnEn);
        }
    }

    // ─── Setup form ───────────────────────────────────────────────────────────
    public class SetupForm : Form
    {
        // Colors
        static readonly Color C_BG        = Color.FromArgb(10,  15,  30);
        static readonly Color C_SURFACE   = Color.FromArgb(18,  28,  48);
        static readonly Color C_SURFACE2  = Color.FromArgb(25,  40,  65);
        static readonly Color C_ACCENT    = Color.FromArgb(59,  130, 246);
        static readonly Color C_ACCENT_H  = Color.FromArgb(37,  99,  235);
        static readonly Color C_TEXT      = Color.FromArgb(248, 250, 252);
        static readonly Color C_TEXTMUTED = Color.FromArgb(148, 163, 184);
        static readonly Color C_BORDER    = Color.FromArgb(40,  60,  90);
        static readonly Color C_SUCCESS   = Color.FromArgb(34,  197, 94);
        static readonly Color C_DANGER    = Color.FromArgb(239, 68,  68);

        private readonly string _lang;
        private bool _isUpdate;
        private bool _isInstalling;

        // Controls
        private TextBox    _txtDir     = null!;
        private Label      _lblStatus  = null!;
        private ProgressBar _bar       = null!;
        private FlatBtn    _btnInstall = null!;
        private FlatBtn    _btnCancel  = null!;
        private CheckBox   _chkDesktop = null!;
        private CheckBox   _chkStart   = null!;
        private CheckBox   _chkAuto    = null!;
        private CheckBox   _chkRun     = null!;
        private Label      _lblStep    = null!;

        private static string DefaultDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "ETDClip");

        private string InstallDir => _txtDir?.Text.Trim() is { Length: > 0 } s ? s : DefaultDir;

        // ─── Localization ─────────────────────────────────────────────────────
        private string TR(string tr, string en) => _lang == "TR" ? tr : en;

        public SetupForm(string lang)
        {
            _lang = lang;
            _isUpdate = File.Exists(Path.Combine(DefaultDir, "ETDClip.exe"));

            SuspendLayout();
            BuildForm();
            ResumeLayout(true);
        }

        // ─── UI construction ──────────────────────────────────────────────────
        private void BuildForm()
        {
            Text            = TR("ETDClip Kurulum", "ETDClip Setup");
            ClientSize      = new Size(520, 500);
            StartPosition   = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            BackColor       = C_BG;
            ForeColor       = C_TEXT;
            Font            = new Font("Segoe UI", 9.5f);

            // Window icon
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("ETDClipSetup.app.ico");
                if (s != null) Icon = new Icon(s);
            }
            catch { }

            int y = 0;

            // ── Header ──────────────────────────────────────────────────────
            var header = new Panel
            {
                BackColor = C_SURFACE,
                Dock      = DockStyle.Top,
                Height    = 88
            };
            Controls.Add(header);
            header.Paint += (s, e) =>
            {
                using var pen = new Pen(C_BORDER, 1);
                e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            var pic = new PictureBox
            {
                Size     = new Size(54, 54),
                Location = new Point(22, 17),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("ETDClipSetup.etdclip_logo.png");
                if (s != null) pic.Image = Image.FromStream(s);
            }
            catch { }
            header.Controls.Add(pic);

            header.Controls.Add(new Label
            {
                Text      = "ETDClip",
                Font      = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = C_TEXT,
                Location  = new Point(88, 14),
                AutoSize  = true
            });

            string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.2";
            string versionLine = TR(
                _isUpdate ? $"Güncelleme  •  v{ver}  •  Emir Tuğra Dağ"
                           : $"Yeni Kurulum  •  v{ver}  •  Emir Tuğra Dağ",
                _isUpdate ? $"Update  •  v{ver}  •  Emir Tuğra Dağ"
                           : $"New Install  •  v{ver}  •  Emir Tuğra Dağ");

            header.Controls.Add(new Label
            {
                Text      = versionLine,
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = C_TEXTMUTED,
                Location  = new Point(90, 50),
                AutoSize  = true
            });

            y = 104;

            // ── Install directory ────────────────────────────────────────────
            AddSectionLabel(TR("Kurulum Klasoru", "Installation Folder"), ref y);
            y += 4;

            var dirRow = new Panel { Location = new Point(22, y), Size = new Size(476, 34), BackColor = C_BG };
            Controls.Add(dirRow);

            _txtDir = new TextBox
            {
                Location    = new Point(0, 4),
                Size        = new Size(370, 26),
                BackColor   = C_SURFACE2,
                ForeColor   = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Text        = DefaultDir
            };
            dirRow.Controls.Add(_txtDir);

            var btnBrowse = new FlatBtn
            {
                Text        = TR("Gozat", "Browse"),
                Location    = new Point(376, 2),
                Size        = new Size(100, 30),
                BackColor   = C_SURFACE2,
                NormalColor = C_SURFACE2,
                HoverColor  = C_SURFACE,
                ForeColor   = C_TEXT,
                Font        = new Font("Segoe UI", 9f)
            };
            btnBrowse.Click += (s, e) =>
            {
                using var dlg = new FolderBrowserDialog
                    { Description = TR("Kurulum klasorunu secin", "Select installation folder") };
                if (dlg.ShowDialog() == DialogResult.OK)
                    _txtDir.Text = Path.Combine(dlg.SelectedPath, "ETDClip");
            };
            dirRow.Controls.Add(btnBrowse);

            y += 44;

            // ── Separator ────────────────────────────────────────────────────
            AddSeparator(ref y);

            // ── Options ──────────────────────────────────────────────────────
            AddSectionLabel(TR("Kurulum Secenekleri", "Installation Options"), ref y);
            y += 6;

            _chkDesktop = AddCheck(TR("Masaustu kisayolu olustur", "Create Desktop shortcut"),       true,  ref y);
            _chkStart   = AddCheck(TR("Baslat Menusu kisayolu ekle", "Add to Start Menu"),           true,  ref y);
            _chkAuto    = AddCheck(TR("Windows basladiginda otomatik calistir", "Run at Windows startup"), false, ref y);
            _chkRun     = AddCheck(TR("Kurulumdan sonra ETDClip'i ac", "Launch ETDClip after install"), true, ref y);

            y += 4;
            AddSeparator(ref y);

            // ── Progress ─────────────────────────────────────────────────────
            _bar = new ProgressBar
            {
                Location  = new Point(22, y),
                Size      = new Size(476, 6),
                Style     = ProgressBarStyle.Continuous,
                Minimum   = 0,
                Maximum   = 100,
                Value     = 0,
                BackColor = C_SURFACE2
            };
            Controls.Add(_bar);
            y += 14;

            _lblStep = new Label
            {
                Location  = new Point(22, y),
                Size      = new Size(476, 16),
                ForeColor = C_TEXTMUTED,
                Font      = new Font("Segoe UI", 8.5f),
                Text      = ""
            };
            Controls.Add(_lblStep);
            y += 20;

            _lblStatus = new Label
            {
                Location  = new Point(22, y),
                Size      = new Size(476, 16),
                ForeColor = C_ACCENT,
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Text      = _isUpdate ? TR("Mevcut kurulum bulundu - guncelleme yapilacak",
                                            "Existing installation found - will update") : ""
            };
            Controls.Add(_lblStatus);
            y += 26;

            // ── Action buttons ───────────────────────────────────────────────
            _btnCancel = new FlatBtn
            {
                Text        = TR("Iptal", "Cancel"),
                Location    = new Point(22, y),
                Size        = new Size(100, 38),
                BackColor   = C_SURFACE2,
                NormalColor = C_SURFACE2,
                HoverColor  = C_SURFACE,
                ForeColor   = C_TEXTMUTED
            };
            _btnCancel.Click += (s, e) => Close();
            Controls.Add(_btnCancel);

            string installText = _isUpdate
                ? TR("Guncelle", "Update")
                : TR("Kur", "Install");

            _btnInstall = new FlatBtn
            {
                Text        = installText,
                Location    = new Point(348, y),
                Size        = new Size(150, 38),
                BackColor   = C_ACCENT,
                NormalColor = C_ACCENT,
                HoverColor  = C_ACCENT_H,
                ForeColor   = Color.White
            };
            _btnInstall.Click += BtnInstall_Click;
            Controls.Add(_btnInstall);

            y += 56;

            // ── Footer ───────────────────────────────────────────────────────
            var footer = new Panel
            {
                BackColor = C_SURFACE,
                Dock      = DockStyle.Bottom,
                Height    = 28
            };
            footer.Controls.Add(new Label
            {
                Text      = "ETDClip  •  MIT License  •  github.com/emirtugra-dag/ETDClip",
                Font      = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(51, 65, 85),
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            });
            Controls.Add(footer);
        }

        // ─── UI helpers ───────────────────────────────────────────────────────
        private void AddSectionLabel(string text, ref int y)
        {
            Controls.Add(new Label
            {
                Text      = text.ToUpper(),
                Font      = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(71, 96, 130),
                Location  = new Point(22, y),
                AutoSize  = true
            });
            y += 20;
        }

        private void AddSeparator(ref int y)
        {
            var sep = new Panel { Location = new Point(22, y), Size = new Size(476, 1), BackColor = C_BORDER };
            Controls.Add(sep);
            y += 12;
        }

        private CheckBox AddCheck(string text, bool @checked, ref int y)
        {
            var chk = new CheckBox
            {
                Text      = text,
                Checked   = @checked,
                Location  = new Point(22, y),
                AutoSize  = true,
                ForeColor = C_TEXT,
                Cursor    = Cursors.Hand
            };
            Controls.Add(chk);
            y += 26;
            return chk;
        }

        private void SetStep(string msg)
        {
            if (InvokeRequired) Invoke(() => _lblStep.Text = msg);
            else _lblStep.Text = msg;
        }

        private void SetStatus(string msg, Color? color = null)
        {
            if (InvokeRequired) Invoke(() => { _lblStatus.Text = msg; if (color.HasValue) _lblStatus.ForeColor = color.Value; });
            else { _lblStatus.Text = msg; if (color.HasValue) _lblStatus.ForeColor = color.Value; }
        }

        private void SetProgress(int val)
        {
            if (InvokeRequired) Invoke(() => _bar.Value = Math.Clamp(val, 0, 100));
            else _bar.Value = Math.Clamp(val, 0, 100);
        }

        // ─── Install logic ────────────────────────────────────────────────────
        private async void BtnInstall_Click(object? sender, EventArgs e)
        {
            if (_isInstalling) return;
            _isInstalling    = true;
            _btnInstall.Enabled = false;
            _btnCancel.Enabled  = false;

            try
            {
                await Task.Run(DoInstall);

                Invoke(() =>
                {
                    SetStatus(TR("Kurulum tamamlandi!", "Installation complete!"), C_SUCCESS);
                    string msg = _isUpdate
                        ? TR("ETDClip basariyla guncellendi.", "ETDClip updated successfully.")
                        : TR("ETDClip basariyla kuruldu.", "ETDClip installed successfully.");
                    MessageBox.Show(msg, TR("Tamamlandi", "Done"), MessageBoxButtons.OK, MessageBoxIcon.Information);

                    if (_chkRun.Checked)
                    {
                        string exe = Path.Combine(InstallDir, "ETDClip.exe");
                        if (File.Exists(exe))
                            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
                    }
                    Close();
                });
            }
            catch (Exception ex)
            {
                Invoke(() =>
                {
                    SetStep("");
                    SetProgress(0);
                    SetStatus(TR("Hata: ", "Error: ") + ex.Message, C_DANGER);
                    MessageBox.Show(ex.Message, TR("Hata", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _btnInstall.Enabled = true;
                    _btnCancel.Enabled  = true;
                    _isInstalling = false;
                });
            }
        }

        private void DoInstall()
        {
            string dir = InstallDir;
            string exe = Path.Combine(dir, "ETDClip.exe");

            // 1 — Kill running instance
            SetStep(TR("Calisan ETDClip kapatiliyor...", "Closing running ETDClip..."));
            SetProgress(5);
            KillRunningEtdClip();
            Thread.Sleep(700);

            // 2 — Create directory
            SetStep(TR("Klasor olusturuluyor...", "Creating directory..."));
            SetProgress(15);
            Directory.CreateDirectory(dir);

            // 3 — Extract ETDClip.exe
            SetStep(TR("Dosyalar kopyalaniyor...", "Extracting files..."));
            SetProgress(30);
            ExtractMainApp(exe);

            // 4 — Assets
            SetProgress(55);
            ExtractAssets(dir);

            // 5 — Shortcuts
            SetStep(TR("Kisayollar olusturuluyor...", "Creating shortcuts..."));
            SetProgress(65);
            CreateShortcuts(exe);

            // 6 — Registry
            SetStep(TR("Sistem kaydı yapılıyor...", "Registering..."));
            SetProgress(82);
            WriteRegistry(dir, exe);

            SetProgress(100);
            SetStep(TR("Tamamlandi.", "Done."));
        }

        private static void KillRunningEtdClip()
        {
            foreach (var p in Process.GetProcessesByName("ETDClip"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        private void ExtractMainApp(string destExe)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith("ETDClip.exe", StringComparison.OrdinalIgnoreCase)) continue;
                using var src = asm.GetManifestResourceStream(name)!;
                using var dst = new FileStream(destExe, FileMode.Create, FileAccess.Write, FileShare.None);
                src.CopyTo(dst);
                return;
            }

            // Fallback: next to setup
            string candidate = Path.Combine(AppContext.BaseDirectory, "ETDClip.exe");
            if (File.Exists(candidate)) { File.Copy(candidate, destExe, true); return; }

            throw new FileNotFoundException(TR(
                "ETDClip.exe kurulum paketinde bulunamadi.",
                "ETDClip.exe not found in setup package."));
        }

        private void ExtractAssets(string installDir)
        {
            try
            {
                string assetsDir = Path.Combine(installDir, "Assets");
                Directory.CreateDirectory(assetsDir);
                var asm = Assembly.GetExecutingAssembly();
                foreach (var assetName in new[] { "app.ico", "etdclip_logo.png" })
                {
                    using var s = asm.GetManifestResourceStream($"ETDClipSetup.{assetName}");
                    if (s == null) continue;
                    using var f = new FileStream(Path.Combine(assetsDir, assetName), FileMode.Create);
                    s.CopyTo(f);
                }
            }
            catch { }
        }

        private void CreateShortcuts(string appExe)
        {
            if (_chkDesktop.Checked)
            {
                string lnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "ETDClip.lnk");
                MakeShortcut(lnk, appExe, TR("ETDClip Pano Yoneticisi", "ETDClip Clipboard Manager"));
            }

            if (_chkStart.Checked)
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs", "ETDClip");
                Directory.CreateDirectory(folder);

                MakeShortcut(
                    Path.Combine(folder, "ETDClip.lnk"),
                    appExe,
                    TR("ETDClip Pano Yoneticisi", "ETDClip Clipboard Manager"));

                MakeShortcut(
                    Path.Combine(folder, TR("ETDClip Kisayol Ayarlama.lnk", "ETDClip Hotkey Settings.lnk")),
                    appExe,
                    TR("ETDClip Kisayol Ayarlama", "ETDClip Hotkey Settings"),
                    "--open-settings");
            }
        }

        private void WriteRegistry(string installDir, string appExe)
        {
            // Auto-start
            using var runKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (_chkAuto.Checked)
                runKey?.SetValue("ETDClip", $"\"{appExe}\" --autostart");
            else
                runKey?.DeleteValue("ETDClip", false);

            // Programs list (Add/Remove Programs)
            using var uninstKey = Registry.CurrentUser.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ETDClip");
            if (uninstKey != null)
            {
                string ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.2";
                uninstKey.SetValue("DisplayName",     "ETDClip");
                uninstKey.SetValue("DisplayVersion",  ver);
                uninstKey.SetValue("Publisher",       "Emir Tuğra Dağ");
                uninstKey.SetValue("InstallLocation", installDir);
                uninstKey.SetValue("UninstallString", $"\"{appExe}\" --uninstall");
                uninstKey.SetValue("DisplayIcon",     $"{appExe},0");
                uninstKey.SetValue("NoModify",        1, RegistryValueKind.DWord);
                uninstKey.SetValue("NoRepair",        1, RegistryValueKind.DWord);
                uninstKey.SetValue("EstimatedSize",   350000, RegistryValueKind.DWord);
                uninstKey.SetValue("URLInfoAbout",    "https://github.com/emirtugra-dag/ETDClip");
            }

            // Write user selected language to settings.json
            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETDClip");
                Directory.CreateDirectory(appDataDir);
                string settingsPath = Path.Combine(appDataDir, "settings.json");
                if (!File.Exists(settingsPath))
                {
                    string json = $"{{\n  \"Language\": \"{_lang}\",\n  \"Hotkey\": \"Alt+V\",\n  \"MaxHistoryItems\": 10,\n  \"MaxSingleFileSizeMB\": 50,\n  \"AutoCacheFiles\": true,\n  \"AutoStartWithWindows\": {_chkAuto.Checked.ToString().ToLower()},\n  \"DarkMode\": true\n}}";
                    File.WriteAllText(settingsPath, json);
                }
                else
                {
                    string content = File.ReadAllText(settingsPath);
                    if (content.Contains("\"Language\""))
                    {
                        content = System.Text.RegularExpressions.Regex.Replace(content, "\"Language\"\\s*:\\s*\"[^\"]*\"", $"\"Language\": \"{_lang}\"");
                        File.WriteAllText(settingsPath, content);
                    }
                }
            }
            catch { }
        }

        private static void MakeShortcut(string lnkPath, string target, string desc, string args = "")
        {
            try
            {
                Type? t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t)!;
                dynamic sc    = shell.CreateShortcut(lnkPath);
                sc.TargetPath       = target;
                sc.Arguments        = args;
                sc.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
                sc.Description      = desc;
                sc.IconLocation     = $"{target},0";
                sc.Save();
            }
            catch { }
        }
    }
}
