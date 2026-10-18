// TrayFolderMenu - kaskadowe menu skrótów w zasobniku (zamiennik pasków narzędzi z Windows 10)
//
// Kompilacja: build.cmd (wbudowany w Windows csc.exe z .NET Framework 4.8; kod zgodny z C# 5).
//
// Przełączniki wiersza poleceń:
//   (brak)                          uruchamia aplikację i otwiera okno ustawień
//   /autostart                      uruchamia po cichu w zasobniku (tak startuje autostart)
//   /install [/quiet] [/folder:X]   instalacja dla bieżącego użytkownika + autostart + start
//   /uninstall [/quiet]             dezinstalacja
//   /exit                           zamyka działającą instancję

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("TrayFolderMenu")]
[assembly: AssemblyDescription("Kaskadowe menu skrótów w zasobniku")]
[assembly: AssemblyProduct("TrayFolderMenu")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: ComVisible(false)]

namespace TrayFolderMenu
{
    // =========================================================================
    // Punkt wejścia, jedna instancja, sygnały między procesami
    // =========================================================================
    static class Program
    {
        public const string MutexName = @"Local\TrayFolderMenu.Instance";
        public const string ShowEventName = @"Local\TrayFolderMenu.Show";
        public const string ExitEventName = @"Local\TrayFolderMenu.Exit";

        static Mutex instanceMutex;

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool quiet = false, install = false, uninstall = false, exit = false, autostart = false, help = false;
            string folder = null;
            foreach (string raw in args)
            {
                string a = raw.Trim();
                string l = a.ToLowerInvariant();
                if (l == "/quiet" || l == "/q" || l == "/silent") quiet = true;
                else if (l == "/install") install = true;
                else if (l == "/uninstall") uninstall = true;
                else if (l == "/exit") exit = true;
                else if (l == "/autostart") autostart = true;
                else if (l.StartsWith("/folder:")) folder = a.Substring(8).Trim().Trim('"');
                else if (l == "/?" || l == "/help" || l == "-h" || l == "--help") help = true;
            }

