// Ironbark Coop setup: one window to install, update or remove the mod. The mod's files (BepInEx and the plugin) are
// inside this exe as payload.zip. Built by tools\make-release.ps1 with the C# compiler that ships with Windows
// (.NET Framework 4), so players need nothing else installed. C# 5 syntax on purpose.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Ironbark Coop setup")]
[assembly: AssemblyProduct("Ironbark Coop")]

namespace IronbarkCoopSetup
{
    static class Program
    {
        [STAThread]
        // No arguments: the window. For scripts and testing: /install <game folder>, /uninstall <game folder> [/loader],
        // /shot <png> [<game folder>] (a picture of the window). The outcome goes to %TEMP%IronbarkCoopSetup.log.
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 0) { Application.Run(new SetupForm(null)); return 0; }
            var form = new SetupForm(args.Length > 1 && args[0] != "/shot" ? args[1] : args.Length > 2 ? args[2] : null);
            form.Quiet = true;
            bool ok = true;
            if (args[0] == "/install") ok = form.Install();
            else if (args[0] == "/uninstall") { form.RemoveLoader = Array.IndexOf(args, "/loader") >= 0; ok = form.Remove(); }
            else if (args[0] == "/shot" && args.Length > 1)
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-4000, 0); form.Show(); Application.DoEvents();
                using (var picture = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(picture, new Rectangle(0, 0, form.Width, form.Height)); picture.Save(args[1]); }
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "IronbarkCoopSetup.log"), form.Report);
            return ok ? 0 : 1;
        }
    }

    sealed class SetupForm : Form
    {
        const string ExeName = "Fears to Fathom - Ironbark Lookout.exe";
        const string ProcessName = "Fears to Fathom - Ironbark Lookout";
        // Assembly-CSharp.dll of the game build the mod was tested on.
        const string TestedBuild = "3590DA9516B67E36E18112E725B165F316757F7D0F4140C4CDAA4E263B9E96B7";
        static readonly bool Ru = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        static string L(string ru, string en) { return Ru ? ru : en; }

        static readonly Color Back = Color.FromArgb(18, 18, 20), Surface = Color.FromArgb(32, 32, 36), Fore = Color.FromArgb(230, 230, 230),
            Dim = Color.FromArgb(150, 150, 155), Pink = Color.FromArgb(232, 64, 112), Yellow = Color.FromArgb(240, 220, 90), Green = Color.FromArgb(110, 200, 120);

        readonly TextBox folder = new TextBox();
        readonly Label state = new Label(), result = new Label(), code = new Label(), note = new Label();
        readonly Button install = new Button(), remove = new Button(), browse = new Button(), copy = new Button(), close = new Button();

        internal bool Quiet, RemoveLoader;
        internal string Report { get { return state.Text + "\n" + result.Text + "\n" + code.Text + "\n" + note.Text; } }

        public SetupForm(string gameFolder)
        {
            Text = "Ironbark Coop " + Version.Value;
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Back; ForeColor = Fore;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 400);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label { Text = "IRONBARK COOP", Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Pink, AutoSize = true, Location = new Point(24, 18) };
            var subtitle = new Label { Text = L("Кооператив на двоих для Fears to Fathom: Ironbark Lookout", "Two-player co-op for Fears to Fathom: Ironbark Lookout") + "  ·  " + Version.Value,
                ForeColor = Dim, AutoSize = true, Location = new Point(27, 58) };
            var folderLabel = new Label { Text = L("Папка игры", "Game folder"), ForeColor = Dim, AutoSize = true, Location = new Point(27, 98) };
            folder.SetBounds(27, 120, 410, 26);
            folder.BackColor = Surface; folder.ForeColor = Fore; folder.BorderStyle = BorderStyle.FixedSingle;
            folder.TextChanged += delegate { UpdateState(); };
            Style(browse, L("Обзор…", "Browse…"), false); browse.SetBounds(445, 119, 88, 28);
            browse.Click += delegate { Browse(); };

            state.SetBounds(27, 156, 506, 22); state.ForeColor = Dim;

            Style(install, "", true); install.SetBounds(27, 190, 250, 40);
            install.Click += delegate { Install(); };
            Style(remove, L("Удалить мод", "Remove the mod"), false); remove.SetBounds(285, 190, 150, 40);
            remove.Click += delegate { Remove(); };

            result.SetBounds(27, 246, 506, 22); result.ForeColor = Green;
            code.SetBounds(27, 270, 300, 36); code.Font = new Font("Consolas", 18f, FontStyle.Bold); code.ForeColor = Yellow;
            Style(copy, L("Скопировать", "Copy"), false); copy.SetBounds(335, 274, 100, 28); copy.Visible = false;
            copy.Click += delegate { try { Clipboard.SetText(code.Text); } catch { } };
            note.SetBounds(27, 310, 506, 54); note.ForeColor = Dim;

            Style(close, L("Закрыть", "Close"), false); close.SetBounds(445, 356, 88, 30);
            close.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { title, subtitle, folderLabel, folder, browse, state, install, remove, result, code, copy, note, close });
            folder.Text = gameFolder ?? FindGame() ?? "";
            Shown += delegate { ActiveControl = install; folder.SelectionLength = 0; };
            UpdateState();
        }

        static void Style(Button button, string text, bool primary)
        {
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? Pink : Color.FromArgb(70, 70, 76);
            button.BackColor = primary ? Pink : Surface;
            button.ForeColor = primary ? Color.White : Fore;
            if (primary) button.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
        }

        string Game { get { return folder.Text.Trim().Trim('"'); } }
        bool GameHere { get { return Game.Length > 0 && File.Exists(Path.Combine(Game, ExeName)); } }
        string PluginDir { get { return Path.Combine(Game, @"BepInEx\plugins\IronbarkCoop"); } }

        string InstalledVersion
        {
            get
            {
                if (!GameHere || !File.Exists(Path.Combine(PluginDir, "IronbarkCoop.dll"))) return null;
                var stamp = Path.Combine(PluginDir, "version.txt");
                return File.Exists(stamp) ? File.ReadAllText(stamp).Trim() : "?";
            }
        }

        void UpdateState()
        {
            var installed = InstalledVersion;
            if (!GameHere)
            {
                state.Text = L("Укажите папку, где лежит «" + ExeName + "».", "Pick the folder with \"" + ExeName + "\".");
                state.ForeColor = Yellow;
            }
            else if (installed == null) { state.Text = L("Игра найдена. Мод ещё не установлен.", "Game found. The mod is not installed yet."); state.ForeColor = Dim; }
            else if (installed == Version.Value) { state.Text = L("Установлена эта версия мода (" + installed + ").", "This version of the mod is installed (" + installed + ")."); state.ForeColor = Green; }
            else { state.Text = L("Установлена другая версия мода (" + (installed == "?" ? "старая" : installed) + ").", "Another version of the mod is installed (" + (installed == "?" ? "older" : installed) + ")."); state.ForeColor = Dim; }
            install.Text = installed == null ? L("Установить", "Install") : installed == Version.Value ? L("Переустановить", "Reinstall") : L("Обновить до " + Version.Value, "Update to " + Version.Value);
            install.Enabled = GameHere;
            remove.Enabled = installed != null;
        }

        void Browse()
        {
            using (var dialog = new FolderBrowserDialog { Description = L("Папка с «" + ExeName + "»", "The folder with \"" + ExeName + "\"") })
            {
                if (GameHere) dialog.SelectedPath = Game;
                if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            }
        }

        bool GameRunning()
        {
            foreach (var process in Process.GetProcessesByName(ProcessName))
            {
                try { if (process.MainModule.FileName.StartsWith(Game, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; }
            }
            return false;
        }

        internal bool Install()
        {
            if (!GameHere) { Warn(L("Папка игры не найдена.", "Game folder not found.")); return false; }
            if (GameRunning()) { Warn(L("Закройте игру и нажмите ещё раз.", "Close the game and press again.")); return false; }
            try
            {
                Cursor = Cursors.WaitCursor;
                string before = InstalledVersion;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string root = Path.GetFullPath(Game) + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        // Windows PowerShell writes zip paths with backslashes.
                        string name = entry.FullName.Replace('\\', '/');
                        var target = Path.GetFullPath(Path.Combine(Game, name.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
                File.WriteAllText(Path.Combine(PluginDir, "version.txt"), Version.Value);
                string hash = BuildHash();
                result.Text = before == null ? L("Установлено.", "Installed.") : L("Обновлено: " + (before == "?" ? "старая версия" : before) + " → " + Version.Value + ". Настройки сохранены.", "Updated: " + (before == "?" ? "older version" : before) + " → " + Version.Value + ". Your settings are kept.");
                result.ForeColor = Green;
                code.Text = hash.Substring(0, 12);
                copy.Visible = true;
                note.Text = L("Код версии игры: сравните его с другом, он должен совпадать.", "Game build code: compare it with your friend, it must match.")
                    + (hash == TestedBuild ? "" : L("\nЭта версия игры не та, на которой мод проверялся: может работать, но не гарантируется.", "\nThis game build differs from the tested one: it may work, but it is not verified."))
                    + L("\nЗапустите игру → «Мультиплеер».", "\nStart the game → Multiplayer.");
            }
            catch (Exception ex) { Warn(L("Не удалось установить: ", "Could not install: ") + ex.Message); return false; }
            finally { Cursor = Cursors.Default; UpdateState(); }
            return true;
        }

        internal bool Remove()
        {
            if (GameRunning()) { Warn(L("Закройте игру и нажмите ещё раз.", "Close the game and press again.")); return false; }
            if (!Quiet && MessageBox.Show(this, L("Удалить мод и его настройки?", "Remove the mod and its settings?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
            try
            {
                if (Directory.Exists(PluginDir)) Directory.Delete(PluginDir, true);
                var config = Path.Combine(Game, @"BepInEx\config\astra.ironbark.coop.cfg");
                if (File.Exists(config)) File.Delete(config);
                // BepInEx may be used by other mods: removed only on request.
                if (Quiet ? RemoveLoader : MessageBox.Show(this, L("Удалить и загрузчик модов BepInEx? Другие моды тоже перестанут работать.", "Also remove the BepInEx mod loader? Other mods will stop working too."),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                {
                    foreach (var file in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" })
                        if (File.Exists(Path.Combine(Game, file))) File.Delete(Path.Combine(Game, file));
                    if (Directory.Exists(Path.Combine(Game, "BepInEx"))) Directory.Delete(Path.Combine(Game, "BepInEx"), true);
                }
                result.Text = L("Мод удалён.", "The mod is removed."); result.ForeColor = Green;
                code.Text = ""; copy.Visible = false; note.Text = "";
            }
            catch (Exception ex) { Warn(L("Не удалось удалить: ", "Could not remove: ") + ex.Message); UpdateState(); return false; }
            UpdateState();
            return true;
        }

        void Warn(string text) { result.Text = text; result.ForeColor = Yellow; }

        string BuildHash()
        {
            var assembly = Path.Combine(Game, @"Fears to Fathom - Ironbark Lookout_Data\Managed\Assembly-CSharp.dll");
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(assembly))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }

        // Steam libraries listed by the Steam client, then the usual folders on every drive.
        static string FindGame()
        {
            var candidates = new System.Collections.Generic.List<string>();
            try
            {
                var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                var vdf = steam == null ? null : Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (vdf != null && File.Exists(vdf))
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        candidates.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps\common\Fears to Fathom - Ironbark Lookout"));
            }
            catch { }
            foreach (var drive in DriveInfo.GetDrives())
                foreach (var sub in new[] { @"Games\Fears to Fathom Ironbark Lookout", @"Games\Fears to Fathom - Ironbark Lookout", @"SteamLibrary\steamapps\common\Fears to Fathom - Ironbark Lookout" })
                    candidates.Add(Path.Combine(drive.RootDirectory.FullName, sub));
            foreach (var path in candidates)
                try { if (File.Exists(Path.Combine(path, ExeName))) return path; } catch { }
            return null;
        }
    }
}
