// SwitchPeek — показывает раскладку по центру экрана при её смене (как в macOS/GNOME).
// Сборка: build.cmd (результат — bin\SwitchPeek.exe и bin\SwitchPeekSetup.exe)
// Ключи: --preview <папка>  — отрисовать плашки в PNG и выйти
//        --welcome          — показать приветствие (запуск из установщика)
//        --uninstall [--quiet] — удалить установленную программу

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("SwitchPeek")]

static class Settings
{
    public const int PollMs = 60;               // как часто проверять раскладку
    public const int IgnoreAfterFocusMs = 400;  // не показывать сразу после переключения окна
    public const int FadeInMs = 70;
    public const int ShowMs = 700;
    public const int FadeOutMs = 300;
    public const int Width = 200;               // логические пиксели (умножаются на масштаб монитора)
    public const int Height = 150;
    public const int Radius = 26;
    public const float CodeFontPx = 60f;
    public const float NameFontPx = 17f;
    public const int ShadowSoftness = 6;        // радиус размытия тени
    public const int ShadowOffsetY = 4;         // смещение тени вниз
    public const int ShadowAlphaLight = 70;     // плотность тени в светлой теме (0..255)
    public const int ShadowAlphaDark = 120;     // и в тёмной
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Native.EnableDpiAwareness();

        if (args.Length >= 2 && args[0] == "--preview")
        {
            Preview(args[1]);
            return;
        }
        if (args.Length >= 1 && args[0] == "--uninstall")
        {
            Uninstaller.Run(Array.IndexOf(args, "--quiet") >= 0);
            return;
        }

        bool created;
        using (var mutex = new Mutex(true, "SwitchPeek_SingleInstance_7f3a91", out created))
        {
            if (!created) return;
            Application.EnableVisualStyles();
            Application.Run(new IndicatorContext(Array.IndexOf(args, "--welcome") >= 0));
            GC.KeepAlive(mutex);
        }
    }

    static void Preview(string dir)
    {
        Directory.CreateDirectory(dir);
        int[] langs = { 0x0409, 0x0419, 0x0422 };
        foreach (int lang in langs)
        {
            string code, name;
            Overlay.Describe(new IntPtr(lang | (lang << 16)), out code, out name);
            foreach (bool light in new[] { true, false })
            {
                using (var bmp = Overlay.Render(code, name, 1.5f, light))
                    bmp.Save(Path.Combine(dir, code + (light ? "_light" : "_dark") + ".png"), ImageFormat.Png);
            }
        }
    }
}

class IndicatorContext : ApplicationContext
{
    readonly Overlay overlay = new Overlay();
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    readonly NotifyIcon tray = new NotifyIcon();
    IntPtr lastFg = IntPtr.Zero;
    IntPtr lastHkl = IntPtr.Zero;
    int fgChangedAt;