            Native.MakeDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Error("nieobsłużony wyjątek", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Write("KRYTYCZNY BŁĄD: " + e.ExceptionObject);

            if (help) { ShowHelp(); return 0; }
            if (uninstall) return Installer.Uninstall(quiet);
            if (install) return Installer.Install(quiet, folder);
            if (exit) { SignalEvent(ExitEventName); return 0; }

            if (!TryAcquireInstance(0))
            {
                // Już działa: przy zwykłym uruchomieniu pokaż jego okno ustawień
                if (!autostart)
                {
                    Native.AllowSetForegroundWindow(-1);
                    SignalEvent(ShowEventName);
                }
                return 0;
            }

            try
            {
                using (var showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                using (var exitEvt = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
                using (var app = new TrayApp(!autostart, showEvt, exitEvt))
                {
                    Application.Run(app);
                }
            }
            finally
            {
                ReleaseInstance();
            }
            return 0;
        }

        public static bool TryAcquireInstance(int timeoutMs)
        {
            if (instanceMutex == null) instanceMutex = new Mutex(false, MutexName);
            try { return instanceMutex.WaitOne(timeoutMs); }
            catch (AbandonedMutexException) { return true; }   // poprzednia instancja została zabita
        }

        public static void ReleaseInstance()
        {
            if (instanceMutex == null) return;
            try { instanceMutex.ReleaseMutex(); } catch { }
        }

        /// <summary>Zamyka działającą instancję i przejmuje blokadę. True = blokada przejęta.</summary>
        public static bool StopRunningInstance()
        {
            if (TryAcquireInstance(0)) return true;
            SignalEvent(ExitEventName);
            return TryAcquireInstance(10000);
        }

        public static void SignalEvent(string name)
        {
            EventWaitHandle ev;
            if (EventWaitHandle.TryOpenExisting(name, out ev))
            {
                using (ev) ev.Set();
            }
        }

        static void ShowHelp()
        {
            MessageBox.Show(
                "TrayFolderMenu " + Version + "\n\n" +
                "(brak)\t\turuchamia i otwiera ustawienia\n" +
                "/autostart\turuchamia po cichu w zasobniku\n" +
                "/install\t\tinstaluje dla bieżącego użytkownika\n" +
                "/uninstall\tdezinstaluje\n" +
                "/quiet\t\tbez okienek (z /install i /uninstall)\n" +
                "/folder:\"C:\\Menu\"\tdodaje folder (z /install)\n" +
                "/exit\t\tzamyka działającą instancję",
                "TrayFolderMenu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // =========================================================================
    // Ścieżki
    // =========================================================================
    static class AppPaths
    {
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static readonly string ConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayFolderMenu");
        public static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");
        public static readonly string DataDir = Path.Combine(LocalAppData, "TrayFolderMenu");
        public static readonly string LogFile = Path.Combine(DataDir, "log.txt");
        public static readonly string InstallDir = Path.Combine(Path.Combine(LocalAppData, "Programs"), "TrayFolderMenu");
        public static readonly string InstallExe = Path.Combine(InstallDir, "TrayFolderMenu.exe");

        public static string CurrentExe { get { return Application.ExecutablePath; } }
        public static bool IsInstalledCopy { get { return SamePath(CurrentExe, InstallExe); } }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            try
            {
                string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                string trimmed = full.TrimEnd('\\', '/');
                return trimmed.EndsWith(":") ? trimmed + "\\" : trimmed;
            }
            catch
            {
                return path.Trim();
            }
        }

        public static bool SamePath(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string LeafName(string path)
        {
            string n = Path.GetFileName((path ?? "").TrimEnd('\\'));
            return string.IsNullOrEmpty(n) ? path : n;
        }
    }

    // =========================================================================
    // Log z prostą rotacją (1 MB)
    // =========================================================================
    static class Log
    {
        static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    var fi = new FileInfo(AppPaths.LogFile);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = Path.Combine(AppPaths.DataDir, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(AppPaths.LogFile, old);
                    }
                    File.AppendAllText(AppPaths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // log nie może wywrócić aplikacji
            }
        }

        public static void Error(string context, Exception ex)
        {
            Write("BŁĄD (" + context + "): " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // =========================================================================
    // Konfiguracja: %APPDATA%\TrayFolderMenu\config.json
    // =========================================================================
    [DataContract]
    sealed class FolderEntry
    {
        [DataMember(Order = 1)] public string Path;
        [DataMember(Order = 2)] public bool Enabled;

        public FolderEntry(string path)
        {
            Path = path;
            Enabled = true;
        }

        [OnDeserializing]
        void OnDeserializing(StreamingContext context) { Enabled = true; }
    }

    [DataContract]
    sealed class AppConfig
    {
        public const string DefaultHotkey = "Ctrl+Shift+A";

        [DataMember(Order = 1)] public List<FolderEntry> Folders;
        [DataMember(Order = 2)] public bool HotkeyEnabled;
        [DataMember(Order = 3)] public string Hotkey;
        [DataMember(Order = 4)] public bool KeepMenuOpen;

        public AppConfig() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext context) { SetDefaults(); }

        void SetDefaults()
        {
            Folders = new List<FolderEntry>();
            HotkeyEnabled = true;
            Hotkey = DefaultHotkey;
            KeepMenuOpen = false;
        }

        public static AppConfig Load()
        {
            if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
            try
            {
                AppConfig cfg;
                using (FileStream fs = File.OpenRead(AppPaths.ConfigFile))
                {
                    cfg = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(fs);
                }
                if (cfg.Folders == null) cfg.Folders = new List<FolderEntry>();
                cfg.Folders.RemoveAll(f => f == null || string.IsNullOrWhiteSpace(f.Path));
                foreach (FolderEntry f in cfg.Folders) f.Path = AppPaths.Normalize(f.Path);
                if (string.IsNullOrWhiteSpace(cfg.Hotkey)) cfg.Hotkey = DefaultHotkey;
                return cfg;
            }
            catch (Exception ex)
            {
                Log.Error("odczyt konfiguracji", ex);
                try { File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".bad", true); } catch { }
                return new AppConfig();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.ConfigDir);
            string tmp = AppPaths.ConfigFile + ".tmp";
            using (FileStream fs = File.Create(tmp))
            using (XmlDictionaryWriterHolder w = new XmlDictionaryWriterHolder(fs))
            {
                new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(w.Writer, this);
            }
            // zapis atomowy: plik tymczasowy -> podmiana
            if (File.Exists(AppPaths.ConfigFile))
            {
                try { File.Replace(tmp, AppPaths.ConfigFile, null); }
                catch (IOException) { File.Copy(tmp, AppPaths.ConfigFile, true); File.Delete(tmp); }
            }
            else
            {
                File.Move(tmp, AppPaths.ConfigFile);
            }
        }

        public FolderEntry Find(string path)
        {
            return Folders.FirstOrDefault(f => AppPaths.SamePath(f.Path, path));
        }

        public bool AddFolder(string path)
        {
            string full = AppPaths.Normalize(path);
            if (string.IsNullOrWhiteSpace(full) || Find(full) != null) return false;
            Folders.Add(new FolderEntry(full));
            return true;
        }

        // Writer JSON z wcięciami (czytelny przy ręcznej edycji)
        sealed class XmlDictionaryWriterHolder : IDisposable
        {
            public readonly System.Xml.XmlDictionaryWriter Writer;
            public XmlDictionaryWriterHolder(Stream s)
            {
                Writer = JsonReaderWriterFactory.CreateJsonWriter(s, new UTF8Encoding(false), false, true, "  ");
            }
            public void Dispose()
            {
                Writer.Flush();
                Writer.Close();
            }
        }
    }

    // =========================================================================
    // Skrót klawiszowy: "Ctrl+Shift+A" <-> modyfikatory + klawisz
    // =========================================================================
    static class Hotkey
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        public static string Format(uint mods, Keys key)
        {
            var parts = new List<string>();
            if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(key >= Keys.D0 && key <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString());
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out uint mods, out Keys key)
        {
            mods = 0;
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string p = raw.Trim();
                if (p.Length == 0) return false;
                string u = p.ToUpperInvariant();
                if (u == "CTRL" || u == "CONTROL") mods |= MOD_CONTROL;
                else if (u == "ALT") mods |= MOD_ALT;
                else if (u == "SHIFT") mods |= MOD_SHIFT;
                else if (u == "WIN" || u == "WINDOWS") mods |= MOD_WIN;
                else
                {
                    if (key != Keys.None) return false;
                    Keys k;
                    if (p.Length == 1 && p[0] >= '0' && p[0] <= '9') k = Keys.D0 + (p[0] - '0');
                    else if (!Enum.TryParse(p, true, out k)) return false;
                    if ((k & Keys.Modifiers) != 0) return false;
                    key = k;
                }
            }
            return key != Keys.None && IsValid(mods, key);
        }

        /// <summary>Wymaga Ctrl, Alt lub Win - inaczej skrót blokowałby zwykłe pisanie. Wyjątek: F1-F24.</summary>
        public static bool IsValid(uint mods, Keys key)
        {
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            return (mods & (MOD_CONTROL | MOD_ALT | MOD_WIN)) != 0 || functionKey;
        }
    }

    // =========================================================================
    // Autostart: HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    // =========================================================================
    static class Autostart
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "TrayFolderMenu";

        /// <summary>True, gdy wpis istnieje i nie został wyłączony w Menedżerze zadań.</summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        if (k == null || k.GetValue(ValueName) == null) return false;
                    }
                    using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                    {
                        byte[] state = a == null ? null : a.GetValue(ValueName) as byte[];
                        return state == null || state.Length == 0 || (state[0] & 1) == 0;   // nieparzysty = wyłączony
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled, string exe)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, "\"" + exe + "\" /autostart");
                else k.DeleteValue(ValueName, false);
            }
            // usuń znacznik "Wyłączone" z Menedżera zadań, żeby wpis faktycznie działał
            using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            {
                if (a != null) a.DeleteValue(ValueName, false);
            }
            Log.Write(enabled ? "Autostart włączony: " + exe : "Autostart wyłączony");
        }
    }

    // =========================================================================
    // Instalacja per użytkownik: %LOCALAPPDATA%\Programs\TrayFolderMenu
    // =========================================================================
    static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TrayFolderMenu";

        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TrayFolderMenu.lnk"); }
        }

        public static bool IsInstalled { get { return File.Exists(AppPaths.InstallExe); } }

        public static int Install(bool quiet, string addFolder)
        {
            if (!Program.StopRunningInstance())
            {
                Fail(quiet, "Nie udało się zamknąć działającej instancji TrayFolderMenu.");
                return 2;
            }
            try
            {
                Directory.CreateDirectory(AppPaths.InstallDir);
                if (!AppPaths.IsInstalledCopy) CopyWithRetry(AppPaths.CurrentExe, AppPaths.InstallExe);
                CreateShortcut(StartMenuShortcut, AppPaths.InstallExe, "Ustawienia menu skrótów w zasobniku");
                WriteUninstallEntry();
                Autostart.Set(true, AppPaths.InstallExe);
                RemoveLegacyAutostart();
                if (!string.IsNullOrWhiteSpace(addFolder))
                {
                    AppConfig cfg = AppConfig.Load();
                    if (cfg.AddFolder(addFolder)) cfg.Save();
                }
                Log.Write("Zainstalowano " + Program.Version + " w " + AppPaths.InstallDir);
            }
            catch (Exception ex)
            {
                Log.Error("instalacja", ex);
                Fail(quiet, "Instalacja nie powiodła się:\n" + ex.Message);
                return 1;
            }
            finally
            {
                Program.ReleaseInstance();
            }

            Process.Start(new ProcessStartInfo(AppPaths.InstallExe, quiet ? "/autostart" : "") { UseShellExecute = false });
            return 0;
        }

        public static int Uninstall(bool quiet)
        {
            Program.StopRunningInstance();
            try
            {
                Autostart.Set(false, null);
                RemoveLegacyAutostart();
                if (File.Exists(StartMenuShortcut)) File.Delete(StartMenuShortcut);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

                bool removeData = !quiet && MessageBox.Show(
                    "TrayFolderMenu zostanie odinstalowane.\n\nUsunąć też ustawienia i log?\n" +
                    AppPaths.ConfigDir + "\n" + AppPaths.DataDir,
                    "TrayFolderMenu", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

                Log.Write("Odinstalowano" + (removeData ? "" : " (ustawienia zachowane)"));
                if (removeData)
                {
                    TryDeleteDirectory(AppPaths.ConfigDir);
                    TryDeleteDirectory(AppPaths.DataDir);
                }
            }
            catch (Exception ex)
            {
                Log.Error("dezinstalacja", ex);
                Fail(quiet, "Dezinstalacja nie powiodła się:\n" + ex.Message);
                return 1;
            }
            finally
            {
                Program.ReleaseInstance();
            }

            // Uruchomionego exe nie da się usunąć - folder znika chwilę po zakończeniu procesu
            if (Directory.Exists(AppPaths.InstallDir))
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + AppPaths.InstallDir + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            return 0;
        }

        static void WriteUninstallEntry()
        {
            string exe = AppPaths.InstallExe;
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "TrayFolderMenu");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("DisplayIcon", exe + ",0");
                k.SetValue("InstallLocation", AppPaths.InstallDir);
                k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + exe + "\" /uninstall /quiet");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>Usuwa wpisy autostartu poprzedniej wersji (skrypt PowerShell).</summary>
        static void RemoveLegacyAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Autostart.RunKey, true))
                {
                    if (k != null)
                    {
                        foreach (string name in k.GetValueNames())
                        {
                            if (name.StartsWith("TrayFolderMenu - ", StringComparison.OrdinalIgnoreCase)) k.DeleteValue(name, false);
                        }
                    }
                }
                string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                if (Directory.Exists(startup))
                {
                    foreach (string lnk in Directory.GetFiles(startup, "TrayFolderMenu - *.lnk")) File.Delete(lnk);
                }
            }
            catch (Exception ex)
            {
                Log.Error("sprzątanie starej wersji", ex);
            }
        }

        static void CreateShortcut(string lnkPath, string target, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                try
                {
                    Type t = lnk.GetType();
                    t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                    t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                    t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { description });
                    t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                    t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(lnk);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        /// <summary>Poprzednia instancja zwalnia blokadę chwilę przed zamknięciem procesu - plik może być jeszcze zajęty.</summary>
        static void CopyWithRetry(string source, string target)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Copy(source, target, true);
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 25) throw;
                    Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt >= 25) throw;
                    Thread.Sleep(200);
                }
            }
        }

        static void TryDeleteDirectory(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception ex) { Log.Error("usuwanie " + dir, ex); }
        }

        static void Fail(bool quiet, string message)
        {
            if (!quiet) MessageBox.Show(message, "TrayFolderMenu", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =========================================================================
    // WinAPI
    // =========================================================================
    static class Native
    {
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int dwProcessId);
        [DllImport("user32.dll")] public static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool HideCaret(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int StrCmpLogicalW(string psz1, string psz2);
        [DllImport("ntdll.dll")] static extern int RtlGetVersion(ref OSVERSIONINFOEX info);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
            public short wServicePackMajor, wServicePackMinor, wSuiteMask;
            public byte wProductType, wReserved;
        }

        public static int WindowsBuild()
        {
            try
            {
                var info = new OSVERSIONINFOEX();
                info.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                return RtlGetVersion(ref info) == 0 ? info.dwBuildNumber : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void MakeDpiAware()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        public static void SetDwmInt(IntPtr hwnd, int attribute, int value)
        {
            try { DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)); } catch { }
        }

        public static void SetTheme(IntPtr hwnd, string name)
        {
            try { SetWindowTheme(hwnd, name, null); } catch { }
        }
    }

    // =========================================================================
    // Ikony z powłoki - te same, które pokazuje Eksplorator
    // =========================================================================
    static class ShellIcons
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_SMALLICON = 0x1;

        public static Icon GetIcon(string path, bool large)
        {
            try
            {
                var info = new SHFILEINFO();
                SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON));
                if (info.hIcon == IntPtr.Zero) return null;
                try { return (Icon)Icon.FromHandle(info.hIcon).Clone(); }
                finally { Native.DestroyIcon(info.hIcon); }
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap GetBitmap(string path)
        {
            Icon icon = GetIcon(path, false);
            if (icon == null) return null;
            using (icon) return icon.ToBitmap();
        }
    }

    // =========================================================================
    // Nowoczesne okno wyboru folderu (IFileOpenDialog), z awaryjnym FolderBrowserDialog
    // =========================================================================
    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCoClass { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40, FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;

        public static string Pick(IWin32Window owner, string title)
        {
            IFileDialog dialog = null;
            try
            {
                dialog = (IFileDialog)new FileOpenDialogCoClass();
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                if (dialog.Show(owner == null ? IntPtr.Zero : owner.Handle) != 0) return null;   // anulowano
                IShellItem item;
                dialog.GetResult(out item);
                string path;
                item.GetDisplayName(SIGDN_FILESYSPATH, out path);
                Marshal.ReleaseComObject(item);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("okno wyboru folderu", ex);
                using (var fallback = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
                {
                    return fallback.ShowDialog(owner) == DialogResult.OK ? fallback.SelectedPath : null;
                }
            }
            finally
            {
                if (dialog != null) Marshal.ReleaseComObject(dialog);
            }
        }
    }

    // =========================================================================
    // Motyw: jasny/ciemny zgodnie z ustawieniem Windows
    // =========================================================================
    static class Theme
    {
        public static bool Dark;
        public static Color WindowBack, Card, CardBorder, Text, SubText, ButtonBack, ButtonBorder, ButtonHover,
                            Accent, AccentHover, AccentText, InputBack, Error, Success,
                            MenuBack, MenuHover, MenuText, MenuDisabled, MenuSeparator, MenuBorder;

        public static readonly bool IsWindows11 = Native.WindowsBuild() >= 22000;
        static float dpiScale;

        public static float DpiScale
        {
            get
            {
                if (dpiScale <= 0)
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f;
                }
                return dpiScale;
            }
        }

        public static int Scale(int px) { return (int)Math.Round(px * DpiScale); }

        public static void Refresh()
        {
            Dark = ReadDarkMode();
            if (Dark)
            {
                WindowBack = C(0x202020); Card = C(0x2B2B2B); CardBorder = C(0x1C1C1C);
                Text = C(0xFFFFFF); SubText = C(0xC5C5C5);
                ButtonBack = C(0x373737); ButtonBorder = C(0x454545); ButtonHover = C(0x404040);
                Accent = C(0x60CDFF); AccentHover = C(0x56B9E6); AccentText = C(0x000000);
                InputBack = C(0x1F1F1F); Error = C(0xFF99A4); Success = C(0x6CCB5F);
                MenuBack = C(0x2C2C2C); MenuHover = C(0x3D3D3D); MenuText = C(0xFFFFFF);
                MenuDisabled = C(0x7A7A7A); MenuSeparator = C(0x404040); MenuBorder = C(0x474747);
            }
            else
            {
                WindowBack = C(0xF3F3F3); Card = C(0xFBFBFB); CardBorder = C(0xE5E5E5);
                Text = C(0x1B1B1B); SubText = C(0x5F5F5F);
                ButtonBack = C(0xFDFDFD); ButtonBorder = C(0xD1D1D1); ButtonHover = C(0xF0F0F0);
                Accent = C(0x005FB8); AccentHover = C(0x1A6FC1); AccentText = C(0xFFFFFF);
                InputBack = C(0xFFFFFF); Error = C(0xC42B1C); Success = C(0x0F7B0F);
                MenuBack = C(0xF9F9F9); MenuHover = C(0xEAEAEA); MenuText = C(0x1B1B1B);
                MenuDisabled = C(0x9D9D9D); MenuSeparator = C(0xE3E3E3); MenuBorder = C(0xD5D5D5);
            }
            ToolStripManager.Renderer = new ModernMenuRenderer();
        }

        static bool ReadDarkMode()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static Font UiFont(float size, bool semibold)
        {
            return new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        /// <summary>Ciemny pasek tytułu i Mica (Windows 11).</summary>
        public static void ApplyWindowFrame(IntPtr hwnd)
        {
            Native.SetDwmInt(hwnd, 20, Dark ? 1 : 0);            // DWMWA_USE_IMMERSIVE_DARK_MODE
            if (IsWindows11) Native.SetDwmInt(hwnd, 38, 2);      // DWMWA_SYSTEMBACKDROP_TYPE = Mica
        }

        /// <summary>Zaokrąglone rogi i kolor ramki menu (Windows 11).</summary>
        public static void StyleMenuWindow(IntPtr hwnd)
        {
            if (!IsWindows11) return;
            Native.SetDwmInt(hwnd, 33, 3);                       // DWMWA_WINDOW_CORNER_PREFERENCE = ROUNDSMALL
            Native.SetDwmInt(hwnd, 34, ColorTranslator.ToWin32(MenuBorder));   // DWMWA_BORDER_COLOR
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0 || r.Width < d || r.Height < d)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // =========================================================================
    // Renderer menu w stylu Windows 11
    // =========================================================================
    sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernMenuRenderer() : base(new ProfessionalColorTable()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.MenuBack)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (Theme.IsWindows11 || !(e.ToolStrip is ToolStripDropDown)) return;   // na Win11 ramkę rysuje DWM
            using (var p = new Pen(Theme.MenuBorder))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        /// <summary>Pozycja właśnie uruchomiona przy otwartym menu - krótko podświetlana kolorem akcentu.</summary>
        public static ToolStripItem FlashItem;

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem item = e.Item;
            bool flash = item == FlashItem;
            if (!flash && (!item.Enabled || !(item.Selected || item.Pressed))) return;
            int m = Theme.Scale(4);
            var r = new Rectangle(m, 1, item.Width - 2 * m, item.Height - 2);
            Color color = flash ? Color.FromArgb(Theme.Dark ? 110 : 70, Theme.Accent) : Theme.MenuHover;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.RoundedRect(r, Theme.Scale(4)))
            using (var b = new SolidBrush(color))
                e.Graphics.FillPath(b, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            int m = Theme.Scale(8);
            using (var p = new Pen(Theme.MenuSeparator)) e.Graphics.DrawLine(p, m, y, e.Item.Width - m, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.MenuText : Theme.MenuDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null && !e.Item.Enabled ? Theme.MenuDisabled : Theme.MenuText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.MenuText, Math.Max(1.5f, 1.6f * Theme.DpiScale)))
            {
                e.Graphics.DrawLines(p, new[]
                {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f)
                });
            }
        }
    }

    // =========================================================================
    // Budowanie menu z folderu (na żądanie, przy każdym otwarciu)
    // =========================================================================
    static class MenuBuilder
    {
        public static TrayApp App;   // decyduje, czy po kliknięciu menu zostaje otwarte
        static Font boldFont;

        public static Font BoldFont
        {
            get { return boldFont ?? (boldFont = new Font(SystemFonts.MenuFont, FontStyle.Bold)); }
        }

        public static void PrepareDropDown(ToolStripDropDown dropDown)
        {
            dropDown.ImageScalingSize = SystemInformation.SmallIconSize;
            dropDown.ShowItemToolTips = true;
            dropDown.Padding = new Padding(0, Theme.Scale(4), 0, Theme.Scale(4));
            dropDown.HandleCreated += (s, e) => Theme.StyleMenuWindow(((Control)s).Handle);
            if (dropDown.IsHandleCreated) Theme.StyleMenuWindow(dropDown.Handle);
        }

        public static ToolStripMenuItem NewItem(string text)
        {
            string tip = null;
            if (text.Length > 70)
            {
                tip = text;
                text = text.Substring(0, 67) + "…";
            }
            var item = new ToolStripMenuItem(text.Replace("&", "&&"));
            item.ToolTipText = tip;
            int v = Theme.Scale(3);
            item.Padding = new Padding(item.Padding.Left, v, item.Padding.Right, v);
            return item;
        }

        public static ToolStripMenuItem Placeholder(string text)
        {
            ToolStripMenuItem item = NewItem(text);
            item.Enabled = false;
            return item;
        }

        public static void Clear(ToolStripItemCollection items)
        {
            var list = new List<ToolStripItem>();
            foreach (ToolStripItem i in items) list.Add(i);
            items.Clear();
            foreach (ToolStripItem i in list)
            {
                var dd = i as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems) Clear(dd.DropDownItems);
                Image img = i.Image;
                i.Dispose();
                if (img != null) img.Dispose();
            }
        }

        public static void Fill(ToolStripItemCollection items, string folder)
        {
            Clear(items);
            DirectoryInfo[] dirs;
            FileInfo[] files;
            try
            {
                var di = new DirectoryInfo(folder);
                if (!di.Exists)
                {
                    items.Add(Placeholder("(folder nie istnieje)"));
                    return;
                }
                dirs = di.GetDirectories().Where(d => (d.Attributes & FileAttributes.Hidden) == 0).ToArray();
                files = di.GetFiles().Where(f => (f.Attributes & FileAttributes.Hidden) == 0).ToArray();
            }
            catch (Exception ex)
            {
                Log.Error("odczyt folderu " + folder, ex);
                items.Add(Placeholder("(brak dostępu)"));
                return;
            }

            // sortowanie jak w Eksploratorze: "Plik 2" przed "Plik 10"
            Array.Sort(dirs, (a, b) => Native.StrCmpLogicalW(a.Name, b.Name));
            Array.Sort(files, (a, b) => Native.StrCmpLogicalW(a.Name, b.Name));

            foreach (DirectoryInfo d in dirs) items.Add(FolderItem(d.FullName, d.Name));
            foreach (FileInfo f in files)
            {
                string ext = f.Extension.ToLowerInvariant();
                string text = ext == ".lnk" || ext == ".url" ? Path.GetFileNameWithoutExtension(f.Name) : f.Name;
                items.Add(FileItem(f.FullName, text.Length > 0 ? text : f.Name));
            }
            if (dirs.Length + files.Length == 0) items.Add(Placeholder("(pusty)"));
        }

        public static ToolStripMenuItem FolderItem(string path, string text)
        {
            ToolStripMenuItem item = NewItem(text);
            item.Tag = path;
            item.Image = ShellIcons.GetBitmap(path);
            PrepareDropDown(item.DropDown);
            App.AttachLauncher(item.DropDown);
            item.DropDownItems.Add(Placeholder("…"));   // żeby była strzałka podmenu
            item.DropDownOpening += (s, e) =>
            {
                var mi = (ToolStripMenuItem)s;
                Fill(mi.DropDownItems, (string)mi.Tag);
            };
            item.Click += (s, e) =>
            {
                var mi = (ToolStripMenuItem)s;
                App.Activate(mi, (string)mi.Tag);
            };
            return item;
        }

        static ToolStripMenuItem FileItem(string path, string text)
        {
            ToolStripMenuItem item = NewItem(text);
            item.Tag = path;
            item.Image = ShellIcons.GetBitmap(path);
            item.Click += (s, e) =>
            {
                var mi = (ToolStripMenuItem)s;
                App.Activate(mi, (string)mi.Tag);
            };
            return item;
        }
    }

    // =========================================================================
    // Globalny hak myszy (WH_MOUSE_LL) na osobnym wątku. Działa tylko wtedy, gdy menu
    // zostaje otwarte po uruchomieniu pozycji: fokus ma już uruchomiony program, więc
    // kliknięcie poza menu trzeba wykryć samodzielnie. Hak nie blokuje kliknięć.
    // =========================================================================
    sealed class OutsideClickWatcher : IDisposable
    {
        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string moduleName);

        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207, WM_XBUTTONDOWN = 0x20B;
        const uint WM_QUIT = 0x12, GA_ROOT = 2;

        readonly Func<IntPtr, bool> isMenuWindow;
        readonly Action onOutsideClick;
        readonly LowLevelMouseProc proc;   // referencja chroni delegata przed GC
        readonly object sync = new object();
        Thread thread;
        uint threadId;

        public OutsideClickWatcher(Func<IntPtr, bool> isMenuWindow, Action onOutsideClick)
        {
            this.isMenuWindow = isMenuWindow;
            this.onOutsideClick = onOutsideClick;
            proc = HookProc;
        }

        public void Start()
        {
            lock (sync)
            {
                if (thread != null) return;
                var ready = new ManualResetEvent(false);
                thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "TrayFolderMenu mouse hook" };
                thread.Start();
                ready.WaitOne(2000);
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (thread == null) return;
                PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                thread = null;
            }
        }

        public void Dispose() { Stop(); }

        // Własna pętla komunikatów: hak nie zależy od obciążenia wątku UI
        void Run(ManualResetEvent ready)
        {
            MSG msg;
            PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);   // tworzy kolejkę komunikatów wątku
            threadId = GetCurrentThreadId();
            IntPtr hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            int error = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero)
            {
                Log.Write("Nie udało się założyć haka myszy (błąd " + error + ")");
                return;
            }
            try
            {
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
            }
            finally
            {
                UnhookWindowsHookEx(hook);
            }
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int m = wParam.ToInt32();
                if (m == WM_LBUTTONDOWN || m == WM_RBUTTONDOWN || m == WM_MBUTTONDOWN || m == WM_XBUTTONDOWN)
                {
                    try
                    {
                        POINT pt;
                        GetCursorPos(out pt);
                        IntPtr root = GetAncestor(WindowFromPoint(pt), GA_ROOT);
                        if (!isMenuWindow(root)) onOutsideClick();
                    }
                    catch
                    {
                        // hak musi zawsze przekazać zdarzenie dalej
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }

    // =========================================================================
    // Ukryte okno: odbiera WM_HOTKEY i służy do przełączania na wątek UI
    // =========================================================================
    sealed class MessageWindow : Form
    {
        public const int HotkeyId = 1;
        readonly TrayApp app;

        public MessageWindow(TrayApp app)
        {
            this.app = app;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(0, 0);
            IntPtr unused = Handle;   // wymuś utworzenie uchwytu bez pokazywania okna
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                app.OnHotkey();
                return;
            }
            base.WndProc(ref m);
        }
    }

    // =========================================================================
    // Ikona w zasobniku dla jednego folderu
    // =========================================================================
    sealed class RootTray : IDisposable
    {
        public readonly string Folder;
        public readonly NotifyIcon Notify;
        readonly Icon ownedIcon;
        readonly ContextMenuStrip menu;

        public RootTray(TrayApp app, string folder)
        {
            Folder = folder;
            menu = app.NewLauncherMenu();
            ownedIcon = Directory.Exists(folder) ? ShellIcons.GetIcon(folder, false) : null;
            Notify = new NotifyIcon { Icon = ownedIcon ?? app.AppIconSmall, Text = TrayApp.TrayText(folder) };
            Notify.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (app.ToggleClosed(menu)) return;   // kliknięcie ikony przy otwartym menu = zamknij
                    MenuBuilder.Fill(menu.Items, Folder);
                    app.ShowFromTray(Notify, menu);
                }
                else if (e.Button == MouseButtons.Right)
                {
                    app.ShowFromTray(Notify, app.BuildAdminMenu(Folder));
                }
            };
            Notify.Visible = true;
        }

        public void Dispose()
        {
            Notify.Visible = false;
            Notify.Dispose();
            if (ownedIcon != null) ownedIcon.Dispose();
            MenuBuilder.Clear(menu.Items);
            menu.Dispose();
        }
    }

    // =========================================================================
    // Aplikacja w zasobniku
    // =========================================================================
    sealed class TrayApp : ApplicationContext
    {
        static readonly MethodInfo ShowContextMenuMethod =
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly MessageWindow msgWin;
        readonly Dictionary<string, RootTray> trays = new Dictionary<string, RootTray>(StringComparer.OrdinalIgnoreCase);
        readonly ContextMenuStrip adminMenu, hotkeyMenu;
        readonly EventWaitHandle showEvt, exitEvt;
        readonly ManualResetEvent stopSignals = new ManualResetEvent(false);
        readonly Thread signalThread;
        readonly List<ToolStripDropDown> openLaunchers = new List<ToolStripDropDown>();
        volatile IntPtr[] openMenuHandles = new IntPtr[0];
        readonly OutsideClickWatcher watcher;
        readonly System.Windows.Forms.Timer flashTimer;
        bool sticky;                     // menu zostaje otwarte mimo utraty fokusu
        ToolStripDropDown lastClosedTop;
        int lastClosedTick;
        readonly AppConfig config;
        NotifyIcon fallback;
        SettingsForm settings;
        IntPtr previousForeground;
        bool exiting;

        public readonly Icon AppIconSmall, AppIconLarge;
        public string HotkeyError { get; private set; }
        public AppConfig Config { get { return config; } }

        public TrayApp(bool showSettings, EventWaitHandle showEvt, EventWaitHandle exitEvt)
        {
            this.showEvt = showEvt;
            this.exitEvt = exitEvt;
            Theme.Refresh();
            AppIconSmall = ShellIcons.GetIcon(AppPaths.CurrentExe, false) ?? (Icon)SystemIcons.Application.Clone();
            AppIconLarge = ShellIcons.GetIcon(AppPaths.CurrentExe, true) ?? (Icon)SystemIcons.Application.Clone();

            msgWin = new MessageWindow(this);
            config = AppConfig.Load();
            MenuBuilder.App = this;
            watcher = new OutsideClickWatcher(IsOpenMenuWindow, OnOutsideClick);
            flashTimer = new System.Windows.Forms.Timer { Interval = 350 };
            flashTimer.Tick += (s, e) =>
            {
                flashTimer.Stop();
                ToolStripItem flashed = ModernMenuRenderer.FlashItem;
                ModernMenuRenderer.FlashItem = null;
                if (flashed != null && !flashed.IsDisposed) flashed.Invalidate();
            };
            adminMenu = NewMenu();
            hotkeyMenu = NewLauncherMenu();
            hotkeyMenu.Closed += OnHotkeyMenuClosed;

            SyncTrays();
            ApplyHotkey(true);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            signalThread = new Thread(SignalLoop) { IsBackground = true, Name = "TrayFolderMenu signals" };
            signalThread.Start();

            Log.Write("Start " + Program.Version + ": " + AppPaths.CurrentExe);
            if (showSettings) ShowSettings();
        }

        public ContextMenuStrip NewMenu()
        {
            var menu = new ContextMenuStrip();
            MenuBuilder.PrepareDropDown(menu);
            return menu;
        }

        public static string TrayText(string folder)
        {
            string name = AppPaths.LeafName(folder);
            return name.Length > 63 ? name.Substring(0, 60) + "..." : name;
        }

        // --- Sygnały od innych procesów (drugie uruchomienie, /exit, /install) --
        void SignalLoop()
        {
            var handles = new WaitHandle[] { showEvt, exitEvt, stopSignals };
            while (true)
            {
                int index;
                try { index = WaitHandle.WaitAny(handles); }
                catch { return; }
                if (index == 2) return;
                Action action;
                if (index == 0) action = ShowSettings;
                else action = ExitApp;
                try { msgWin.BeginInvoke(action); }
                catch { return; }
            }
        }

        // --- Menu z pozycjami: co się dzieje po kliknięciu -------------------
        public ContextMenuStrip NewLauncherMenu()
        {
            ContextMenuStrip menu = NewMenu();
            AttachLauncher(menu);
            return menu;
        }

        public void AttachLauncher(ToolStripDropDown dropDown)
        {
            dropDown.Closing += OnLauncherClosing;
            dropDown.Opened += OnLauncherOpened;
            dropDown.Closed += OnLauncherClosed;
        }

        /// <summary>Czy po tym kliknięciu menu ma zostać otwarte: ustawienie, odwracane przez Ctrl.</summary>
        bool KeepOpenNow()
        {
            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;   // VK_CONTROL, niezależnie od fokusu
            return config.KeepMenuOpen != ctrl;
        }

        void OnLauncherClosing(object sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && KeepOpenNow())
                e.Cancel = true;
            else if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange && sticky)
                e.Cancel = true;   // uruchomiony program przejął fokus, a menu ma zostać
        }

        void OnLauncherOpened(object sender, EventArgs e)
        {
            var dropDown = (ToolStripDropDown)sender;
            if (!openLaunchers.Contains(dropDown)) openLaunchers.Add(dropDown);
            UpdateMenuHandles();
        }

        void OnLauncherClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            var dropDown = (ToolStripDropDown)sender;
            openLaunchers.Remove(dropDown);
            UpdateMenuHandles();
            if (dropDown.OwnerItem != null) return;

            // zamknięto całe menu: koniec trybu "zostaje otwarte"
            lastClosedTop = dropDown;
            lastClosedTick = Environment.TickCount;
            sticky = false;
            watcher.Stop();
            ModernMenuRenderer.FlashItem = null;
        }

        void UpdateMenuHandles()
        {
            openMenuHandles = openLaunchers.Where(d => d.IsHandleCreated).Select(d => d.Handle).ToArray();
        }

        bool IsOpenMenuWindow(IntPtr rootWindow)   // wątek haka
        {
            return Array.IndexOf(openMenuHandles, rootWindow) >= 0;
        }

        void OnOutsideClick()   // wątek haka
        {
            try { msgWin.BeginInvoke(new Action(CloseLauncherMenus)); }
            catch { }
        }

        public void CloseLauncherMenus()
        {
            ToolStripDropDown[] open = openLaunchers.ToArray();
            for (int i = open.Length - 1; i >= 0; i--) open[i].Close(ToolStripDropDownCloseReason.CloseCalled);
        }

        /// <summary>Kliknięcie ikony, gdy jej menu jest otwarte (albo właśnie zamknęło się tym kliknięciem) - nie otwieraj ponownie.</summary>
        public bool ToggleClosed(ToolStripDropDown menu)
        {
            if (menu.Visible)
            {
                CloseLauncherMenus();
                return true;
            }
            return menu == lastClosedTop && unchecked(Environment.TickCount - lastClosedTick) < 400;
        }

        public void Activate(ToolStripMenuItem item, string target)
        {
            if (KeepOpenNow())
            {
                if (!sticky)
                {
                    sticky = true;
                    watcher.Start();
                }
                Flash(item);
            }
            else
            {
                CloseLauncherMenus();
            }
            Launch(target);
        }

        void Flash(ToolStripItem item)
        {
            ToolStripItem previous = ModernMenuRenderer.FlashItem;
            ModernMenuRenderer.FlashItem = item;
            if (previous != null && !previous.IsDisposed) previous.Invalidate();
            item.Invalidate();
            flashTimer.Stop();
            flashTimer.Start();
        }

        // --- Ikony w zasobniku -----------------------------------------------
        public void SyncTrays()
        {
            List<string> wanted = config.Folders.Where(f => f.Enabled).Select(f => f.Path).ToList();

            foreach (string key in trays.Keys.ToList())
            {
                if (!wanted.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    trays[key].Dispose();
                    trays.Remove(key);
                }
            }
            foreach (string path in wanted)
            {
                if (!trays.ContainsKey(path)) trays[path] = new RootTray(this, path);
            }

            if (trays.Count == 0 && fallback == null)
            {
                fallback = new NotifyIcon { Icon = AppIconSmall, Text = "TrayFolderMenu – kliknij, aby dodać folder" };
                fallback.MouseUp += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left) ShowSettings();
                    else if (e.Button == MouseButtons.Right) ShowFromTray(fallback, BuildAdminMenu(null));
                };
                fallback.Visible = true;
            }
            else if (trays.Count > 0 && fallback != null)
            {
                fallback.Visible = false;
                fallback.Dispose();
                fallback = null;
            }
        }

        public void ShowFromTray(NotifyIcon notifyIcon, ContextMenuStrip menu)
        {
            notifyIcon.ContextMenuStrip = menu;
            try
            {
                // prywatna metoda NotifyIcon ustawia fokus, więc menu zamyka się po kliknięciu obok
                ShowContextMenuMethod.Invoke(notifyIcon, null);
            }
            catch (Exception ex)
            {
                Log.Error("pokazywanie menu", ex);
                Native.SetForegroundWindow(msgWin.Handle);
                menu.Show(Cursor.Position);
            }
            finally
            {
                notifyIcon.ContextMenuStrip = null;
            }
        }

        public ContextMenuStrip BuildAdminMenu(string folder)
        {
            MenuBuilder.Clear(adminMenu.Items);

            ToolStripMenuItem settingsItem = MenuBuilder.NewItem("Ustawienia…");
            settingsItem.Font = MenuBuilder.BoldFont;
            settingsItem.Click += (s, e) => ShowSettings();
            adminMenu.Items.Add(settingsItem);

            if (folder != null)
            {
                ToolStripMenuItem open = MenuBuilder.NewItem("Otwórz folder w Eksploratorze");
                open.Click += (s, e) => Launch(folder);
                adminMenu.Items.Add(open);
            }

            ToolStripMenuItem keep = MenuBuilder.NewItem("Nie zamykaj menu po kliknięciu");
            keep.Checked = config.KeepMenuOpen;
            keep.Click += (s, e) =>
            {
                config.KeepMenuOpen = !config.KeepMenuOpen;
                SaveConfigOnly();
                if (settings != null && !settings.IsDisposed) settings.RefreshState();
            };
            adminMenu.Items.Add(keep);

            ToolStripMenuItem auto = MenuBuilder.NewItem("Uruchamiaj przy logowaniu");
            auto.Checked = Autostart.IsEnabled;
            auto.Click += (s, e) =>
            {
                try { Autostart.Set(!Autostart.IsEnabled, AppPaths.CurrentExe); }
                catch (Exception ex) { Log.Error("autostart", ex); }
                if (settings != null && !settings.IsDisposed) settings.RefreshState();
            };
            adminMenu.Items.Add(auto);

            adminMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem quit = MenuBuilder.NewItem("Zamknij TrayFolderMenu");
            quit.Click += (s, e) => ExitApp();
            adminMenu.Items.Add(quit);
            return adminMenu;
        }

        // --- Skrót klawiszowy ------------------------------------------------
        public bool ApplyHotkey(bool notifyOnFailure)
        {
            Native.UnregisterHotKey(msgWin.Handle, MessageWindow.HotkeyId);
            HotkeyError = null;
            if (!config.HotkeyEnabled) return true;

            uint mods;
            Keys key;
            if (!Hotkey.TryParse(config.Hotkey, out mods, out key))
            {
                HotkeyError = "Nieprawidłowy skrót w konfiguracji: " + config.Hotkey;
            }
            else if (!Native.RegisterHotKey(msgWin.Handle, MessageWindow.HotkeyId, mods | Hotkey.MOD_NOREPEAT, (uint)key))
            {
                HotkeyError = "Skrót " + config.Hotkey + " jest zajęty przez inną aplikację lub system.";
            }

            if (HotkeyError == null) return true;
            Log.Write(HotkeyError);
            if (notifyOnFailure) Balloon("Skrót klawiszowy", HotkeyError, ToolTipIcon.Warning);
            return false;
        }

        /// <summary>Wyrejestrowuje skrót na czas nagrywania nowej kombinacji w oknie ustawień.</summary>
        public void SuspendHotkey()
        {
            Native.UnregisterHotKey(msgWin.Handle, MessageWindow.HotkeyId);
        }

        public bool TrySetHotkey(string combo, out string error)
        {
            string previous = config.Hotkey;
            config.Hotkey = combo;
            if (!ApplyHotkey(false))
            {
                error = HotkeyError;
                config.Hotkey = previous;
                ApplyHotkey(false);
                return false;
            }
            error = null;
            SaveConfigOnly();
            return true;
        }

        public void OnHotkey()
        {
            if (openLaunchers.Count > 0)   // ponowne naciśnięcie zamyka menu
            {
                CloseLauncherMenus();
                return;
            }
            List<FolderEntry> roots = config.Folders.Where(f => f.Enabled).ToList();
            if (roots.Count == 0)
            {
                ShowSettings();
                return;
            }
            if (roots.Count == 1)
            {
                MenuBuilder.Fill(hotkeyMenu.Items, roots[0].Path);
            }
            else
            {
                // kilka folderów: każdy jako podmenu
                MenuBuilder.Clear(hotkeyMenu.Items);
                foreach (FolderEntry root in roots) hotkeyMenu.Items.Add(MenuBuilder.FolderItem(root.Path, AppPaths.LeafName(root.Path)));
            }
            previousForeground = Native.GetForegroundWindow();
            Native.SetForegroundWindow(msgWin.Handle);
            hotkeyMenu.Show(Cursor.Position);
        }

        void OnHotkeyMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            // Esc: oddaj fokus oknu, w którym użytkownik pracował
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard && previousForeground != IntPtr.Zero)
                Native.SetForegroundWindow(previousForeground);
            previousForeground = IntPtr.Zero;
        }

        // --- Akcje -----------------------------------------------------------
        public void Launch(string target)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, ErrorDialog = false })) { }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return;   // anulowany monit UAC
                ReportLaunchError(target, ex);
            }
            catch (Exception ex)
            {
                ReportLaunchError(target, ex);
            }
        }

        void ReportLaunchError(string target, Exception ex)
        {
            Log.Error("uruchamianie " + target, ex);
            Balloon("Nie można uruchomić", Path.GetFileName(target) + ": " + ex.Message, ToolTipIcon.Error);
        }

        public void Balloon(string title, string text, ToolTipIcon icon)
        {
            NotifyIcon target = trays.Values.Select(t => t.Notify).FirstOrDefault() ?? fallback;
            if (target == null || string.IsNullOrEmpty(text)) return;
            if (text.Length > 250) text = text.Substring(0, 250);
            target.ShowBalloonTip(5000, title, text, icon);
        }

        public void ShowSettings()
        {
            if (exiting) return;
            if (settings == null || settings.IsDisposed) settings = new SettingsForm(this);
            settings.RefreshState();
            if (!settings.Visible) settings.Show();
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
            Native.SetForegroundWindow(settings.Handle);
        }

        public void SaveConfig()
        {
            SaveConfigOnly();
            SyncTrays();
        }

        public void SaveConfigOnly()
        {
            try { config.Save(); }
            catch (Exception ex)
            {
                Log.Error("zapis konfiguracji", ex);
                MessageBox.Show("Nie udało się zapisać ustawień:\n" + ex.Message, "TrayFolderMenu",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.VisualStyle) return;
            try
            {
                msgWin.BeginInvoke(new Action(() =>
                {
                    bool wasDark = Theme.Dark;
                    Theme.Refresh();
                    if (wasDark != Theme.Dark && settings != null && !settings.IsDisposed) settings.ApplyTheme();
                }));
            }
            catch { }
        }

        public void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            Log.Write("Zamknięcie");
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                stopSignals.Set();
                watcher.Dispose();
                flashTimer.Dispose();
                Native.UnregisterHotKey(msgWin.Handle, MessageWindow.HotkeyId);
                foreach (RootTray t in trays.Values) t.Dispose();
                trays.Clear();
                if (fallback != null)
                {
                    fallback.Visible = false;
                    fallback.Dispose();
                }
                if (settings != null) settings.Dispose();
                MenuBuilder.Clear(adminMenu.Items);
                MenuBuilder.Clear(hotkeyMenu.Items);
                adminMenu.Dispose();
                hotkeyMenu.Dispose();
                msgWin.Dispose();
                AppIconSmall.Dispose();
                AppIconLarge.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // =========================================================================
    // Kontrolki okna ustawień
    // =========================================================================
    /// <summary>Karta z zaokrąglonymi rogami (jak w Ustawieniach Windows 11).</summary>
    sealed class CardPanel : TableLayoutPanel
    {
        public Color CardColor = Color.White;
        public Color BorderColor = Color.Gainsboro;

        public CardPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(Theme.Scale(18), Theme.Scale(14), Theme.Scale(18), Theme.Scale(16));
            Margin = new Padding(0, 0, 0, Theme.Scale(10));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.WindowBack);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Theme.Scale(8)))
            using (var fill = new SolidBrush(CardColor))
            using (var pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Panel z cienką ramką - obudowa listy i pola skrótu.</summary>
    sealed class BorderPanel : Panel
    {
        public Color BorderColor = Color.Gray;

        public BorderPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Padding = new Padding(1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(BorderColor)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Pole nagrywające kombinację klawiszy.</summary>
    sealed class HotkeyBox : TextBox
    {
        public event Action<uint, Keys> Captured;
        public event Action<string> Hint;
        public event Action Cancelled;

        public HotkeyBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            Cursor = Cursors.Hand;
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Native.HideCaret(Handle);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN) return base.ProcessCmdKey(ref msg, keyData);

            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0;
            bool alt = (keyData & Keys.Alt) != 0;
            bool shift = (keyData & Keys.Shift) != 0;
            bool win = (Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0;

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin || key == Keys.None)
            {
                Text = Preview(ctrl, alt, shift, win);
                return true;
            }
            if (key == Keys.Escape && !ctrl && !alt && !shift && !win)
            {
                if (Cancelled != null) Cancelled();
                return true;
            }

            uint mods = (ctrl ? Hotkey.MOD_CONTROL : 0u) | (alt ? Hotkey.MOD_ALT : 0u) |
                        (shift ? Hotkey.MOD_SHIFT : 0u) | (win ? Hotkey.MOD_WIN : 0u);
            if (!Hotkey.IsValid(mods, key))
            {
                if (Hint != null) Hint("Dodaj Ctrl, Alt lub Win (same F1–F24 też są dozwolone).");
                return true;
            }
            if (Captured != null) Captured(mods, key);
            return true;
        }

        static string Preview(bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            return parts.Count == 0 ? "Naciśnij kombinację…" : string.Join("+", parts) + "+…";
        }
    }

    // =========================================================================
    // Okno ustawień
    // =========================================================================
    sealed class SettingsForm : Form
    {
        readonly TrayApp app;
        readonly TableLayoutPanel root;
        readonly List<CardPanel> cards = new List<CardPanel>();
        readonly List<Label> titles = new List<Label>();
        readonly List<Label> subs = new List<Label>();
        readonly List<Button> buttons = new List<Button>();
        readonly List<LinkLabel> links = new List<LinkLabel>();
        readonly CheckedListBox list;
        readonly BorderPanel listBorder, hotkeyBorder;
        readonly HotkeyBox hotkeyBox;
        readonly CheckBox chkHotkey, chkAutostart, chkKeepOpen;
        readonly Label lblTitle, lblHotkeyStatus, lblLocation;
        readonly Button btnAdd, btnRemove, btnOpen, btnDefaultHotkey, btnInstall, btnQuit, btnDone;
        readonly LinkLabel lnkLog, lnkConfig;
        bool loading;
        string statusOverride;
        bool statusIsError;

        static int S(int px) { return Theme.Scale(px); }

        sealed class FolderView
        {
            public readonly FolderEntry Entry;
            public FolderView(FolderEntry entry) { Entry = entry; }
            public override string ToString()
            {
                return Directory.Exists(Entry.Path) ? Entry.Path : Entry.Path + "   (nie istnieje)";
            }
        }

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;   // rozmiary liczone ręcznie przez Theme.Scale
            Font = Theme.UiFont(9f, false);
            Text = "TrayFolderMenu – ustawienia";
            Icon = app.AppIconLarge;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            root = new TableLayoutPanel
            {
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(S(20), S(16)),
                Margin = Padding.Empty
            };

            // --- Nagłówek ---
            lblTitle = NewLabel("TrayFolderMenu", Theme.UiFont(15f, true));
            lblTitle.Margin = Padding.Empty;
            root.Controls.Add(lblTitle);
            Label subtitle = NewLabel("Kaskadowe menu skrótów w zasobniku · wersja " + Program.Version, null);
            subtitle.Margin = new Padding(0, 0, 0, S(12));
            subs.Add(subtitle);
            root.Controls.Add(subtitle);

            // --- Karta: foldery ---
            CardPanel cFolders = NewCard(2);
            AddTitle(cFolders, "Foldery menu", 0, 2);
            AddSub(cFolders, "Każdy zaznaczony folder ma własną ikonę w zasobniku, a jego podfoldery stają się podmenu. " +
                             "Folder możesz też przeciągnąć tutaj z Eksploratora.", 1, 2);
            list = new CheckedListBox
            {
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                IntegralHeight = false,
                Dock = DockStyle.Fill,
                AllowDrop = true,
                HorizontalScrollbar = true
            };
            listBorder = new BorderPanel { Size = new Size(S(420), S(150)), Margin = new Padding(0, S(8), S(12), 0) };
            listBorder.Controls.Add(list);
            cFolders.Controls.Add(listBorder, 0, 2);
            var folderButtons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, S(8), 0, 0)
            };
            btnAdd = NewButton("Dodaj…", false);
            btnRemove = NewButton("Usuń", false);
            btnOpen = NewButton("Otwórz", false);
            folderButtons.Controls.AddRange(new Control[] { btnAdd, btnRemove, btnOpen });
            cFolders.Controls.Add(folderButtons, 1, 2);
            root.Controls.Add(cFolders);

            // --- Karta: zachowanie menu ---
            CardPanel cMenu = NewCard(1);
            AddTitle(cMenu, "Zachowanie menu", 0, 1);
            chkKeepOpen = NewCheck("Nie zamykaj menu po uruchomieniu pozycji");
            cMenu.Controls.Add(chkKeepOpen, 0, 1);
            AddSub(cMenu, "Menu zostaje otwarte, więc możesz uruchomić kilka pozycji z jednego otwarcia. Zamyka się " +
                          "kliknięciem poza menu, ponownym kliknięciem ikony lub skrótem klawiszowym. " +
                          "Ctrl+klik odwraca to zachowanie dla pojedynczego kliknięcia.", 2, 1);
            root.Controls.Add(cMenu);

            // --- Karta: skrót klawiszowy ---
            CardPanel cHotkey = NewCard(2);
            AddTitle(cHotkey, "Skrót klawiszowy", 0, 2);
            chkHotkey = NewCheck("Otwieraj menu skrótem klawiszowym (menu pojawia się przy kursorze myszy)");
            cHotkey.Controls.Add(chkHotkey, 0, 1);
            cHotkey.SetColumnSpan(chkHotkey, 2);
            hotkeyBox = new HotkeyBox { BorderStyle = BorderStyle.None, Dock = DockStyle.Fill, Font = Theme.UiFont(10f, true) };
            hotkeyBorder = new BorderPanel { Padding = new Padding(S(8), S(6), S(8), S(6)), Margin = new Padding(0, 0, S(10), 0) };
            hotkeyBorder.Size = new Size(S(220), hotkeyBox.PreferredHeight + S(12) + 2);
            hotkeyBorder.Controls.Add(hotkeyBox);
            cHotkey.Controls.Add(hotkeyBorder, 0, 2);
            btnDefaultHotkey = NewButton("Przywróć " + AppConfig.DefaultHotkey, false);
            btnDefaultHotkey.Margin = Padding.Empty;
            btnDefaultHotkey.Anchor = AnchorStyles.Left;
            cHotkey.Controls.Add(btnDefaultHotkey, 1, 2);
            lblHotkeyStatus = AddSub(cHotkey, "", 3, 2);
            lblHotkeyStatus.Margin = new Padding(0, S(8), 0, 0);
            root.Controls.Add(cHotkey);

            // --- Karta: uruchamianie i instalacja ---
            CardPanel cStart = NewCard(1);
            AddTitle(cStart, "Uruchamianie i instalacja", 0, 1);
            chkAutostart = NewCheck("Uruchamiaj przy logowaniu do Windows");
            cStart.Controls.Add(chkAutostart, 0, 1);
            lblLocation = AddSub(cStart, "", 2, 1);
            btnInstall = NewButton("", false);
            btnInstall.Margin = new Padding(0, S(6), 0, 0);
            btnInstall.Anchor = AnchorStyles.Left;
            cStart.Controls.Add(btnInstall, 0, 3);
            root.Controls.Add(cStart);

            // --- Stopka ---
            var footer = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, S(4), 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var linkPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            lnkLog = NewLink("Log");
            lnkConfig = NewLink("Folder ustawień");
            linkPanel.Controls.Add(lnkLog);
            linkPanel.Controls.Add(lnkConfig);
            var buttonPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            btnQuit = NewButton("Zamknij aplikację", false);
            btnQuit.Margin = new Padding(0, 0, S(8), 0);
            btnDone = NewButton("Gotowe", true);
            btnDone.Margin = Padding.Empty;
            buttonPanel.Controls.Add(btnQuit);
            buttonPanel.Controls.Add(btnDone);
            footer.Controls.Add(linkPanel, 0, 0);
            footer.Controls.Add(buttonPanel, 1, 0);
            root.Controls.Add(footer);

            Controls.Add(root);
            AcceptButton = btnDone;
            CancelButton = btnDone;
            WireEvents();
            ApplyTheme();
            ResumeLayout(true);
            root.PerformLayout();
            UpdateClientSize();
            root.SizeChanged += (s, e) => UpdateClientSize();
            ActiveControl = btnDone;
        }

        // --- Fabryki kontrolek ---------------------------------------------------
        CardPanel NewCard(int columns)
        {
            var card = new CardPanel { ColumnCount = columns, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            cards.Add(card);
            return card;
        }

        static Label NewLabel(string text, Font font)
        {
            var label = new Label { Text = text, AutoSize = true, UseMnemonic = false };
            if (font != null) label.Font = font;
            return label;
        }

        Label AddTitle(CardPanel card, string text, int row, int span)
        {
            Label label = NewLabel(text, Theme.UiFont(10.5f, true));
            label.Margin = new Padding(0, 0, 0, S(2));
            card.Controls.Add(label, 0, row);
            card.SetColumnSpan(label, span);
            titles.Add(label);
            return label;
        }

        Label AddSub(CardPanel card, string text, int row, int span)
        {
            Label label = NewLabel(text, null);
            label.MaximumSize = new Size(S(540), 0);
            label.Margin = new Padding(0, 0, 0, S(2));
            card.Controls.Add(label, 0, row);
            card.SetColumnSpan(label, span);
            subs.Add(label);
            return label;
        }

        CheckBox NewCheck(string text)
        {
            return new CheckBox { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, S(6), 0, S(8)) };
        }

        Button NewButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(S(104), S(32)),
                Padding = new Padding(S(10), 0, S(10), 0),
                Margin = new Padding(0, 0, 0, S(6)),
                UseMnemonic = false,
                Tag = primary ? "primary" : null
            };
            buttons.Add(b);
            return b;
        }

        LinkLabel NewLink(string text)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, S(8), S(14), 0) };
            links.Add(link);
            return link;
        }

        void UpdateClientSize()
        {
            ClientSize = new Size(root.Right + S(20), root.Bottom + S(16));
        }

        // --- Motyw -------------------------------------------------------------
        public void ApplyTheme()
        {
            BackColor = Theme.WindowBack;
            ForeColor = Theme.Text;
            lblTitle.ForeColor = Theme.Text;
            foreach (CardPanel c in cards)
            {
                c.BackColor = Theme.Card;
                c.ForeColor = Theme.Text;
                c.CardColor = Theme.Card;
                c.BorderColor = Theme.CardBorder;
                c.Invalidate();
            }
            foreach (Label l in titles) l.ForeColor = Theme.Text;
            foreach (Label l in subs) l.ForeColor = Theme.SubText;
            foreach (Button b in buttons) StyleButton(b, (b.Tag as string) == "primary");
            foreach (CheckBox cb in new[] { chkHotkey, chkAutostart, chkKeepOpen }) StyleCheck(cb);
            foreach (LinkLabel l in links)
            {
                l.LinkColor = Theme.Accent;
                l.ActiveLinkColor = Theme.AccentHover;
                l.VisitedLinkColor = Theme.Accent;
            }
            foreach (BorderPanel p in new[] { listBorder, hotkeyBorder })
            {
                p.BackColor = Theme.InputBack;
                p.BorderColor = Theme.ButtonBorder;
                p.Invalidate();
            }
            list.BackColor = Theme.InputBack;
            list.ForeColor = Theme.Text;
            hotkeyBox.BackColor = Theme.InputBack;
            hotkeyBox.ForeColor = Theme.Text;
            if (IsHandleCreated) Theme.ApplyWindowFrame(Handle);
            if (list.IsHandleCreated) Native.SetTheme(list.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer");
            UpdateHotkeyStatus();
            Invalidate(true);
        }

        static void StyleButton(Button b, bool primary)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 1;
            b.BackColor = primary ? Theme.Accent : Theme.ButtonBack;
            b.ForeColor = primary ? Theme.AccentText : Theme.Text;
            b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = primary ? Theme.AccentHover : Theme.ButtonHover;
        }

        static void StyleCheck(CheckBox cb)
        {
            cb.ForeColor = Theme.Text;
            cb.FlatStyle = Theme.Dark ? FlatStyle.Flat : FlatStyle.Standard;
            cb.FlatAppearance.BorderColor = Theme.SubText;
            cb.FlatAppearance.CheckedBackColor = Theme.Card;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindowFrame(Handle);
        }

        // --- Stan --------------------------------------------------------------
        public void RefreshState()
        {
            loading = true;
            try
            {
                object selected = list.SelectedItem;
                string selectedPath = selected is FolderView ? ((FolderView)selected).Entry.Path : null;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (FolderEntry f in app.Config.Folders)
                {
                    int i = list.Items.Add(new FolderView(f));
                    list.SetItemChecked(i, f.Enabled);
                    if (selectedPath != null && AppPaths.SamePath(selectedPath, f.Path)) list.SelectedIndex = i;
                }
                list.EndUpdate();

                chkKeepOpen.Checked = app.Config.KeepMenuOpen;
                chkHotkey.Checked = app.Config.HotkeyEnabled;
                if (!hotkeyBox.Focused) hotkeyBox.Text = app.Config.Hotkey;
                hotkeyBox.Enabled = btnDefaultHotkey.Enabled = app.Config.HotkeyEnabled;
                chkAutostart.Checked = Autostart.IsEnabled;

                if (AppPaths.IsInstalledCopy)
                {
                    lblLocation.Text = "Zainstalowano dla bieżącego użytkownika w " + AppPaths.InstallDir +
                                       ". Program jest w menu Start i na liście aplikacji Windows.";
                    btnInstall.Text = "Odinstaluj…";
                }
                else if (Installer.IsInstalled)
                {
                    lblLocation.Text = "Działa kopia przenośna: " + AppPaths.CurrentExe +
                                       ". Zainstalowana wersja jest w " + AppPaths.InstallDir + ".";
                    btnInstall.Text = "Zaktualizuj instalację tą wersją";
                }
                else
                {
                    lblLocation.Text = "Wersja przenośna: " + AppPaths.CurrentExe + ". Instalacja kopiuje program do " +
                                       AppPaths.InstallDir + ", dodaje go do menu Start, autostartu i listy aplikacji " +
                                       "(bez uprawnień administratora).";
                    btnInstall.Text = "Zainstaluj dla bieżącego użytkownika";
                }
                UpdateHotkeyStatus();
                UpdateButtons();
            }
            finally
            {
                loading = false;
            }
        }

        void UpdateButtons()
        {
            btnRemove.Enabled = btnOpen.Enabled = list.SelectedItem != null;
        }

        void UpdateHotkeyStatus()
        {
            string text;
            Color color;
            if (statusOverride != null)
            {
                text = statusOverride;
                color = statusIsError ? Theme.Error : Theme.Success;
                statusOverride = null;
            }
            else if (!app.Config.HotkeyEnabled)
            {
                text = "Skrót klawiszowy jest wyłączony.";
                color = Theme.SubText;
            }
            else if (app.HotkeyError != null)
            {
                text = app.HotkeyError;
                color = Theme.Error;
            }
            else
            {
                text = "Kliknij pole i naciśnij nową kombinację (Esc anuluje). Skrót działa w całym systemie, " +
                       "a ponowne naciśnięcie zamyka menu.";
                color = Theme.SubText;
            }
            lblHotkeyStatus.Text = text;
            lblHotkeyStatus.ForeColor = color;
        }

        FolderView SelectedFolder { get { return list.SelectedItem as FolderView; } }

        void AddFolders(IEnumerable<string> paths)
        {
            string last = null;
            bool changed = false;
            foreach (string p in paths)
            {
                if (!Directory.Exists(p)) continue;
                if (app.Config.AddFolder(p)) changed = true;
                last = p;
            }
            if (changed) app.SaveConfig();
            RefreshState();
            if (last != null)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (AppPaths.SamePath(((FolderView)list.Items[i]).Entry.Path, last)) list.SelectedIndex = i;
                }
            }
        }

        // --- Zdarzenia ---------------------------------------------------------
        void WireEvents()
        {
            list.ItemCheck += (s, e) =>
            {
                if (loading) return;
                var view = list.Items[e.Index] as FolderView;
                if (view == null) return;
                view.Entry.Enabled = e.NewValue == CheckState.Checked;
                BeginInvoke(new Action(app.SaveConfig));
            };
            list.SelectedIndexChanged += (s, e) => UpdateButtons();
            list.DragEnter += (s, e) =>
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            };
            list.DragDrop += (s, e) =>
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null) AddFolders(paths);
            };
            list.HandleCreated += (s, e) => Native.SetTheme(list.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer");

            btnAdd.Click += (s, e) =>
            {
                string path = FolderPicker.Pick(this, "Wybierz folder dla menu");
                if (path != null) AddFolders(new[] { path });
            };
            btnRemove.Click += (s, e) =>
            {
                FolderView view = SelectedFolder;
                if (view == null) return;
                app.Config.Folders.Remove(view.Entry);
                app.SaveConfig();
                RefreshState();
            };
            btnOpen.Click += (s, e) =>
            {
                FolderView view = SelectedFolder;
                if (view != null) app.Launch(view.Entry.Path);
            };

            chkKeepOpen.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                app.Config.KeepMenuOpen = chkKeepOpen.Checked;
                app.SaveConfigOnly();
            };

            chkHotkey.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                app.Config.HotkeyEnabled = chkHotkey.Checked;
                app.SaveConfigOnly();
                app.ApplyHotkey(false);
                hotkeyBox.Enabled = btnDefaultHotkey.Enabled = chkHotkey.Checked;
                UpdateHotkeyStatus();
            };

            hotkeyBox.Enter += (s, e) =>
            {
                app.SuspendHotkey();   // inaczej obecny skrót zostałby przechwycony przez system
                hotkeyBox.Text = "Naciśnij kombinację…";
                lblHotkeyStatus.Text = "Naciśnij nową kombinację klawiszy. Esc anuluje.";
                lblHotkeyStatus.ForeColor = Theme.SubText;
            };
            hotkeyBox.Leave += (s, e) =>
            {
                app.ApplyHotkey(false);
                hotkeyBox.Text = app.Config.Hotkey;
                UpdateHotkeyStatus();
            };
            hotkeyBox.Captured += (mods, key) =>
            {
                string combo = Hotkey.Format(mods, key);
                string error;
                bool ok = app.TrySetHotkey(combo, out error);
                statusOverride = ok ? "Zapisano skrót " + combo + "." : error;
                statusIsError = !ok;
                bool altGr = (mods & (Hotkey.MOD_CONTROL | Hotkey.MOD_ALT)) == (Hotkey.MOD_CONTROL | Hotkey.MOD_ALT) &&
                             (mods & Hotkey.MOD_WIN) == 0 && key >= Keys.A && key <= Keys.Z;
                if (ok && altGr)
                {
                    statusOverride = "Zapisano " + combo + ", ale uwaga: Ctrl+Alt to AltGr na polskiej klawiaturze, " +
                                     "więc ten skrót może zablokować pisanie polskich liter (ą, ę, ś…).";
                    statusIsError = true;
                }
                ActiveControl = btnDone;   // wyjście z pola -> Leave odświeża stan
            };
            hotkeyBox.Hint += message =>
            {
                lblHotkeyStatus.Text = message;
                lblHotkeyStatus.ForeColor = Theme.Error;
            };
            hotkeyBox.Cancelled += () => ActiveControl = btnDone;

            btnDefaultHotkey.Click += (s, e) =>
            {
                string error;
                bool ok = app.TrySetHotkey(AppConfig.DefaultHotkey, out error);
                statusOverride = ok ? "Przywrócono skrót " + AppConfig.DefaultHotkey + "." : error;
                statusIsError = !ok;
                hotkeyBox.Text = app.Config.Hotkey;
                UpdateHotkeyStatus();
            };

            chkAutostart.CheckedChanged += (s, e) =>
            {
                if (loading) return;
                try { Autostart.Set(chkAutostart.Checked, AppPaths.CurrentExe); }
                catch (Exception ex)
                {
                    Log.Error("autostart", ex);
                    MessageBox.Show(this, "Nie udało się zmienić autostartu:\n" + ex.Message, "TrayFolderMenu",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshState();
            };

            btnInstall.Click += (s, e) =>
            {
                if (AppPaths.IsInstalledCopy)
                {
                    if (MessageBox.Show(this, "Odinstalować TrayFolderMenu?\n\nAplikacja zostanie zamknięta i usunięta z autostartu.",
                            "TrayFolderMenu", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Process.Start(AppPaths.CurrentExe, "/uninstall");
                }
                else
                {
                    Process.Start(AppPaths.CurrentExe, "/install");
                }
            };

            lnkLog.LinkClicked += (s, e) =>
            {
                if (File.Exists(AppPaths.LogFile)) app.Launch(AppPaths.LogFile);
                else MessageBox.Show(this, "Log jest jeszcze pusty.", "TrayFolderMenu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            lnkConfig.LinkClicked += (s, e) =>
            {
                Directory.CreateDirectory(AppPaths.ConfigDir);
                app.Launch(AppPaths.ConfigDir);
            };

            btnQuit.Click += (s, e) => app.ExitApp();
            btnDone.Click += (s, e) => Hide();

            // Gdy okno traci fokus w trakcie nagrywania, przywróć skrót
            Deactivate += (s, e) =>
            {
                if (ActiveControl != hotkeyBox) return;
                ActiveControl = btnDone;
                app.ApplyHotkey(false);
                hotkeyBox.Text = app.Config.Hotkey;
                UpdateHotkeyStatus();
            };
            VisibleChanged += (s, e) => { if (!Visible) app.ApplyHotkey(false); };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // zamknięcie okna = schowanie; aplikacja działa dalej w zasobniku
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
