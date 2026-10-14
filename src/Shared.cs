// Shared between SwitchPeek.exe and SwitchPeekSetup.exe.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

[assembly: AssemblyProduct("SwitchPeek")]
[assembly: AssemblyCompany("Yurii Patrin")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0")]

static class AppInfo
{
    public const string Name = "SwitchPeek";
    public const string Publisher = "Yurii Patrin";
    public const string RepoUrl = "https://github.com/ypatrin/SwitchPeek";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SwitchPeek";

    public static string Version
    {
        get
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.ToString(v.Build > 0 ? 3 : 2); // 1.0, 1.0.1, ...
        }
    }

    public static string InstallDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "Programs", Name);
        }
    }

    public static string InstalledExe
    {
        get { return Path.Combine(InstallDir, Name + ".exe"); }
    }

    public static string StartMenuShortcut
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); }
    }

    // Иконка из ресурсов сборки (SwitchPeek.ico) нужного размера.
    public static Icon LoadIcon(Size size)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SwitchPeek.ico"))
            return s != null ? new Icon(s, size) : (Icon)SystemIcons.Application.Clone();
    }

    // Самый крупный PNG-кадр из SwitchPeek.ico (System.Drawing.Icon их не декодирует).
    public static Image LoadLargeImage()
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SwitchPeek.ico"))
        {
            if (s == null) return SystemIcons.Application.ToBitmap();
            var data = new byte[s.Length];
            s.Read(data, 0, data.Length);
            int count = BitConverter.ToUInt16(data, 4), best = -1, bestSize = 0;
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int size = data[e] == 0 ? 256 : data[e];
                int offset = BitConverter.ToInt32(data, e + 12);
                bool png = data[offset] == 0x89 && data[offset + 1] == (byte)'P';
                if (png && size > bestSize) { best = e; bestSize = size; }
            }
            if (best < 0) return new Icon(new MemoryStream(data), 256, 256).ToBitmap();
            int len = BitConverter.ToInt32(data, best + 8), off = BitConverter.ToInt32(data, best + 12);
            return new Bitmap(new MemoryStream(data, off, len));
        }
    }
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppInfo.Name) != null;
    }

    public static void Set(bool on, string exePath)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppInfo.Name, "\"" + exePath + "\"");
            else k.DeleteValue(AppInfo.Name, false);
        }
    }
}

static class Loc
{
    // Строка на языке интерфейса Windows.
    public static string T(string en, string ru, string uk)
    {
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (lang == "uk") return uk;
        if (lang == "ru" || lang == "be") return ru;
        return en;
    }
}