    public IndicatorContext(bool welcome)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Loc.T("Show now", "Показать сейчас", "Показати зараз")).Click += delegate { ShowNow(); };
        var autoItem = new ToolStripMenuItem(Loc.T("Run at Windows startup", "Запускать вместе с Windows", "Запускати разом з Windows"));
        autoItem.Checked = Autostart.IsEnabled();
        autoItem.Click += delegate
        {
            Autostart.Set(!autoItem.Checked, Application.ExecutablePath);
            autoItem.Checked = Autostart.IsEnabled();
        };
        menu.Items.Add(autoItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("Exit", "Выход", "Вихід")).Click += delegate { ExitThread(); };

        tray.Icon = AppInfo.LoadIcon(SystemInformation.SmallIconSize);
        tray.Text = Loc.T("SwitchPeek — keyboard layout indicator", "SwitchPeek — индикатор раскладки", "SwitchPeek — індикатор розкладки");
        tray.ContextMenuStrip = menu;
        tray.Visible = true;

        poll.Interval = Settings.PollMs;
        poll.Tick += OnPoll;
        poll.Start();

        if (welcome) Welcome();
    }

    // После установки: уведомление и демонстрация плашки.
    void Welcome()
    {
        tray.ShowBalloonTip(6000, "SwitchPeek",
            Loc.T("SwitchPeek is running. Switch the keyboard layout to see it.",
                  "SwitchPeek запущен. Переключите раскладку, чтобы увидеть индикатор.",
                  "SwitchPeek запущено. Перемкніть розкладку, щоб побачити індикатор."),
            ToolTipIcon.None);
        var once = new System.Windows.Forms.Timer { Interval = 700 };
        once.Tick += delegate { once.Stop(); once.Dispose(); ShowNow(); };
        once.Start();
    }

    void ShowNow()
    {
        IntPtr fg = Native.GetForegroundWindow();
        IntPtr hkl = Native.GetLayoutFor(fg);
        if (hkl != IntPtr.Zero) overlay.Popup(hkl, fg);
    }

    void OnPoll(object sender, EventArgs e)
    {
        IntPtr fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        IntPtr hkl = Native.GetLayoutFor(fg);
        if (hkl == IntPtr.Zero) return;

        int now = Environment.TickCount;
        if (fg != lastFg)
        {
            // Смена окна — не смена раскладки, просто запоминаем.
            lastFg = fg;
            lastHkl = hkl;
            fgChangedAt = now;
            return;
        }
        if (hkl == lastHkl) return;
        lastHkl = hkl;

        if (unchecked(now - fgChangedAt) < Settings.IgnoreAfterFocusMs) return;
        if (Native.IsFullscreenGame()) return;
        overlay.Popup(hkl, fg);
    }

    protected override void ExitThreadCore()
    {
        poll.Stop();
        tray.Visible = false;
        tray.Dispose();
        overlay.Dispose();
        base.ExitThreadCore();
    }
}

class Overlay : Form
{
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
    IntPtr hbmp = IntPtr.Zero;
    Point pos;
    Size size;
    int startedAt;
    bool shown;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        anim.Interval = 15;
        anim.Tick += OnAnim;
        IntPtr h = Handle; // создать окно заранее
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
                        | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public void Popup(IntPtr hkl, IntPtr fg)
    {
        string code, name;
        Describe(hkl, out code, out name);

        IntPtr mon = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
        float scale = Native.GetScale(mon);
        Rectangle b = Screen.FromHandle(fg).Bounds;

        FreeBitmap();
        using (var bmp = Render(code, name, scale, Native.IsLightTheme()))
        {
            hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            size = bmp.Size;
        }
        pos = new Point(b.Left + (b.Width - size.Width) / 2, b.Top + (b.Height - size.Height) / 2);

        // Если уже на экране — без повторного появления, просто обновляем и продлеваем.
        startedAt = Environment.TickCount - (shown ? Settings.FadeInMs : 0);
        Apply(shown ? (byte)255 : (byte)0);
        if (!shown)
        {
            Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
            shown = true;
        }
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        anim.Start();
    }

    void OnAnim(object sender, EventArgs e)
    {
        int t = unchecked(Environment.TickCount - startedAt);
        int fadeOutStart = Settings.FadeInMs + Settings.ShowMs;
        if (t >= fadeOutStart + Settings.FadeOutMs)
        {
            anim.Stop();
            Native.ShowWindow(Handle, Native.SW_HIDE);
            shown = false;
            FreeBitmap();
            return;
        }
        double a;
        if (t < Settings.FadeInMs) a = (double)t / Settings.FadeInMs;
        else if (t < fadeOutStart) a = 1;
        else
        {
            double k = (double)(t - fadeOutStart) / Settings.FadeOutMs;
            a = 1 - k * k; // плавное ускоряющееся исчезновение
        }
        Apply((byte)Math.Round(255 * Math.Max(0, Math.Min(1, a))));
    }

