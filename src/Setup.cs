// SwitchPeekSetup — per-user installer (no admin rights needed).
// Embeds SwitchPeek.exe, copies it to %LOCALAPPDATA%\Programs\SwitchPeek, adds a Start menu
// shortcut, an "Installed apps" entry with uninstall, optional autostart, then launches it.
// Ключи: /S или --silent — тихая установка с автозапуском.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("SwitchPeek Setup")]

static class SetupProgram
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch (Exception) { }

        if (Array.IndexOf(args, "/S") >= 0 || Array.IndexOf(args, "--silent") >= 0)
        {
            try
            {
                Installer.Install(true);
                Installer.Launch(false);
                return 0;
            }
            catch (Exception) { return 1; }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}

static class Installer
{
    public static bool IsInstalled { get { return File.Exists(AppInfo.InstalledExe); } }

    public static void Install(bool autostart)
    {
        // Обновление поверх запущенной версии.
        foreach (Process p in Process.GetProcessesByName(AppInfo.Name))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
        }

        Directory.CreateDirectory(AppInfo.InstallDir);
        using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("SwitchPeek.exe"))
        using (FileStream dst = File.Create(AppInfo.InstalledExe))
            src.CopyTo(dst);

        CreateShortcut(AppInfo.StartMenuShortcut, AppInfo.InstalledExe);
        Autostart.Set(autostart, AppInfo.InstalledExe);
        RegisterUninstall();
    }

    public static void Launch(bool welcome)
    {
        var psi = new ProcessStartInfo(AppInfo.InstalledExe, welcome ? "--welcome" : "");
        psi.UseShellExecute = false;
        psi.WorkingDirectory = AppInfo.InstallDir;
        Process.Start(psi);
    }

    static void RegisterUninstall()
    {
        string exe = AppInfo.InstalledExe;
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(AppInfo.UninstallKey))
        {
            k.SetValue("DisplayName", AppInfo.Name);
            k.SetValue("DisplayVersion", AppInfo.Version);
            k.SetValue("Publisher", AppInfo.Publisher);
            k.SetValue("DisplayIcon", exe + ",0");
            k.SetValue("InstallLocation", AppInfo.InstallDir);
            k.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
            k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --quiet");
            k.SetValue("URLInfoAbout", AppInfo.RepoUrl);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            k.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    // Ярлык через WScript.Shell (позднее связывание, без лишних ссылок на сборки).
    static void CreateShortcut(string lnkPath, string target)
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
                t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Keyboard layout indicator" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            finally { Marshal.FinalReleaseComObject(lnk); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
}

class SetupForm : Form
{
    readonly CheckBox autostart = new CheckBox();
    readonly Button install = new Button();
    readonly Button cancel = new Button();
    readonly PictureBox logo = new PictureBox();
    readonly Label title = new Label();
    readonly Label subtitle = new Label();
    readonly Label where = new Label();

    public SetupForm()
    {
        bool update = Installer.IsInstalled;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Text = Loc.T("SwitchPeek Setup", "Установка SwitchPeek", "Встановлення SwitchPeek");
        Icon = AppInfo.LoadIcon(new Size(32, 32));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ClientSize = new Size(480, 250);

        logo.Bounds = new Rectangle(24, 24, 64, 64);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.Image = AppInfo.LoadLargeImage();

        title.Text = AppInfo.Name + " " + AppInfo.Version;
        title.Font = new Font("Segoe UI Semibold", 15f);
        title.Bounds = new Rectangle(104, 24, 352, 34);

        subtitle.Text = Loc.T(
            "Shows the keyboard layout in the center of the screen whenever you switch it.",
            "Показывает раскладку клавиатуры по центру экрана при каждом её переключении.",
            "Показує розкладку клавіатури по центру екрана під час кожного її перемикання.");
        subtitle.ForeColor = Color.FromArgb(80, 80, 80);
        subtitle.Bounds = new Rectangle(104, 60, 352, 40);

        where.Text = Loc.T("Install to: ", "Папка установки: ", "Тека встановлення: ") + AppInfo.InstallDir;
        where.ForeColor = Color.FromArgb(120, 120, 120);
        where.AutoEllipsis = true;
        where.Bounds = new Rectangle(24, 112, 432, 20);

        autostart.Text = Loc.T("Run at Windows startup", "Запускать вместе с Windows", "Запускати разом з Windows");
        autostart.Checked = update ? Autostart.IsEnabled() : true;
        autostart.Bounds = new Rectangle(24, 140, 432, 24);

        var footer = new Panel();
        footer.BackColor = Color.FromArgb(243, 243, 243);
        footer.Bounds = new Rectangle(0, 186, 480, 64);

        install.Text = update ? Loc.T("Update", "Обновить", "Оновити") : Loc.T("Install", "Установить", "Встановити");
        install.Bounds = new Rectangle(264, 16, 100, 32);
        install.Click += OnInstall;

        cancel.Text = Loc.T("Cancel", "Отмена", "Скасувати");
        cancel.Bounds = new Rectangle(372, 16, 88, 32);
        cancel.DialogResult = DialogResult.Cancel;

        footer.Controls.Add(install);
        footer.Controls.Add(cancel);
        Controls.Add(logo);
        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(where);
        Controls.Add(autostart);
        Controls.Add(footer);
        AcceptButton = install;
        CancelButton = cancel;
        ResumeLayout(false);
    }

    // После DPI-масштабирования растягиваем тексты до правого поля окна.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        int margin = logo.Left;
        foreach (Control c in new Control[] { title, subtitle, where, autostart })
            c.Width = ClientSize.Width - c.Left - margin;
        subtitle.Height = where.Top - subtitle.Top - margin / 3;
    }

    void OnInstall(object sender, EventArgs e)
    {
        install.Enabled = cancel.Enabled = autostart.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            Installer.Install(autostart.Checked);
            Installer.Launch(true);
            Close();
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(this,
                Loc.T("Installation failed:\n", "Не удалось установить:\n", "Не вдалося встановити:\n") + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = cancel.Enabled = autostart.Enabled = true;
        }
    }
}