    void Apply(byte alpha)
    {
        if (hbmp == IntPtr.Zero) return;
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr old = Native.SelectObject(memDc, hbmp);
        try
        {
            var dst = new Native.POINT { X = pos.X, Y = pos.Y };
            var sz = new Native.SIZE { CX = size.Width, CY = size.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref sz, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    void FreeBitmap()
    {
        if (hbmp != IntPtr.Zero) { Native.DeleteObject(hbmp); hbmp = IntPtr.Zero; }
    }

    protected override void Dispose(bool disposing)
    {
        anim.Dispose();
        FreeBitmap();
        base.Dispose(disposing);
    }

    public static void Describe(IntPtr hkl, out string code, out string name)
    {
        int langId = (int)(hkl.ToInt64() & 0xFFFF);
        code = langId.ToString("X4");
        name = "";
        try
        {
            CultureInfo ci = CultureInfo.GetCultureInfo(langId);
            CultureInfo neutral = ci.IsNeutralCulture ? ci : ci.Parent;
            code = ci.TwoLetterISOLanguageName.ToUpperInvariant();
            name = neutral.NativeName;
            if (name.Length > 0) name = char.ToUpper(name[0], ci) + name.Substring(1);
        }
        catch (Exception) { }
        if (code == "UK") code = "UA";
    }

    public static Bitmap Render(string code, string name, float s, bool light)
    {
        int w = (int)Math.Round(Settings.Width * s);
        int h = (int)Math.Round(Settings.Height * s);
        int soft = Math.Max(1, (int)Math.Round(Settings.ShadowSoftness * s));
        int offY = (int)Math.Round(Settings.ShadowOffsetY * s);
        int m = soft * 3 + offY; // поля под тень
        var bmp = new Bitmap(w + 2 * m, h + 2 * m, PixelFormat.Format32bppArgb);
        DrawShadow(bmp, new RectangleF(m, m + offY, w, h), Settings.Radius * s, soft,
                   light ? Settings.ShadowAlphaLight : Settings.ShadowAlphaDark);

        Color bg = light ? Color.FromArgb(232, 246, 246, 248) : Color.FromArgb(220, 32, 32, 34);
        Color border = light ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255);
        Color fg = light ? Color.FromArgb(255, 22, 22, 24) : Color.FromArgb(255, 255, 255, 255);
        Color sub = light ? Color.FromArgb(160, 22, 22, 24) : Color.FromArgb(170, 255, 255, 255);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.TranslateTransform(m, m);

            using (var path = RoundRect(new RectangleF(0.5f, 0.5f, w - 1f, h - 1f), Settings.Radius * s))
            using (var bgBrush = new SolidBrush(bg))
            using (var pen = new Pen(border, Math.Max(1f, s)))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(pen, path);
            }

            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var codeFont = new Font("Segoe UI Semibold", Settings.CodeFontPx * s, GraphicsUnit.Pixel))
            using (var nameFont = new Font("Segoe UI", Settings.NameFontPx * s, GraphicsUnit.Pixel))
            using (var fgBrush = new SolidBrush(fg))
            using (var subBrush = new SolidBrush(sub))
            {
                g.DrawString(code, codeFont, fgBrush, new RectangleF(0, h * 0.04f, w, h * 0.64f), sf);
                g.DrawString(name, nameFont, subBrush, new RectangleF(0, h * 0.64f, w, h * 0.24f), sf);
            }
        }
        return bmp;
    }

    // Мягкая тень: силуэт плашки, размытый тремя проходами box blur (≈ гаусс).
    static void DrawShadow(Bitmap bmp, RectangleF rect, float radius, int soft, int opacity)
    {
        int w = bmp.Width, h = bmp.Height;
        var full = new Rectangle(0, 0, w, h);
        byte[] a = new byte[w * h];
        int[] px = new int[w * h];

        using (var mask = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(mask))
            using (var path = RoundRect(rect, radius))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillPath(Brushes.Black, path);
            }
            BitmapData md = mask.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(md.Scan0, px, 0, px.Length);
            mask.UnlockBits(md);
        }
        for (int i = 0; i < a.Length; i++) a[i] = (byte)((uint)px[i] >> 24);

        byte[] tmp = new byte[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlur(a, tmp, w, h, soft, 1, w);   // по горизонтали
            BoxBlur(tmp, a, h, w, soft, w, 1);   // по вертикали
        }

        for (int i = 0; i < a.Length; i++) px[i] = (a[i] * opacity / 255) << 24; // чёрный с альфой
        BitmapData bd = bmp.LockBits(full, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, bd.Scan0, px.Length);
        bmp.UnlockBits(bd);
    }

    // Скользящее среднее вдоль линий длиной len; step — шаг внутри линии, lineStep — между линиями.
    static void BoxBlur(byte[] src, byte[] dst, int len, int lines, int r, int step, int lineStep)
    {
        int div = 2 * r + 1;
        for (int l = 0; l < lines; l++)
        {
            int b = l * lineStep;
            int sum = 0;
            for (int i = -r; i <= r; i++) sum += src[b + Math.Max(0, Math.Min(len - 1, i)) * step];
            for (int i = 0; i < len; i++)
            {
                dst[b + i * step] = (byte)(sum / div);
                int iOut = Math.Max(0, i - r);
                int iIn = Math.Min(len - 1, i + r + 1);
                sum += src[b + iIn * step] - src[b + iOut * step];
            }
        }
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// Удаление: вызывается из «Установленные приложения» как SwitchPeek.exe --uninstall.
static class Uninstaller
{
    public static void Run(bool quiet)
    {
        if (!quiet && MessageBox.Show(
                Loc.T("Remove SwitchPeek from this computer?",
                      "Удалить SwitchPeek с этого компьютера?",
                      "Видалити SwitchPeek з цього комп'ютера?"),
                "SwitchPeek", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName(AppInfo.Name))
        {
            if (p.Id == self) continue;
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception) { }
        }

        Autostart.Set(false, null);
        try { Registry.CurrentUser.DeleteSubKeyTree(AppInfo.UninstallKey, false); } catch (Exception) { }
        try { File.Delete(AppInfo.StartMenuShortcut); } catch (Exception) { }

        if (!quiet)
            MessageBox.Show(Loc.T("SwitchPeek has been removed.", "SwitchPeek удалён.", "SwitchPeek видалено."),
                            "SwitchPeek", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Папку удаляем только если запущены из неё — и уже после выхода этого процесса.
        string dir = Path.GetDirectoryName(Application.ExecutablePath);
        if (string.Equals(dir.TrimEnd('\\'), AppInfo.InstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + dir + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            Process.Start(psi);
        }
    }
}

static class Native
{
    public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8,
                     WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    public const int ULW_ALPHA = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO info);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dpiX, out uint dpiY);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public static IntPtr GetLayoutFor(IntPtr fg)
    {
        uint tid = GetWindowThreadProcessId(fg, IntPtr.Zero);
        var gti = new GUITHREADINFO();
        gti.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
        if (GetGUIThreadInfo(tid, ref gti) && gti.hwndFocus != IntPtr.Zero)
            tid = GetWindowThreadProcessId(gti.hwndFocus, IntPtr.Zero);
        return GetKeyboardLayout(tid);
    }

    public static float GetScale(IntPtr mon)
    {
        try
        {
            uint dx, dy;
            if (GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96f;
        }
        catch (Exception) { }
        return 1f;
    }

    public static bool IsLightTheme()
    {
        try
        {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                                         "SystemUsesLightTheme", 0);
            return v is int && (int)v == 1;
        }
        catch (Exception) { return false; }
    }

    // Полноэкранная игра (Direct3D exclusive) — индикатор поверх не рисуем.
    public static bool IsFullscreenGame()
    {
        try
        {
            int state;
            return SHQueryUserNotificationState(out state) == 0 && state == 3;
        }
        catch (Exception) { return false; }
    }

    public static void EnableDpiAwareness()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (Exception) { }
        try { SetProcessDPIAware(); } catch (Exception) { }
    }
}
