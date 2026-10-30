using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace ProMaxx2.ServiceManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // --tray = เปิดพร้อม Windows: เริ่มแบบซ่อนไว้ใน tray
        Application.Run(new MainForm(args.Contains("--tray", StringComparer.OrdinalIgnoreCase)));
    }
}

internal enum Tone { Neutral, Success, Warning, Danger, Info }
internal enum Health { Unknown, Running, Unresponsive, Stopped }
internal enum LogLevel { System, Success, Info, Warning, Error }
internal readonly record struct LogEntry(DateTime Time, string? Source, string Text, LogLevel Level);

/// <summary>ข้อผิดพลาดที่เขียนลง Activity Log แล้ว — ตัวจัดการกลางแสดงแค่แถบแจ้งเตือน ไม่ log ซ้ำ</summary>
internal sealed class ServiceException(string message) : Exception(message);

/// <summary>สีและตัวอักษรตาม Design Tokens ของเว็บ (UI_DESIGN_SYSTEM.md §3) ให้หน้าตาไปทางเดียวกับ QA Hub</summary>
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0xf5, 0xf7, 0xfb);
    public static readonly Color Card = Color.White;
    public static readonly Color Text = Color.FromArgb(0x1f, 0x29, 0x37);
    public static readonly Color Muted = Color.FromArgb(0x66, 0x70, 0x85);
    public static readonly Color Line = Color.FromArgb(0xe5, 0xe7, 0xeb);
    public static readonly Color Primary = Color.FromArgb(0x24, 0x57, 0xd6);
    public static readonly Color Green = Color.FromArgb(0x16, 0x9c, 0x63);
    public static readonly Color Yellow = Color.FromArgb(0xd7, 0x9a, 0x00);
    public static readonly Color Red = Color.FromArgb(0xd6, 0x45, 0x45);
    public static readonly Color WarningText = Color.FromArgb(0x9a, 0x6d, 0x00);
    public static readonly Color LogBg = Color.FromArgb(17, 24, 39);

    // Leelawadee UI มีทั้งไทยและอังกฤษในชุดเดียว (มากับ Windows 10/11) — ถ้าไม่มีใช้ Tahoma แบบเดิม
    private static readonly string FontName = PickFont("Leelawadee UI", "Tahoma");
    public static readonly Font Body = new(FontName, 9.5f);
    public static readonly Font Small = new(FontName, 8.5f);
    public static readonly Font SmallBold = new(FontName, 8.5f, FontStyle.Bold);
    public static readonly Font CardTitle = new(FontName, 11.5f, FontStyle.Bold);
    public static readonly Font PageTitle = new(FontName, 15f, FontStyle.Bold);
    // Consolas ไม่มีอักษรไทย — RichTextBox จะยืมฟอนต์อื่นมาแสดงไทยจนขนาดไม่เท่ากัน จึงใช้ฟอนต์ที่มีทั้งไทยและอังกฤษชุดเดียว
    public static readonly Font Log = new(FontName, 9.5f);

    /// <summary>ผสมสีกับขาว — amount 1 = สีเดิม, 0.1 = พื้นอ่อน 10%</summary>
    public static Color Tint(Color c, float amount)
    {
        int Mix(int v) => (int)Math.Round(255 + (v - 255) * amount);
        return Color.FromArgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    public static Color ToneColor(Tone tone) => tone switch { Tone.Success => Green, Tone.Warning => Yellow, Tone.Danger => Red, Tone.Info => Primary, _ => Muted };
    public static Color ToneText(Tone tone) => tone == Tone.Warning ? WarningText : ToneColor(tone);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string PickFont(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        return names.FirstOrDefault(n => installed.Families.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) ?? "Tahoma";
    }
}

/// <summary>ตั้งค่าของผู้ใช้ — เก็บที่ %LOCALAPPDATA%\ProMaxx2.ServiceManager\settings.json (การเปิดพร้อม Windows อ่านจาก registry โดยตรง)</summary>
internal sealed class ManagerSettings
{
    public bool AutoStartServices { get; set; }
    public bool AutoRestartOnCrash { get; set; }

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProMaxx2.ServiceManager", "settings.json");

    public static ManagerSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>เปิดพร้อม Windows ผ่าน HKCU\...\Run (เฉพาะผู้ใช้นี้ ไม่ต้องสิทธิ์ admin) พร้อม --tray</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProMaxx2ServiceManager";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --tray");
        else key.DeleteValue(ValueName, false);
    }
}

internal readonly record struct ProbeResult(bool Ok, string Detail);

/// <summary>ผลตรวจสภาพแวดล้อม (ทุก 30 วินาที บน thread pool): โดเมนสาธารณะ, Windows service, ดิสก์, โค้ดล่าสุด และ git</summary>
internal sealed record EnvSnapshot(ProbeResult PublicWeb, ProbeResult PublicApi, string? Tunnel, string? Sql,
    IReadOnlyList<(string Drive, long FreeBytes)> Disks, DateTime? ApiCodeAt, DateTime? WebConfigAt, string? Branch, string? Commit, int DirtyFiles);

/// <summary>สถานะของ API / Web ที่หน้าจอใช้ — Busy = กำลังเริ่ม/หยุด (ปุ่มของบริการนั้นถูกปิดจนเสร็จ)</summary>
internal sealed class ManagedService(string name, int port, string tech, ServiceCard card)
{
    private readonly Stopwatch _busyWatch = new();
    public string Name { get; } = name;
    public int Port { get; } = port;
    public string Tech { get; } = tech;
    public ServiceCard Card { get; } = card;
    public UiButton Start { get; } = new("Start", ButtonKind.Primary);
    public UiButton Restart { get; } = new("Restart");
    public UiButton Stop { get; } = new("Stop", ButtonKind.Danger);
    public Health Health { get; set; } = Health.Unknown;
    public bool IsUp => Health is Health.Running or Health.Unresponsive;
    public int? Pid { get; set; }
    public DateTime? StartedAt { get; set; }
    public float Cpu { get; set; }
    public float RamMb { get; set; }
    public string? Busy { get; private set; }
    public TimeSpan BusyElapsed => _busyWatch.Elapsed;
    /// <summary>เวลาแก้ไฟล์โค้ด/ตั้งค่าล่าสุดที่ตรวจพบ (ทุก 30 วินาที)</summary>
    public DateTime? LatestCodeAt { get; set; }
    /// <summary>มีค่า = แก้โค้ดหลังจาก process เริ่ม ตัวที่รันอยู่ยังใช้โค้ดเก่า</summary>
    public DateTime? CodeChangedAt => StartedAt is { } started && LatestCodeAt > started.AddSeconds(2) ? LatestCodeAt : null;
    /// <summary>เวลาที่ Start ใหม่อัตโนมัติ (watchdog) — จำกัด 3 ครั้งใน 10 นาที กันวนไม่รู้จบ</summary>
    public Queue<DateTime> AutoRestarts { get; } = new();
    public void SetBusy(string? label) { Busy = label; if (label is null) { _busyWatch.Reset(); LastActionAt = DateTime.UtcNow; } else _busyWatch.Restart(); }
    /// <summary>เวลาที่งานจากปุ่มล่าสุดจบ — การเปลี่ยนสถานะช่วงสั้น ๆ หลังจากนี้มาจากผู้ใช้เอง ไม่ต้องแจ้งเตือน</summary>
    public DateTime LastActionAt { get; private set; } = DateTime.MinValue;
}

internal sealed class MainForm : Form
{
    private const string ApiHealthUrl = "http://127.0.0.1:5038/health";
    private const string WebUrl = "http://127.0.0.1:5173";
    private const string WorkerUrl = "http://127.0.0.1:5038/api/v1/automation/schedules/worker-status";
    private const int MaxQueuedLogLines = 5000;
    private const int MaxHistory = 5000;
    private const int MaxLogCharacters = 200_000;

    private readonly string _root;
    private readonly ManagedService _api;
    private readonly ManagedService _web;
    private readonly ServiceCard _workerCard = new("Schedule Worker");
    private readonly UiButton _workerOn = new("เปิด Worker", ButtonKind.Primary);
    private readonly UiButton _workerOff = new("ปิด Worker");
    private readonly StatusPill _overall = new();
    private readonly UiButton _startAll = new("Start ทั้งหมด", ButtonKind.Primary);
    private readonly UiButton _restartAll = new("Restart ทั้งหมด");
    private readonly UiButton _openWeb = new("เปิดหน้าเว็บ");
    private readonly NoticeBar _notice = new();
    private readonly ResourceChart _chart = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.LogBg, ForeColor = Color.Gainsboro, Font = Theme.Log, BorderStyle = BorderStyle.None, DetectUrls = false };
    private readonly CheckBox _showAll = new() { Text = "แสดง log ทั้งหมดของ API/Web", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Text, BackColor = Theme.Card };
    private readonly Label _hiddenInfo = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 1000 };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly ConcurrentDictionary<string, LogLevel> _lastLevel = new();
    private readonly List<LogEntry> _history = [];
    private readonly Dictionary<int, (TimeSpan cpu, DateTime sampledAt)> _processSamples = new();
    private int _queuedLogLines;
    private int _hiddenCount;
    private bool _refreshing;
    private bool _refreshQueued;
    private bool? _workerEnabled;
    private bool _workerBusy;
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _trayStartAll = new("Start ทั้งหมด");
    private readonly ToolStripMenuItem _trayRestartAll = new("Restart ทั้งหมด");
    private readonly ToolStripMenuItem _trayOpenWeb = new("เปิดหน้าเว็บ");
    private Icon? _appIcon;
    private Tone? _iconTone;
    private bool _trayHintShown;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private const string PublicWebUrl = "https://promaxx2.qahub.store";
    private const string PublicApiUrl = "https://api-promaxx2.qahub.store/health";
    private const string TunnelService = "cloudflared";
    private const string SqlService = "MSSQL$MSSQLSERVER2022";
    private readonly ManagerSettings _settings = ManagerSettings.Load();
    private readonly HttpClient _publicHttp = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly System.Windows.Forms.Timer _envTimer = new() { Interval = 30_000 };
    private readonly Label _subtitle = new() { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true };
    private readonly UiButton _settingsButton = new("ตั้งค่า");
    private readonly ToolStripMenuItem _optStartup = new("เปิดพร้อม Windows (ซ่อนไว้ใน tray)");
    private readonly ToolStripMenuItem _optAutoStart = new("Start API/Web อัตโนมัติเมื่อเปิดโปรแกรม");
    private readonly ToolStripMenuItem _optAutoRestart = new("Start ใหม่อัตโนมัติเมื่อ API/Web หยุดเอง (สูงสุด 3 ครั้ง / 10 นาที)");
    private readonly EnvTile _tilePublicWeb = new("เว็บสาธารณะ");
    private readonly EnvTile _tilePublicApi = new("API สาธารณะ");
    private readonly EnvTile _tileTunnel = new("Cloudflare Tunnel");
    private readonly EnvTile _tileSql = new("SQL Server 2022");
    private readonly EnvTile _tileDisk = new("พื้นที่ดิสก์");
    private bool _startHidden;
    private bool _runtimeStarted;
    private bool _envRefreshing;
    private int _publicFailures;
    private string? _lastTunnel;
    private string? _lastSql;

    public MainForm(bool startHidden = false)
    {
        _startHidden = startHidden;
        _root = FindRepositoryRoot();
        _api = new ManagedService("API", 5038, "ASP.NET Core", new ServiceCard("API Service"));
        _web = new ManagedService("Web", 5173, "Vite", new ServiceCard("Web App"));
        Text = "ProMaxx2 QA - System Manager";
        StartPosition = FormStartPosition.CenterScreen;
        Font = Theme.Body;
        BackColor = Theme.Bg;
        MinimumSize = new Size(S(900), S(720));
        var area = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(S(1080), S(880));
        Size = new Size(Math.Min(S(1080), area.Width), Math.Min(S(880), area.Height));

        // ---------- ส่วนหัว: ชื่อ + สถานะรวม + ปุ่มที่ใช้บ่อย ----------
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = S(76), ColumnCount = 2, RowCount = 1, BackColor = Theme.Card, Padding = new Padding(S(24), S(12), S(20), S(12)) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
        titles.Controls.Add(new Label { Text = "ProMaxx2 QA System Manager", Font = Theme.PageTitle, ForeColor = Theme.Text, AutoSize = true, Margin = Padding.Empty });
        _subtitle.Text = _root;
        _subtitle.Margin = new Padding(1, S(2), 0, 0);
        titles.Controls.Add(_subtitle);
        var headerActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        _overall.Margin = new Padding(0, S(4), S(14), 0);
        _startAll.Margin = Padding.Empty;
        headerActions.Controls.AddRange([_overall, _settingsButton, _openWeb, _restartAll, _startAll]);
        header.Controls.Add(titles, 0, 0);
        header.Controls.Add(headerActions, 1, 0);
        var headerLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Line };

        // ---------- เนื้อหา: แถบแจ้งเตือน → การ์ดบริการ 3 ใบ → สภาพแวดล้อม → ทรัพยากร → log ----------
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(S(16), S(12), S(16), S(16)), BackColor = Theme.Bg };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, S(184)));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, S(120)));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, S(140)));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _notice.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _notice.Margin = new Padding(S(8), 0, S(8), S(8));
        body.Controls.Add(_notice, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Bg };
        for (var i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        foreach (var svc in new[] { _api, _web }) svc.Card.AddActions(svc.Start, svc.Restart, svc.Stop);
        _workerCard.AddActions(_workerOn, _workerOff);
        _workerCard.SetDetail("ตรวจ Schedule ทุก 30 วินาที · ทำงานอยู่ใน API");
        cards.Controls.Add(_api.Card, 0, 0);
        cards.Controls.Add(_web.Card, 1, 0);
        cards.Controls.Add(_workerCard, 2, 0);
        body.Controls.Add(cards, 0, 1);

        var envCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(S(16), S(12), S(4), S(10)) };
        var envGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Theme.Card, Margin = Padding.Empty };
        var tiles = new[] { _tilePublicWeb, _tilePublicApi, _tileTunnel, _tileSql, _tileDisk };
        for (var i = 0; i < tiles.Length; i++) { envGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20)); envGrid.Controls.Add(tiles[i], i, 0); }
        envCard.Controls.Add(envGrid);
        body.Controls.Add(envCard, 0, 2);

        var resourceCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(S(16), S(12), S(16), S(12)) };
        resourceCard.Controls.Add(_chart);
        body.Controls.Add(resourceCard, 0, 3);

        var logCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(S(16), S(10), S(16), S(16)) };
        var logHeader = new TableLayoutPanel { Dock = DockStyle.Top, Height = S(40), ColumnCount = 5, RowCount = 1, BackColor = Theme.Card, Margin = Padding.Empty };
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var copyLog = new UiButton("คัดลอก");
        var clearLog = new UiButton("ล้าง Log");
        clearLog.Margin = Padding.Empty;
        _showAll.Anchor = AnchorStyles.Left;
        _showAll.Margin = new Padding(S(8), 0, S(12), 0);
        copyLog.Anchor = clearLog.Anchor = AnchorStyles.Right;
        _hiddenInfo.Margin = new Padding(S(12), 0, 0, 0);
        logHeader.Controls.Add(new Label { Text = "Activity Log", Font = Theme.CardTitle, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 0, 0);
        logHeader.Controls.Add(_hiddenInfo, 1, 0);
        logHeader.Controls.Add(_showAll, 2, 0);
        logHeader.Controls.Add(copyLog, 3, 0);
        logHeader.Controls.Add(clearLog, 4, 0);
        var logFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.LogBg, Padding = new Padding(S(10), S(8), S(4), S(8)) };
        logFrame.Controls.Add(_log);
        // ปิด auto font / dual font ของ RichEdit — ไม่งั้นมันสลับฟอนต์เองตามภาษา ทำให้ไทยกับอังกฤษขนาดไม่เท่ากัน
        _log.HandleCreated += (_, _) =>
        {
            var options = (int)Native.SendMessage(_log.Handle, Native.EmGetLangOptions, IntPtr.Zero, IntPtr.Zero);
            Native.SendMessage(_log.Handle, Native.EmSetLangOptions, IntPtr.Zero, new IntPtr(options & ~(Native.ImfAutoFont | Native.ImfDualFont)));
        };
        var logGap = new Panel { Dock = DockStyle.Top, Height = S(8), BackColor = Theme.Card };
        logCard.Controls.Add(logFrame);
        logCard.Controls.Add(logGap);
        logCard.Controls.Add(logHeader);
        body.Controls.Add(logCard, 0, 4);

        Controls.Add(body);
        Controls.Add(headerLine);
        Controls.Add(header);

        // ---------- การทำงานของปุ่ม ----------
        _api.Start.Click += async (_, _) => await Run([_api], StartApi, "API พร้อมใช้งานแล้ว");
        _api.Restart.Click += async (_, _) => await Run([_api], async () => { await VerifyApiBuild(); await StopService(_api); await StartApi(); }, "เริ่ม API ใหม่แล้ว — ผู้ใช้อาจต้อง login ใหม่");
        _api.Stop.Click += async (_, _) => { if (Confirm("หยุด API?\n\nผู้ใช้ทุกคน (รวมผ่านโดเมนสาธารณะ) จะใช้งานระบบไม่ได้จนกว่าจะ Start ใหม่")) await Run([_api], () => StopService(_api), "หยุด API แล้ว"); };
        _web.Start.Click += async (_, _) => await Run([_web], StartWeb, "Web พร้อมใช้งานแล้ว");
        _web.Restart.Click += async (_, _) => await Run([_web], async () => { await StopService(_web); await StartWeb(); }, "เริ่ม Web ใหม่แล้ว");
        _web.Stop.Click += async (_, _) => { if (Confirm("หยุด Web?\n\nผู้ใช้จะเปิดหน้าเว็บไม่ได้จนกว่าจะ Start ใหม่")) await Run([_web], () => StopService(_web), "หยุด Web แล้ว"); };
        // API กับ Web ไม่ขึ้นต่อกัน — เริ่ม/หยุดพร้อมกันเพื่อลดเวลารอ
        _startAll.Click += async (_, _) => await StartAll();
        _restartAll.Click += async (_, _) => await RestartAll();
        _openWeb.Click += (_, _) => OpenWeb();
        _workerOn.Click += async (_, _) => await SetScheduleWorker(true);
        _workerOff.Click += async (_, _) => await SetScheduleWorker(false);
        _showAll.CheckedChanged += (_, _) => RenderAll();
        copyLog.Click += (_, _) => CopyLog();
        clearLog.Click += (_, _) => ClearLog();

        SetupTray();
        _statusTimer.Tick += async (_, _) => await RefreshStatus();
        _logTimer.Tick += (_, _) => DrainLogs();
        _tickTimer.Tick += (_, _) => Tick();
        SetupSettingsMenu();
        _envTimer.Tick += async (_, _) => await RefreshEnvironment();
        Shown += async (_, _) => await InitializeRuntime();
        FormClosed += (_, _) =>
        {
            _statusTimer.Stop(); _logTimer.Stop(); _tickTimer.Stop();
            _envTimer.Stop();
            _statusTimer.Dispose(); _logTimer.Dispose(); _tickTimer.Dispose(); _envTimer.Dispose(); _http.Dispose(); _publicHttp.Dispose();
            _settingsButton.ContextMenuStrip?.Dispose();
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
            ReleaseIcon(_appIcon);
        };
    }

    /// <summary>เริ่มตัวจับเวลาและตรวจครั้งแรก — เรียกจาก Shown หรือทันทีเมื่อเปิดแบบซ่อนใน tray (ซึ่งไม่มี Shown จนกว่าจะเปิดหน้าต่าง)</summary>
    private async Task InitializeRuntime()
    {
        if (_runtimeStarted) return;
        _runtimeStarted = true;
        Log($"เปิด System Manager · Workspace: {_root}");
        UpdateUi();
        _logTimer.Start(); _tickTimer.Start(); _statusTimer.Start(); _envTimer.Start();
        await RefreshStatus();
        _ = RefreshEnvironment();
        if (_settings.AutoStartServices && (_api.Health == Health.Stopped || _web.Health == Health.Stopped))
        {
            Log("Start API/Web อัตโนมัติตามที่ตั้งค่าไว้");
            await StartAll();
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _startHidden)
        {
            // เปิดพร้อม Windows: สร้างหน้าต่างไว้แต่ไม่แสดง — คลิกไอคอนใน tray เพื่อเปิด
            _startHidden = false;
            _trayHintShown = true;
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
            BeginInvoke(new Action(() => _ = InitializeRuntime()));
            return;
        }
        base.SetVisibleCore(value);
    }

    // ---------- ตั้งค่า ----------

    private void SetupSettingsMenu()
    {
        var menu = new ContextMenuStrip { Font = Theme.Body };
        _optStartup.CheckOnClick = _optAutoStart.CheckOnClick = _optAutoRestart.CheckOnClick = true;
        menu.Items.AddRange([_optStartup, _optAutoStart, _optAutoRestart]);
        menu.Opening += (_, _) =>
        {
            _optStartup.Checked = StartupRegistration.IsEnabled();
            _optAutoStart.Checked = _settings.AutoStartServices;
            _optAutoRestart.Checked = _settings.AutoRestartOnCrash;
        };
        _optStartup.Click += (_, _) =>
        {
            try
            {
                StartupRegistration.Set(_optStartup.Checked);
                Notify(_optStartup.Checked ? $"จะเปิดพร้อม Windows (ซ่อนใน tray) จาก {Application.ExecutablePath}" : "ยกเลิกการเปิดพร้อม Windows แล้ว", Tone.Info);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Notify($"ตั้งค่าเปิดพร้อม Windows ไม่สำเร็จ: {ex.Message}", Tone.Danger); }
        };
        _optAutoStart.Click += (_, _) => SaveSetting(() => _settings.AutoStartServices = _optAutoStart.Checked,
            _optAutoStart.Checked ? "เปิดโปรแกรมครั้งหน้าจะ Start API/Web ที่หยุดอยู่ให้อัตโนมัติ" : "ปิด Start อัตโนมัติแล้ว");
        _optAutoRestart.Click += (_, _) => SaveSetting(() => _settings.AutoRestartOnCrash = _optAutoRestart.Checked,
            _optAutoRestart.Checked ? "API/Web ที่หยุดเองจะถูก Start ใหม่อัตโนมัติ (สูงสุด 3 ครั้งใน 10 นาที)" : "ปิด Start ใหม่อัตโนมัติแล้ว — จะแจ้งเตือนอย่างเดียว");
        _settingsButton.ContextMenuStrip = menu;
        _settingsButton.Click += (_, _) => menu.Show(_settingsButton, new Point(0, _settingsButton.Height + S(4)));
    }

    private void SaveSetting(Action apply, string message)
    {
        apply();
        try { _settings.Save(); Notify(message, Tone.Info); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify($"บันทึกการตั้งค่าไม่สำเร็จ: {ex.Message}", Tone.Danger); }
    }

    private Task StartAll() => Run([_api, _web], () => Task.WhenAll(StartApi(), StartWeb()), "API และ Web พร้อมใช้งานแล้ว");

    private Task RestartAll() => Run([_api, _web], async () =>
    {
        if (_api.IsUp) await VerifyApiBuild();
        await Task.WhenAll(StopService(_api), StopService(_web));
        await Task.WhenAll(StartApi(), StartWeb());
    }, "เริ่ม API และ Web ใหม่แล้ว — ผู้ใช้อาจต้อง login ใหม่");

    private static void OpenWeb() => Process.Start(new ProcessStartInfo(WebUrl) { UseShellExecute = true });

    // ---------- System tray: ย่อหน้าต่าง = ซ่อนลง tray, คลิกไอคอน = เปิดกลับ, คลิกขวา = เมนูสั่งงาน ----------

    private void SetupTray()
    {
        var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
        var open = new ToolStripMenuItem("เปิด System Manager") { Font = new Font(Theme.Body, FontStyle.Bold) };
        open.Click += (_, _) => RestoreFromTray();
        _trayStartAll.Click += async (_, _) => await StartAll();
        _trayRestartAll.Click += async (_, _) => await RestartAll();
        _trayOpenWeb.Click += (_, _) => OpenWeb();
        var exit = new ToolStripMenuItem("ออกจากโปรแกรม");
        exit.Click += (_, _) => Close();
        menu.Items.AddRange([open, _trayStatus, new ToolStripSeparator(), _trayStartAll, _trayRestartAll, _trayOpenWeb, new ToolStripSeparator(), exit]);
        // อัปเดตสถานะ/ปุ่มตอนเปิดเมนู — ไม่ต้องเขียนทุกวินาที
        menu.Opening += (_, _) =>
        {
            var anyBusy = _api.Busy is not null || _web.Busy is not null;
            _trayStatus.Text = $"API: {HealthText(_api)} · Web: {HealthText(_web)}";
            _trayStartAll.Visible = _api.Health == Health.Stopped || _web.Health == Health.Stopped;
            _trayStartAll.Enabled = _trayRestartAll.Enabled = !anyBusy;
            _trayOpenWeb.Enabled = _web.Health == Health.Running;
        };
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
        _tray.BalloonTipClicked += (_, _) => RestoreFromTray();
        _tray.Text = "ProMaxx2 QA System Manager";
        UpdateAppIcon(Tone.Neutral);
        _tray.Visible = true;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized) HideToTray();
            else _restoreState = WindowState;
        };
    }

    private void HideToTray()
    {
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(4000, "System Manager ยังทำงานอยู่", "ซ่อนไว้ที่ system tray — คลิกไอคอนเพื่อเปิด, คลิกขวาเพื่อสั่ง Start/Restart", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = _restoreState;
        Activate();
    }

    private bool IsOnScreen => Visible && WindowState != FormWindowState.Minimized;

    /// <summary>หน้าต่างเปิดอยู่ → แถบแจ้งผลด้านบน; ซ่อนอยู่ใน tray → แจ้งเตือนของ Windows แทน</summary>
    private void Notify(string message, Tone tone)
    {
        if (IsOnScreen) { _notice.Show(message, tone); return; }
        var (title, icon) = tone switch
        {
            Tone.Success => ("สำเร็จ", ToolTipIcon.Info),
            Tone.Danger => ("ดำเนินการไม่สำเร็จ", ToolTipIcon.Error),
            Tone.Warning => ("แจ้งเตือน", ToolTipIcon.Warning),
            _ => ("ProMaxx2 QA", ToolTipIcon.Info),
        };
        _tray.ShowBalloonTip(5000, title, message, icon);
    }

    private static string HealthText(ManagedService svc) => svc.Busy ?? svc.Health switch
    {
        Health.Running => "ทำงาน",
        Health.Unresponsive => "ไม่ตอบสนอง",
        Health.Stopped => "หยุด",
        _ => "กำลังตรวจสอบ",
    };

    /// <summary>ไอคอนหน้าต่าง/tray = กล่อง primary ตัว Q + จุดสถานะมุมขวาล่าง (เขียว/เหลือง/แดง) สร้างใหม่เฉพาะตอนสีเปลี่ยน</summary>
    private void UpdateAppIcon(Tone tone)
    {
        if (_iconTone == tone) return;
        _iconTone = tone;
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width) * 2;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            float n = size;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using (var path = Theme.Rounded(new RectangleF(0, 0, n - 1, n - 1), n * 0.22f))
            using (var brush = new SolidBrush(Theme.Primary))
                g.FillPath(brush, path);
            using (var font = new Font("Segoe UI", n * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("Q", font, Brushes.White, new RectangleF(tone == Tone.Neutral ? 0 : -n * 0.1f, -n * 0.08f, n, n), format);
            if (tone != Tone.Neutral)
            {
                var d = n * 0.5f;
                using var ring = new SolidBrush(Color.White);
                using var dot = new SolidBrush(Theme.ToneColor(tone));
                g.FillEllipse(ring, n - d, n - d, d, d);
                g.FillEllipse(dot, n - d + n * 0.06f, n - d + n * 0.06f, d - n * 0.12f, d - n * 0.12f);
            }
        }
        var previous = _appIcon;
        _appIcon = Icon.FromHandle(bitmap.GetHicon());
        Icon = _appIcon;
        _tray.Icon = _appIcon;
        ReleaseIcon(previous);
    }

    /// <summary>Icon.FromHandle ไม่คืน HICON เอง — ต้อง DestroyIcon ไม่งั้น GDI handle รั่วทุกครั้งที่สีเปลี่ยน</summary>
    private static void ReleaseIcon(Icon? icon)
    {
        if (icon is null) return;
        Native.DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    private int S(int value) => LogicalToDeviceUnits(value);

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "ยืนยัน", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.OK;

    /// <summary>
    /// ตัวจัดการกลางของทุกปุ่มที่เริ่ม/หยุดบริการ: ล็อกปุ่มของบริการนั้นทันที (กันกดซ้ำ), ผิดพลาดแสดงแถบแจ้งเตือนด้านบน
    /// แทน MessageBox ที่บังหน้าจอ แล้วตรวจสถานะใหม่เสมอ
    /// </summary>
    private async Task Run(ManagedService[] services, Func<Task> work, string success)
    {
        if (services.Any(s => s.Busy is not null)) return;
        foreach (var s in services) s.SetBusy("กำลังตรวจสอบ");
        UpdateUi();
        try
        {
            await work();
            Notify(success, Tone.Success);
        }
        catch (Exception ex)
        {
            if (ex is not ServiceException) Log(ex.Message, LogLevel.Error);
            Notify(ex.Message, Tone.Danger);
        }
        finally
        {
            foreach (var s in services) s.SetBusy(null);
            UpdateUi();
            await RefreshStatus();
            _ = RefreshEnvironment(); // ล้างป้าย "มีโค้ดใหม่" หลัง Restart
        }
    }

    private async Task StartApi()
    {
        if (await PortOpen(_api.Port)) { Log("API ทำงานอยู่แล้ว"); return; }
        _api.SetBusy("กำลังเริ่ม"); UpdateUi();
        // API สาธารณะต้องรันแบบ Production ด้วย JWT key จาก User environment variable (อ่านใหม่จาก registry ทุกครั้ง
        // เพราะ process ของ Service Manager ที่เปิดค้างไว้จะไม่เห็นตัวแปรที่ตั้งหลังจากเปิดโปรแกรม) — ถ้ายังไม่ได้ตั้ง
        // Jwt__Key จะ fallback เป็น Development profile เดิมเพื่อไม่ให้ API สตาร์ทไม่ขึ้น
        var jwtKey = Environment.GetEnvironmentVariable("Jwt__Key", EnvironmentVariableTarget.User);
        var profile = string.IsNullOrWhiteSpace(jwtKey) ? "http" : "production";
        if (profile == "http") Log("ไม่พบ User environment variable Jwt__Key — เริ่ม API แบบ Development", LogLevel.Warning);
        var exited = StartProcess("dotnet.exe", $"run --project \"{Path.Combine(_root, "src", "ProMaxx2.QA.Api", "ProMaxx2.QA.Api.csproj")}\" --launch-profile {profile}", _root, "API",
            string.IsNullOrWhiteSpace(jwtKey) ? null : new Dictionary<string, string> { ["Jwt__Key"] = jwtKey });
        // dotnet run ต้อง build ก่อนเปิด Port (มัก 20–60 วินาที) — รอได้ถึง 3 นาที แต่ถ้า process จบเอง (build error) แจ้งทันที
        await WaitForPort(_api, exited, TimeSpan.FromMinutes(3));
    }

    private async Task StartWeb()
    {
        if (await PortOpen(_web.Port)) { Log("Web ทำงานอยู่แล้ว"); return; }
        _web.SetBusy("กำลังเริ่ม"); UpdateUi();
        var exited = StartProcess("cmd.exe", "/c npm.cmd run dev", Path.Combine(_root, "src", "ProMaxx2.QA.Web"), "WEB");
        await WaitForPort(_web, exited, TimeSpan.FromMinutes(1));
    }

    /// <returns>Task ที่จบเมื่อ process ออก (ค่า = exit code) — ใช้แยก "ยังเริ่มไม่เสร็จ" ออกจาก "ล้มระหว่างเริ่ม"</returns>
    private Task<int> StartProcess(string file, string args, string workingDirectory, string source, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file, args) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (environment is not null) foreach (var (name, value) in environment) info.Environment[name] = value;
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) ProcessLog(source, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) ProcessLog(source, e.Data); };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => { exited.TrySetResult(process.ExitCode); process.Dispose(); };
        process.Start();
        var pid = process.Id;
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        Log($"เริ่ม {source} (PID {pid})");
        return exited.Task;
    }

    /// <summary>
    /// Restart แบบปลอดภัย: build API ลงโฟลเดอร์แยกใน %TEMP% ก่อน (ไม่ชน DLL ที่ API ตัวที่รันอยู่ล็อกไว้ — วิธีเดียวกับ pre-pr-check.ps1)
    /// ถ้า build ไม่ผ่านจะไม่หยุด API ตัวเดิม ผู้ใช้ยังใช้งานต่อได้
    /// </summary>
    private async Task VerifyApiBuild()
    {
        _api.SetBusy("กำลังตรวจ build"); UpdateUi();
        Log("ตรวจ build ของ API ก่อนหยุดตัวที่รันอยู่...");
        var output = Path.Combine(Path.GetTempPath(), "qa-svcmgr-verify", "api");
        var project = Path.Combine(_root, "src", "ProMaxx2.QA.Api", "ProMaxx2.QA.Api.csproj");
        var exited = StartProcess("dotnet.exe", $"build \"{project}\" --nologo -v q -o \"{output}\"", _root, "BUILD");
        var finished = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromMinutes(5)));
        if (finished != exited)
        {
            Log("ตรวจ build นานเกิน 5 นาที — ยกเลิก Restart", LogLevel.Error);
            throw new ServiceException("ตรวจ build ของ API นานเกิน 5 นาที — ยกเลิก Restart, API ตัวเดิมยังทำงานอยู่");
        }
        if (await exited != 0)
        {
            Log("build ของ API ไม่ผ่าน — ไม่หยุด API ตัวที่รันอยู่", LogLevel.Error);
            throw new ServiceException("Build ของ API ไม่ผ่าน — ไม่ได้ Restart, API ตัวเดิมยังทำงานอยู่ (ดูบรรทัดสีแดง [BUILD] ใน Activity Log)");
        }
        Log("build ของ API ผ่าน — เริ่ม Restart", LogLevel.Success);
    }

    private async Task StopService(ManagedService svc)
    {
        var pids = await Task.Run(FindListeningPids);
        if (!pids.TryGetValue(svc.Port, out var pid)) { Log($"{svc.Name} หยุดอยู่แล้ว"); return; }
        svc.SetBusy("กำลังหยุด"); UpdateUi();
        Log($"หยุด {svc.Name} (PID {pid})");
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { } // process จบไปก่อนแล้ว
        catch (OperationCanceledException)
        {
            Log($"{svc.Name} (PID {pid}) ไม่หยุดภายใน 15 วินาที", LogLevel.Error);
            throw new ServiceException($"{svc.Name} ไม่หยุดภายใน 15 วินาที — ลองกด Stop อีกครั้ง หรือปิด process {pid} ใน Task Manager");
        }
        Log($"{svc.Name} หยุดแล้ว", LogLevel.Success);
    }

    private async Task WaitForPort(ManagedService svc, Task exited, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        var nextNotice = TimeSpan.FromSeconds(15);
        while (watch.Elapsed < timeout)
        {
            if (await PortOpen(svc.Port)) { Log($"{svc.Name} พร้อมใช้งานที่ Port {svc.Port} ({watch.Elapsed.TotalSeconds:0} วินาที)", LogLevel.Success); return; }
            // Port อาจถูกเปิดโดย process ลูก (dotnet run → API.exe) แม้ตัวที่เราเริ่มจะจบแล้ว จึงเช็ค Port ซ้ำอีกครั้งก่อนสรุปว่าล้ม
            if (exited.IsCompleted && !await PortOpen(svc.Port))
            {
                Log($"{svc.Name} หยุดทำงานระหว่างเริ่ม", LogLevel.Error);
                throw new ServiceException($"{svc.Name} หยุดทำงานระหว่างเริ่ม (เช่น build ไม่ผ่านหรือ Port ถูกใช้อยู่) — ดูบรรทัดสีแดงใน Activity Log");
            }
            if (watch.Elapsed >= nextNotice) { Log($"กำลังรอ {svc.Name} เริ่มทำงาน... {watch.Elapsed.TotalSeconds:0} วินาที"); nextNotice += TimeSpan.FromSeconds(15); }
            await Task.Delay(500);
        }
        Log($"{svc.Name} ยังไม่เปิด Port {svc.Port} ภายใน {timeout.TotalSeconds:0} วินาที", LogLevel.Warning);
        throw new ServiceException($"{svc.Name} ยังไม่พร้อมภายใน {timeout.TotalSeconds:0} วินาที — process อาจยังทำงานอยู่ สถานะจะเปลี่ยนเองเมื่อพร้อม");
    }

    private async Task SetScheduleWorker(bool enabled)
    {
        if (_workerBusy) return;
        _workerBusy = true; UpdateUi();
        try
        {
            using var response = await _http.PostAsync($"{WorkerUrl}?enabled={enabled.ToString().ToLowerInvariant()}", null);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"เปลี่ยนสถานะ Schedule Worker ไม่สำเร็จ (HTTP {(int)response.StatusCode})");
            Log(enabled ? "เปิด Automation Schedule Worker" : "ปิด Automation Schedule Worker", LogLevel.Success);
            Notify(enabled ? "เปิด Automation Schedule Worker แล้ว" : "ปิด Automation Schedule Worker แล้ว", Tone.Success);
            await RefreshScheduleWorkerStatus();
        }
        catch (Exception ex)
        {
            var message = ex is HttpRequestException or TaskCanceledException ? "ติดต่อ API ไม่ได้ — ตรวจว่า API กำลังทำงาน" : ex.Message;
            Log(message, LogLevel.Error);
            Notify(message, Tone.Danger);
        }
        finally { _workerBusy = false; UpdateUi(); }
    }

    private async Task RefreshScheduleWorkerStatus()
    {
        try
        {
            using var response = await _http.GetAsync(WorkerUrl);
            var json = await response.Content.ReadAsStringAsync();
            _workerEnabled = response.IsSuccessStatusCode ? json.Contains("\"enabled\":true", StringComparison.OrdinalIgnoreCase) : null;
        }
        catch { _workerEnabled = null; }
    }

    /// <summary>
    /// ตรวจทั้งสองบริการพร้อมกัน และรัน netstat บน thread pool — เดิมรัน netstat 2 รอบแบบรอผลบน UI thread ทุก 3 วินาที ทำให้หน้าต่างกระตุก
    /// ถ้ามีคำขอตรวจระหว่างที่กำลังตรวจอยู่ จะตรวจซ้ำอีกรอบหลังจบ (ไม่ทิ้งคำขอ)
    /// </summary>
    private async Task RefreshStatus()
    {
        if (_refreshing) { _refreshQueued = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshQueued = false;
                var pidsTask = Task.Run(FindListeningPids);
                var apiTask = CheckHealth(ApiHealthUrl, _api.Port);
                var webTask = CheckHealth(WebUrl, _web.Port);
                await Task.WhenAll(pidsTask, apiTask, webTask);
                var (apiBefore, webBefore) = (_api.Health, _web.Health);
                Apply(_api, apiTask.Result, pidsTask.Result);
                Apply(_web, webTask.Result, pidsTask.Result);
                AlertIfStopped(_api, apiBefore);
                AlertIfStopped(_web, webBefore);
                if (_api.Health == Health.Running) await RefreshScheduleWorkerStatus(); else _workerEnabled = null;
                if (!IsDisposed) UpdateUi();
            } while (_refreshQueued && !IsDisposed);
        }
        finally { _refreshing = false; }
    }

    /// <summary>บริการที่เคยทำงานหยุดเองโดยไม่ได้กดจากโปรแกรมนี้ (เช่น crash) → แจ้งเตือน (ซ่อนใน tray = notification ของ Windows)</summary>
    private void AlertIfStopped(ManagedService svc, Health before)
    {
        if (before != Health.Running || svc.Health != Health.Stopped || svc.Busy is not null || DateTime.UtcNow - svc.LastActionAt < TimeSpan.FromSeconds(15)) return;
        Log($"{svc.Name} หยุดทำงานโดยไม่ได้สั่งจากโปรแกรมนี้", LogLevel.Error);
        if (!_settings.AutoRestartOnCrash) { Notify($"{svc.Name} หยุดทำงาน — เปิด System Manager แล้วกด Start", Tone.Danger); return; }
        var now = DateTime.UtcNow;
        while (svc.AutoRestarts.Count > 0 && now - svc.AutoRestarts.Peek() > TimeSpan.FromMinutes(10)) svc.AutoRestarts.Dequeue();
        if (svc.AutoRestarts.Count >= 3)
        {
            Log($"{svc.Name} หยุดเองซ้ำ 3 ครั้งใน 10 นาที — หยุด Start อัตโนมัติ", LogLevel.Error);
            Notify($"{svc.Name} หยุดเองซ้ำหลายครั้ง — ไม่ Start อัตโนมัติแล้ว ตรวจ error ใน Activity Log", Tone.Danger);
            return;
        }
        svc.AutoRestarts.Enqueue(now);
        Log($"Start {svc.Name} ใหม่อัตโนมัติ (ครั้งที่ {svc.AutoRestarts.Count} ใน 10 นาที)", LogLevel.Warning);
        _ = Run([svc], svc == _api ? StartApi : StartWeb, $"{svc.Name} หยุดเอง — Start ใหม่อัตโนมัติแล้ว");
    }

    // ---------- สภาพแวดล้อม: โดเมนสาธารณะ / Tunnel / SQL Server / ดิสก์ / โค้ดใหม่ / git ----------

    private async Task RefreshEnvironment()
    {
        if (_envRefreshing || IsDisposed) return;
        _envRefreshing = true;
        try
        {
            var webTask = Probe(PublicWebUrl);
            var apiTask = Probe(PublicApiUrl);
            var localTask = Task.Run(CollectLocal);
            await Task.WhenAll(webTask, apiTask, localTask);
            if (!IsDisposed) ApplyEnvironment(localTask.Result with { PublicWeb = webTask.Result, PublicApi = apiTask.Result });
        }
        finally { _envRefreshing = false; }
    }

    private async Task<ProbeResult> Probe(string url)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await _publicHttp.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            return new ProbeResult(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode} · {watch.Elapsed.TotalSeconds:0.00} วินาที");
        }
        catch (TaskCanceledException) { return new ProbeResult(false, "ไม่ตอบภายใน 8 วินาที"); }
        catch (HttpRequestException) { return new ProbeResult(false, "เชื่อมต่อไม่ได้"); }
    }

    private EnvSnapshot CollectLocal()
    {
        var src = Path.Combine(_root, "src");
        var apiCode = LatestWrite(new[] { "ProMaxx2.QA.Api", "ProMaxx2.QA.Application", "ProMaxx2.QA.Domain", "ProMaxx2.QA.Infrastructure" }.Select(d => Path.Combine(src, d)), ["*.cs", "*.csproj", "appsettings*.json"], recursive: true);
        // Vite เสิร์ฟ source สด — ต้อง Restart Web เฉพาะเมื่อไฟล์ตั้งค่า/แพ็กเกจเปลี่ยน
        var webConfig = LatestWrite([Path.Combine(src, "ProMaxx2.QA.Web")], ["vite.config.*", "package.json", "package-lock.json", ".env*", "tsconfig*.json"], recursive: false);
        var disks = new[] { Path.GetPathRoot(_root), Path.GetPathRoot(Environment.SystemDirectory) }
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => { try { return (root.TrimEnd('\\'), new DriveInfo(root).AvailableFreeSpace); } catch (IOException) { return (root.TrimEnd('\\'), -1L); } })
            .ToList();
        var branch = RunCapture("git", $"-C \"{_root}\" rev-parse --abbrev-ref HEAD")?.Trim();
        var commit = RunCapture("git", $"-C \"{_root}\" rev-parse --short HEAD")?.Trim();
        var dirty = RunCapture("git", $"-C \"{_root}\" status --porcelain")?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        return new EnvSnapshot(default, default, ServiceState(TunnelService), ServiceState(SqlService), disks, apiCode, webConfig, branch, commit, dirty);
    }

    private static DateTime? LatestWrite(IEnumerable<string> directories, string[] patterns, bool recursive)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        DateTime? latest = null;
        foreach (var dir in directories.Where(Directory.Exists))
            foreach (var pattern in patterns)
                foreach (var file in Directory.EnumerateFiles(dir, pattern, options))
                {
                    if (file.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) || file.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)) continue;
                    var time = File.GetLastWriteTime(file);
                    if (latest is null || time > latest) latest = time;
                }
        return latest;
    }

    /// <summary>สถานะ Windows service จาก sc.exe — RUNNING / STOPPED / ... หรือ null เมื่อไม่มี service นี้</summary>
    private static string? ServiceState(string name)
    {
        var output = RunCapture("sc.exe", $"query \"{name}\"");
        if (output is null) return null;
        // "        STATE              : 4  RUNNING \r" — ตัดด้วย whitespace ทุกชนิด (รวม \r) แล้วเอาคำหลังรหัสตัวเลข
        var line = output.Split('\n').FirstOrDefault(x => x.TrimStart().StartsWith("STATE", StringComparison.Ordinal));
        var parts = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 4 } ? parts[3] : null;
    }

    private static string? RunCapture(string file, string args)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 });
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private void ApplyEnvironment(EnvSnapshot env)
    {
        _tilePublicWeb.Set(env.PublicWeb.Ok ? "เข้าได้" : "เข้าไม่ได้", env.PublicWeb.Ok ? Tone.Success : Tone.Danger, env.PublicWeb.Detail);
        _tilePublicApi.Set(env.PublicApi.Ok ? "เข้าได้" : "เข้าไม่ได้", env.PublicApi.Ok ? Tone.Success : Tone.Danger, env.PublicApi.Detail);
        SetServiceTile(_tileTunnel, env.Tunnel, TunnelService);
        SetServiceTile(_tileSql, env.Sql, SqlService);
        var known = env.Disks.Where(d => d.FreeBytes >= 0).ToList();
        var minFree = known.Count > 0 ? known.Min(d => d.FreeBytes) / 1024d / 1024 / 1024 : (double?)null;
        _tileDisk.Set(minFree is { } gb ? $"เหลือ {gb:0} GB" : "อ่านไม่ได้", minFree switch { null => Tone.Neutral, < 5 => Tone.Danger, < 15 => Tone.Warning, _ => Tone.Success },
            string.Join(" · ", env.Disks.Select(d => d.FreeBytes >= 0 ? $"{d.Drive} {d.FreeBytes / 1024d / 1024 / 1024:0} GB" : $"{d.Drive} -")));

        // เทียบกับเวลาเริ่มของ process ใน UpdateUi (ทุกวินาที) — ตอนตรวจรอบแรกอาจยังไม่รู้เวลาเริ่ม
        _api.LatestCodeAt = env.ApiCodeAt;
        _web.LatestCodeAt = env.WebConfigAt;
        var git = env.Branch is null ? "" : $" · {env.Branch} @ {env.Commit}" + (env.DirtyFiles > 0 ? $" · แก้ไขค้าง {env.DirtyFiles} ไฟล์" : "");
        var subtitle = _root + git;
        if (_subtitle.Text != subtitle) _subtitle.Text = subtitle;

        // แจ้งเตือนเมื่อเปลี่ยนจากปกติเป็นผิดปกติเท่านั้น (ไม่เตือนซ้ำทุก 30 วินาที)
        if (_lastTunnel == "RUNNING" && env.Tunnel != "RUNNING") { Log("Cloudflare Tunnel หยุดทำงาน — ผู้ใช้ภายนอกเข้าระบบไม่ได้", LogLevel.Error); Notify("Cloudflare Tunnel หยุดทำงาน — ผู้ใช้ภายนอกเข้าระบบไม่ได้", Tone.Danger); }
        if (_lastSql == "RUNNING" && env.Sql != "RUNNING") { Log("SQL Server 2022 หยุดทำงาน", LogLevel.Error); Notify("SQL Server 2022 หยุดทำงาน — API จะอ่าน/บันทึกข้อมูลไม่ได้", Tone.Danger); }
        _lastTunnel = env.Tunnel;
        _lastSql = env.Sql;
        // ผ่านโดเมนไม่ได้ทั้งที่ในเครื่องทำงานอยู่ 2 รอบติด (1 นาที) = ปัญหาที่ Tunnel/DNS ไม่ใช่ตัวแอป
        var publicDown = (_web.Health == Health.Running && !env.PublicWeb.Ok) || (_api.Health == Health.Running && !env.PublicApi.Ok);
        _publicFailures = publicDown ? _publicFailures + 1 : 0;
        if (_publicFailures == 2)
        {
            Log("เข้าผ่านโดเมนสาธารณะไม่ได้ทั้งที่ในเครื่องทำงานอยู่ — ตรวจ Cloudflare Tunnel / อินเทอร์เน็ต", LogLevel.Error);
            Notify("ผู้ใช้ภายนอกเข้าผ่านโดเมนไม่ได้ — ตรวจ Cloudflare Tunnel / อินเทอร์เน็ต", Tone.Danger);
        }
        UpdateUi();
    }

    private static void SetServiceTile(EnvTile tile, string? state, string serviceName)
    {
        if (state is null) tile.Set("ไม่พบ service", Tone.Neutral, serviceName);
        else if (state == "RUNNING") tile.Set("ทำงาน", Tone.Success, serviceName);
        else tile.Set(state.EndsWith("PENDING", StringComparison.Ordinal) ? "กำลังเปลี่ยน" : "หยุดอยู่", state.EndsWith("PENDING", StringComparison.Ordinal) ? Tone.Warning : Tone.Danger, $"{serviceName} · {state}");
    }

    private static void Apply(ManagedService svc, Health health, IReadOnlyDictionary<int, int> pids)
    {
        svc.Health = health;
        int? pid = health == Health.Stopped ? null : pids.TryGetValue(svc.Port, out var p) ? p : null;
        if (pid != svc.Pid) { svc.Pid = pid; svc.StartedAt = null; }
    }

    /// <summary>Health ไม่ตอบแต่ Port ยังเปิด = "ไม่ตอบสนอง" (เช่นกำลังยุ่ง) — แยกจาก "หยุดอยู่" ไม่ให้ผู้ใช้กด Start ซ้ำ</summary>
    private async Task<Health> CheckHealth(string url, int port)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode) return Health.Running;
        }
        catch { }
        return await PortOpen(port) ? Health.Unresponsive : Health.Stopped;
    }

    private static async Task<bool> PortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port).WaitAsync(TimeSpan.FromMilliseconds(500));
            return true;
        }
        catch { return false; }
    }

    /// <summary>ทุก 1 วินาที: วัด CPU/RAM ของ process ที่ฟัง Port, เวลาที่ทำงานมา และตัวนับวินาทีของงานที่กำลังทำ</summary>
    private void Tick()
    {
        var now = DateTime.UtcNow;
        Sample(_api, now);
        Sample(_web, now);
        foreach (var stale in _processSamples.Keys.Where(pid => pid != _api.Pid && pid != _web.Pid).ToArray()) _processSamples.Remove(stale);
        _chart.AddSample(Math.Clamp(_api.Cpu + _web.Cpu, 0, 100), _api.RamMb + _web.RamMb, $"API {_api.RamMb:0} MB · Web {_web.RamMb:0} MB");
        UpdateUi();
    }

    private void Sample(ManagedService svc, DateTime now)
    {
        if (svc.Pid is not { } pid) { svc.Cpu = 0; svc.RamMb = 0; return; }
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Refresh();
            svc.RamMb = process.WorkingSet64 / (1024f * 1024f);
            var cpu = process.TotalProcessorTime;
            if (_processSamples.TryGetValue(pid, out var previous))
            {
                var elapsed = (now - previous.sampledAt).TotalMilliseconds;
                if (elapsed > 0) svc.Cpu = (float)((cpu - previous.cpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount) * 100);
            }
            _processSamples[pid] = (cpu, now);
            svc.StartedAt ??= process.StartTime;
        }
        catch { _processSamples.Remove(pid); svc.Cpu = 0; svc.RamMb = 0; }
    }

    private void UpdateUi()
    {
        if (IsDisposed) return;
        foreach (var svc in new[] { _api, _web })
        {
            var busy = svc.Busy is not null;
            if (busy) svc.Card.Status.Set(svc.BusyElapsed.TotalSeconds >= 1 ? $"{svc.Busy}... {svc.BusyElapsed.TotalSeconds:0} วินาที" : $"{svc.Busy}...", Tone.Warning);
            else
            {
                var (text, tone) = svc.Health switch
                {
                    Health.Running => ("กำลังทำงาน", Tone.Success),
                    Health.Unresponsive => ("ไม่ตอบสนอง", Tone.Warning),
                    Health.Stopped => ("หยุดอยู่", Tone.Danger),
                    _ => ("กำลังตรวจสอบ", Tone.Neutral),
                };
                svc.Card.Status.Set(text, tone);
                // แสดงเฉพาะปุ่มที่ใช้ได้กับสถานะตอนนี้ (หยุดอยู่ → Start, ทำงานอยู่ → Restart / Stop)
                svc.Start.Visible = svc.Health != Health.Unknown && !svc.IsUp;
                svc.Restart.Visible = svc.Stop.Visible = svc.IsUp;
            }
            svc.Start.Enabled = svc.Restart.Enabled = svc.Stop.Enabled = !busy;
            svc.Card.SetDetail($"Port {svc.Port} · {svc.Tech}" + (svc.Pid is { } pid ? $" · PID {pid}" : ""));
            svc.Card.SetHint(!busy && svc.IsUp && svc.CodeChangedAt is { } changed
                ? svc == _api ? $"มีโค้ดใหม่ (แก้ {changed:HH:mm}) — ควร Restart"
                    : $"ตั้งค่า Vite/แพ็กเกจเปลี่ยน ({changed:HH:mm}) — ควร Restart"
                : "");
            svc.Card.SetMetrics(svc.IsUp && svc.Pid is not null
                ? $"CPU {svc.Cpu:0.0}% · RAM {svc.RamMb:0} MB" + (svc.StartedAt is { } started ? $" · ทำงานมา {Uptime(DateTime.Now - started)}" : "")
                : svc.Health == Health.Stopped ? "กด Start เพื่อเริ่มบริการ" : "");
        }

        var apiUp = _api.Health == Health.Running;
        if (_workerBusy) _workerCard.Status.Set("กำลังเปลี่ยน...", Tone.Warning);
        else if (!apiUp) _workerCard.Status.Set("รอ API", Tone.Neutral);
        else if (_workerEnabled is true) _workerCard.Status.Set("เปิดอยู่", Tone.Success);
        else if (_workerEnabled is false) _workerCard.Status.Set("ปิดอยู่", Tone.Warning);
        else _workerCard.Status.Set("ไม่ทราบสถานะ", Tone.Neutral);
        _workerCard.SetMetrics(!apiUp ? "เริ่ม API ก่อนจึงจะเปิด/ปิดได้"
            : _workerEnabled is true ? "รัน Automation Schedule ที่ถึงเวลาให้อัตโนมัติ"
            : _workerEnabled is false ? "Schedule จะไม่ถูกรันจนกว่าจะเปิด Worker" : "อ่านสถานะจาก API ไม่ได้");
        _workerOff.Visible = _workerEnabled is true;
        _workerOn.Visible = _workerEnabled is not true;
        _workerOn.Enabled = _workerOff.Enabled = apiUp && !_workerBusy;

        var anyBusy = _api.Busy is not null || _web.Busy is not null;
        var down = new[] { _api, _web }.Where(s => s.Health == Health.Stopped).Select(s => s.Name).ToList();
        if (anyBusy) _overall.Set("กำลังดำเนินการ...", Tone.Warning);
        else if (_api.Health == Health.Unknown || _web.Health == Health.Unknown) _overall.Set("กำลังตรวจสอบ", Tone.Neutral);
        else if (down.Count == 2) _overall.Set("ระบบหยุดอยู่", Tone.Danger);
        else if (down.Count == 1) _overall.Set($"{down[0]} หยุดอยู่", Tone.Danger);
        else if (_api.Health == Health.Unresponsive || _web.Health == Health.Unresponsive) _overall.Set("มีบริการไม่ตอบสนอง", Tone.Warning);
        else _overall.Set("ระบบพร้อมใช้งาน", Tone.Success);
        UpdateAppIcon(_overall.Tone);
        var tip = $"ProMaxx2 QA — {_overall.Text}";
        if (_tray.Text != tip) _tray.Text = tip.Length > 63 ? tip[..63] : tip; // NotifyIcon.Text ยาวได้ไม่เกิน 63 ตัวอักษร
        _startAll.Visible = down.Count > 0;
        _startAll.Enabled = _restartAll.Enabled = !anyBusy;
        _restartAll.Margin = _startAll.Visible ? new Padding(0, 0, S(8), 0) : Padding.Empty;
        _openWeb.Enabled = _web.Health == Health.Running;
    }

    private static string Uptime(TimeSpan t) =>
        t.TotalMinutes < 1 ? "ไม่ถึง 1 นาที"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} นาที"
        : t.TotalDays < 1 ? $"{(int)t.TotalHours} ชม. {t.Minutes} นาที"
        : $"{(int)t.TotalDays} วัน {t.Hours} ชม.";

    private static Dictionary<int, int> FindListeningPids()
    {
        var result = new Dictionary<int, int>();
        using var netstat = Process.Start(new ProcessStartInfo("netstat.exe", "-ano -p TCP") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
        if (netstat is null) return result;
        var output = netstat.StandardOutput.ReadToEnd();
        netstat.WaitForExit();
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase) || !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[4], out var pid)) continue;
            var colon = parts[1].LastIndexOf(':');
            if (colon >= 0 && int.TryParse(parts[1][(colon + 1)..], out var port)) result.TryAdd(port, pid);
        }
        return result;
    }

    // ---------- Activity Log ----------

    private void Log(string message, LogLevel level = LogLevel.System) => EnqueueLog(new LogEntry(DateTime.Now, null, message, level));

    private void ProcessLog(string source, string line)
    {
        var level = Classify(line, _lastLevel.GetValueOrDefault(source, LogLevel.Info));
        _lastLevel[source] = level;
        EnqueueLog(new LogEntry(DateTime.Now, source, line, level));
    }

    /// <summary>
    /// แยกระดับจากรูปแบบ log ของ ASP.NET ("fail:", "warn:", "info:") — บรรทัดที่ขึ้นต้นด้วยช่องว่าง (เนื้อความ/SQL/stack trace)
    /// ใช้ระดับของบรรทัดก่อนหน้า; บรรทัดอื่น (build, Vite) ดูจากคำว่า error/warning
    /// </summary>
    private static LogLevel Classify(string line, LogLevel previous)
    {
        var text = line.TrimStart();
        if (text.StartsWith("fail:", StringComparison.Ordinal) || text.StartsWith("crit:", StringComparison.Ordinal)) return LogLevel.Error;
        if (text.StartsWith("warn:", StringComparison.Ordinal)) return LogLevel.Warning;
        if (text.StartsWith("info:", StringComparison.Ordinal) || text.StartsWith("dbug:", StringComparison.Ordinal) || text.StartsWith("trce:", StringComparison.Ordinal)) return LogLevel.Info;
        if (line.Length > 0 && char.IsWhiteSpace(line[0])) return previous;
        if (text.Contains("error", StringComparison.OrdinalIgnoreCase) || text.Contains("exception", StringComparison.OrdinalIgnoreCase) || text.Contains("failed", StringComparison.OrdinalIgnoreCase)) return LogLevel.Error;
        if (text.Contains("warning", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warning;
        return LogLevel.Info;
    }

    private void EnqueueLog(LogEntry entry)
    {
        _pendingLogs.Enqueue(entry);
        var queued = Interlocked.Increment(ref _queuedLogLines);
        while (queued > MaxQueuedLogLines && _pendingLogs.TryDequeue(out _)) queued = Interlocked.Decrement(ref _queuedLogLines);
    }

    /// <summary>ค่าเริ่มต้นซ่อน log ระดับ info ของ API/Web (เช่น SQL ของ EF ที่ท่วมหน้าจอ) แต่แสดงข้อความระบบ คำเตือน และ error เสมอ</summary>
    private bool IsVisible(LogEntry e) => _showAll.Checked || e.Source is null || e.Level is not LogLevel.Info;

    private void DrainLogs()
    {
        if (IsDisposed || _pendingLogs.IsEmpty) return;
        var batch = new List<LogEntry>();
        while (batch.Count < 500 && _pendingLogs.TryDequeue(out var entry)) { Interlocked.Decrement(ref _queuedLogLines); batch.Add(entry); }
        if (batch.Count == 0) return;
        _history.AddRange(batch);
        if (_history.Count > MaxHistory) _history.RemoveRange(0, _history.Count - MaxHistory);
        var visible = batch.Where(IsVisible).ToList();
        _hiddenCount += batch.Count - visible.Count;
        AppendLog(visible);
        UpdateHiddenInfo();
    }

    private void RenderAll()
    {
        _log.Clear();
        var visible = _history.Where(IsVisible).ToList();
        _hiddenCount = _history.Count - visible.Count;
        var start = visible.Count;
        for (var size = 0; start > 0 && size < MaxLogCharacters; ) size += visible[--start].Text.Length + 20;
        AppendLog(visible.GetRange(start, visible.Count - start));
        UpdateHiddenInfo();
    }

    private void AppendLog(IReadOnlyList<LogEntry> entries)
    {
        if (entries.Count == 0) return;
        var atBottom = LogAtBottom();
        var scroll = Point.Empty;
        if (!atBottom) Native.SendMessage(_log.Handle, Native.EmGetScrollPos, IntPtr.Zero, ref scroll);
        var (selStart, selLength) = (_log.SelectionStart, _log.SelectionLength);
        Native.SendMessage(_log.Handle, Native.WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        try
        {
            // ต่อข้อความสีเดียวกันเป็นก้อนเดียว — ลดจำนวนครั้งที่ต้องเลือก/ระบายสีใน RichTextBox
            var chunk = new StringBuilder();
            LogLevel? current = null;
            foreach (var e in entries)
            {
                if (current is { } level && level != e.Level) WriteChunk(chunk, level);
                current = e.Level;
                chunk.Append(e.Time.ToString("HH:mm:ss")).Append("  ");
                if (e.Source is not null) chunk.Append('[').Append(e.Source).Append("] ");
                chunk.Append(e.Text).Append('\n');
            }
            if (current is { } last) WriteChunk(chunk, last);
            if (_log.TextLength > MaxLogCharacters)
            {
                var cut = _log.Text.IndexOf('\n', _log.TextLength - MaxLogCharacters + 20_000);
                if (cut > 0) { _log.Select(0, cut + 1); _log.SelectedText = ""; }
            }
            // RichTextBox เก็บ Undo ของทุกการเพิ่ม/ตัดข้อความไว้ตลอด (แม้ ReadOnly) — เปิดค้างทั้งวันหน่วยความจำโตเกิน 1 GB
            _log.ClearUndo();
            if (atBottom) { _log.Select(_log.TextLength, 0); _log.ScrollToCaret(); }
            else
            {
                _log.Select(Math.Min(selStart, _log.TextLength), Math.Min(selLength, Math.Max(0, _log.TextLength - selStart)));
                Native.SendMessage(_log.Handle, Native.EmSetScrollPos, IntPtr.Zero, ref scroll);
            }
        }
        finally
        {
            Native.SendMessage(_log.Handle, Native.WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            _log.Invalidate();
        }
    }

    private void WriteChunk(StringBuilder chunk, LogLevel level)
    {
        _log.Select(_log.TextLength, 0);
        _log.SelectionColor = level switch
        {
            LogLevel.Success => Color.FromArgb(74, 222, 128),
            LogLevel.Info => Color.FromArgb(148, 163, 184),
            LogLevel.Warning => Color.FromArgb(251, 191, 36),
            LogLevel.Error => Color.FromArgb(248, 113, 113),
            _ => Color.FromArgb(229, 231, 235),
        };
        _log.SelectedText = chunk.ToString();
        chunk.Clear();
    }

    /// <summary>ผู้ใช้เลื่อนขึ้นไปอ่าน log เก่าอยู่ → ไม่เลื่อนลงล่างอัตโนมัติ</summary>
    private bool LogAtBottom()
    {
        if (!_log.IsHandleCreated) return true;
        var info = new Native.ScrollInfo { Size = (uint)Marshal.SizeOf<Native.ScrollInfo>(), Mask = Native.SifAll };
        if (!Native.GetScrollInfo(_log.Handle, Native.SbVert, ref info)) return true;
        return info.Max <= (int)info.Page || info.Pos + (int)info.Page >= info.Max - 1;
    }

    private void UpdateHiddenInfo()
    {
        var text = _showAll.Checked ? "" : _hiddenCount > 0 ? $"ซ่อน log ปกติของ API/Web {_hiddenCount:N0} บรรทัด (error/คำเตือนแสดงเสมอ)" : "แสดงข้อความระบบ คำเตือน และ error";
        if (_hiddenInfo.Text != text) _hiddenInfo.Text = text;
    }

    private void CopyLog()
    {
        var text = _log.SelectionLength > 0 ? _log.SelectedText : _log.Text;
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); _notice.Show(_log.SelectionLength > 0 ? "คัดลอกข้อความที่เลือกแล้ว" : "คัดลอก log ที่แสดงอยู่ทั้งหมดแล้ว", Tone.Info); }
        catch (ExternalException) { _notice.Show("คัดลอกไม่สำเร็จ — โปรแกรมอื่นกำลังใช้ Clipboard ลองใหม่อีกครั้ง", Tone.Danger); }
    }

    private void ClearLog()
    {
        while (_pendingLogs.TryDequeue(out _)) Interlocked.Decrement(ref _queuedLogLines);
        _history.Clear();
        _hiddenCount = 0;
        _log.Clear();
        _log.ClearUndo();
        UpdateHiddenInfo();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { if (File.Exists(Path.Combine(directory.FullName, "ProMaxx2.QA.slnx"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("ไม่พบ ProMaxx2.QA.slnx กรุณาวางโปรแกรมไว้ภายใน Workspace ของระบบ");
    }

    private static class Native
    {
        public const int WmSetRedraw = 0x000B, EmGetScrollPos = 0x04DD, EmSetScrollPos = 0x04DE, SbVert = 1;
        public const int EmSetLangOptions = 0x0478, EmGetLangOptions = 0x0479, ImfAutoFont = 0x0002, ImfDualFont = 0x0080;
        public const uint SifAll = 0x17;
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);
        [DllImport("user32.dll")] public static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
        [StructLayout(LayoutKind.Sequential)] public struct ScrollInfo { public uint Size, Mask; public int Min, Max; public uint Page; public int Pos, TrackPos; }
    }
}

/// <summary>การ์ดพื้นขาว มุมโค้ง 12px และเส้นขอบ --line (UI_DESIGN_SYSTEM §6 Card)</summary>
internal class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Card;
        Margin = new Padding(LogicalToDeviceUnits(8));
        Padding = new Padding(LogicalToDeviceUnits(16));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(12));
        using var fill = new SolidBrush(Theme.Card);
        using var border = new Pen(Theme.Line);
        g.FillPath(fill, path);
        g.DrawPath(border, path);
    }
}

internal sealed class ServiceCard : CardPanel
{
    private readonly Label _detail;
    private readonly Label _metrics;
    private readonly Label _hint;
    private readonly FlowLayoutPanel _actions = new() { AutoSize = true, WrapContents = false, BackColor = Theme.Card, Margin = Padding.Empty, Anchor = AnchorStyles.Left | AnchorStyles.Bottom };

    public ServiceCard(string title)
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Theme.Card, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // ชื่อบรรทัดเดียวเสมอ — ถ้ายาวเกินตัดด้วย … ไม่ดันปุ่มด้านล่างจนถูกตัด
        layout.Controls.Add(new Label { Text = title, Font = Theme.CardTitle, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = TextRenderer.MeasureText(title, Theme.CardTitle).Height + LogicalToDeviceUnits(4), Margin = new Padding(0, LogicalToDeviceUnits(2), LogicalToDeviceUnits(8), 0) }, 0, 0);
        Status.Margin = Padding.Empty;
        Status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        layout.Controls.Add(Status, 1, 0);
        _detail = InfoLine();
        _metrics = InfoLine();
        layout.Controls.Add(_detail, 0, 1);
        layout.SetColumnSpan(_detail, 2);
        layout.Controls.Add(_metrics, 0, 2);
        layout.SetColumnSpan(_metrics, 2);
        _hint = InfoLine();
        _hint.ForeColor = Theme.WarningText;
        _hint.Font = Theme.SmallBold;
        _hint.Visible = false;
        layout.Controls.Add(_hint, 0, 3);
        layout.SetColumnSpan(_hint, 2);
        layout.Controls.Add(_actions, 0, 4);
        layout.SetColumnSpan(_actions, 2);
        Controls.Add(layout);
    }

    public StatusPill Status { get; } = new();
    public void SetDetail(string value) { if (_detail.Text != value) _detail.Text = value; }
    public void SetMetrics(string value) { if (_metrics.Text != value) _metrics.Text = value; }
    /// <summary>บรรทัดเตือนสีเหลือง (เช่น "มีโค้ดใหม่ ควร Restart") — ว่าง = ซ่อน</summary>
    public void SetHint(string value)
    {
        if (_hint.Text != value) _hint.Text = value;
        if (_hint.Visible != (value.Length > 0)) _hint.Visible = value.Length > 0;
    }

    public void AddActions(params Control[] buttons) => _actions.Controls.AddRange(buttons);

    private Label InfoLine() => new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = LogicalToDeviceUnits(20), Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0) };
}

/// <summary>ช่องตรวจสภาพแวดล้อม: ชื่อ → ป้ายสถานะ → รายละเอียด (ตัดด้วย … เมื่อยาว)</summary>
internal sealed class EnvTile : TableLayoutPanel
{
    private readonly Label _detail;

    public EnvTile(string name)
    {
        ColumnCount = 1;
        RowCount = 3;
        Dock = DockStyle.Fill;
        BackColor = Theme.Card;
        Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
        for (var i = 0; i < 3; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new Label { Text = name, Font = Theme.SmallBold, ForeColor = Theme.Text, BackColor = Theme.Card, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = LogicalToDeviceUnits(18), Margin = Padding.Empty }, 0, 0);
        Status.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0);
        Controls.Add(Status, 0, 1);
        _detail = new Label { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = LogicalToDeviceUnits(18), Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0), Text = "กำลังตรวจ..." };
        Controls.Add(_detail, 0, 2);
    }

    public StatusPill Status { get; } = new();

    public void Set(string status, Tone tone, string detail)
    {
        Status.Set(status, tone);
        if (_detail.Text != detail) _detail.Text = detail;
    }
}

internal enum ButtonKind { Secondary, Primary, Danger }

/// <summary>ปุ่มมุมโค้ง 8px ตาม Button Standard (§8): Primary = พื้น primary, Secondary = ขาว + เส้น --line, Danger = ตัวแดง</summary>
internal sealed class UiButton : Button
{
    private bool _hover;
    private bool _pressed;

    public UiButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        Kind = kind;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Height = LogicalToDeviceUnits(34);
        Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
        Text = text;
    }

    public ButtonKind Kind { get; }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Width = TextRenderer.MeasureText(Text, Font).Width + LogicalToDeviceUnits(30);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill, border, fore;
        if (!Enabled)
        {
            fill = Kind == ButtonKind.Primary ? Theme.Tint(Theme.Primary, 0.4f) : Color.FromArgb(0xf3, 0xf4, 0xf6);
            border = Kind == ButtonKind.Primary ? fill : Theme.Line;
            fore = Kind == ButtonKind.Primary ? Color.White : Color.FromArgb(0x9c, 0xa3, 0xaf);
        }
        else if (Kind == ButtonKind.Primary)
        {
            fill = _pressed ? Color.FromArgb(0x1a, 0x45, 0xb0) : _hover ? Color.FromArgb(0x1d, 0x4c, 0xc2) : Theme.Primary;
            border = fill;
            fore = Color.White;
        }
        else
        {
            var accent = Kind == ButtonKind.Danger ? Theme.Red : Theme.Primary;
            fill = _pressed ? Theme.Tint(accent, 0.14f) : _hover ? Theme.Tint(accent, 0.07f) : Color.White;
            border = _hover || _pressed ? Theme.Tint(accent, 0.45f) : Kind == ButtonKind.Danger ? Theme.Tint(Theme.Red, 0.3f) : Theme.Line;
            fore = Kind == ButtonKind.Danger ? Theme.Red : Theme.Text;
        }
        var radius = LogicalToDeviceUnits(8);
        using (var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var ring = Theme.Rounded(new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f), radius - 1);
            using var pen = new Pen(Theme.Tint(Theme.Primary, 0.55f), 2);
            g.DrawPath(pen, ring);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>ป้ายสถานะ: จุดสี + ข้อความ บนพื้นอ่อนของสีเดียวกัน — มีข้อความเสมอ ไม่สื่อด้วยสีอย่างเดียว (§6 Badge)</summary>
internal sealed class StatusPill : Control
{
    private Tone _tone = Tone.Neutral;

    public StatusPill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.SmallBold;
        Height = LogicalToDeviceUnits(26);
        Set("กำลังตรวจสอบ", Tone.Neutral);
    }

    public Tone Tone => _tone;

    public void Set(string text, Tone tone)
    {
        if (Text == text && _tone == tone) return;
        Text = text;
        _tone = tone;
        Width = TextRenderer.MeasureText(text, Font).Width + LogicalToDeviceUnits(34);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Card);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = Theme.ToneColor(_tone);
        using (var path = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (var brush = new SolidBrush(Theme.Tint(color, 0.12f)))
            g.FillPath(brush, path);
        var dot = LogicalToDeviceUnits(8);
        using (var brush = new SolidBrush(color))
            g.FillEllipse(brush, LogicalToDeviceUnits(11), (Height - dot) / 2f, dot, dot);
        var textBounds = new Rectangle(LogicalToDeviceUnits(24), 0, Width - LogicalToDeviceUnits(24), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Theme.ToneText(_tone), TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.Left);
    }
}

/// <summary>
/// แถบแจ้งผลด้านบนแทน MessageBox: พื้นอ่อน 9% ตามประเภท + เส้นซ้าย 5px (แบบ toast ของเว็บ §13) ไม่บังการทำงาน —
/// สำเร็จ/ข้อมูลหายเองใน 6 วินาที, error ค้าง 20 วินาที; ปิดเองได้ด้วยปุ่ม ×
/// </summary>
internal sealed class NoticeBar : Control
{
    private readonly System.Windows.Forms.Timer _hide = new();
    private Tone _tone = Tone.Info;
    private Rectangle _closeBounds;

    public NoticeBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Body;
        Height = LogicalToDeviceUnits(42);
        Visible = false;
        _hide.Tick += (_, _) => { _hide.Stop(); Visible = false; };
    }

    public void Show(string message, Tone tone)
    {
        Text = message;
        _tone = tone;
        Visible = true;
        Invalidate();
        _hide.Stop();
        _hide.Interval = tone == Tone.Danger ? 20_000 : 6_000;
        _hide.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var color = Theme.ToneColor(_tone);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.Rounded(bounds, LogicalToDeviceUnits(10)))
        using (var fill = new SolidBrush(Theme.Tint(color, 0.09f)))
        using (var border = new Pen(Theme.Tint(color, 0.3f)))
        using (var bar = new SolidBrush(color))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
            var clip = g.Clip;
            g.SetClip(path);
            g.FillRectangle(bar, 0, 0, LogicalToDeviceUnits(5), Height);
            g.Clip = clip;
        }
        // ไอคอนวงกลม: ✓ สำเร็จ, ! ผิดพลาด/เตือน, i ข้อมูล
        var size = LogicalToDeviceUnits(18);
        var icon = new RectangleF(LogicalToDeviceUnits(16), (Height - size) / 2f, size, size);
        using (var brush = new SolidBrush(color)) g.FillEllipse(brush, icon);
        if (_tone == Tone.Success)
        {
            using var pen = new Pen(Color.White, LogicalToDeviceUnits(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, [new PointF(icon.X + size * 0.28f, icon.Y + size * 0.52f), new PointF(icon.X + size * 0.44f, icon.Y + size * 0.68f), new PointF(icon.X + size * 0.73f, icon.Y + size * 0.34f)]);
        }
        else TextRenderer.DrawText(g, _tone == Tone.Info ? "i" : "!", Theme.SmallBold, Rectangle.Round(icon), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        var closeSize = LogicalToDeviceUnits(28);
        _closeBounds = new Rectangle(Width - closeSize - LogicalToDeviceUnits(8), (Height - closeSize) / 2, closeSize, closeSize);
        using (var pen = new Pen(Theme.Muted, 1.5f))
        {
            var c = new PointF(_closeBounds.X + closeSize / 2f, _closeBounds.Y + closeSize / 2f);
            var r = LogicalToDeviceUnits(5);
            g.DrawLine(pen, c.X - r, c.Y - r, c.X + r, c.Y + r);
            g.DrawLine(pen, c.X - r, c.Y + r, c.X + r, c.Y - r);
        }
        var textLeft = (int)icon.Right + LogicalToDeviceUnits(10);
        var textBounds = new Rectangle(textLeft, 0, _closeBounds.Left - textLeft - LogicalToDeviceUnits(8), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.Left);
    }

    protected override void OnMouseMove(MouseEventArgs e) { Cursor = _closeBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
    protected override void OnMouseClick(MouseEventArgs e) { if (_closeBounds.Contains(e.Location)) { _hide.Stop(); Visible = false; } base.OnMouseClick(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>กราฟ CPU (%) และ RAM (MB) รวมของ process ที่ฟัง Port API และ Web ทุก 1 วินาที ย้อนหลัง 2 นาที</summary>
internal sealed class ResourceChart : Control
{
    private const int Capacity = 120;
    private readonly Queue<float> _cpu = new();
    private readonly Queue<float> _ram = new();
    private float _currentCpu;
    private float _currentRam;
    private string _breakdown = "";

    public ResourceChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;
    }

    public void AddSample(float cpu, float ram, string breakdown)
    {
        _currentCpu = cpu; _currentRam = ram; _breakdown = breakdown;
        _cpu.Enqueue(cpu); _ram.Enqueue(ram);
        while (_cpu.Count > Capacity) _cpu.Dequeue();
        while (_ram.Count > Capacity) _ram.Dequeue();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, "การใช้ทรัพยากร", Theme.CardTitle, new Point(0, 0), Theme.Text, flags);
        var titleHeight = TextRenderer.MeasureText("การใช้ทรัพยากร", Theme.CardTitle, Size.Empty, flags).Height;
        var ramScale = Math.Max(256f, (float)Math.Ceiling(Math.Max(_currentRam, _ram.DefaultIfEmpty().Max()) / 256f) * 256f);
        // สเกลอยู่ในบรรทัดคำอธิบาย ไม่วาดในกรอบกราฟ — เดิมทับเส้น RAM ตอนค่าสูงใกล้ขอบบน
        var subtitleBounds = new Rectangle(0, titleHeight + LogicalToDeviceUnits(2), Width, TextRenderer.MeasureText("ก", Theme.Small).Height);
        TextRenderer.DrawText(g, $"รวม API + Web · {_breakdown} · ย้อนหลัง 2 นาที · เต็มกรอบ = CPU 100% / RAM {ramScale:0} MB", Theme.Small, subtitleBounds, Theme.Muted, flags | TextFormatFlags.EndEllipsis);

        // ค่าปัจจุบันชิดขวา: ● CPU  ● RAM
        var x = Width;
        foreach (var (label, color) in new[] { ($"RAM {_currentRam:0} MB", Theme.Green), ($"CPU {_currentCpu:0.0}%", Theme.Primary) })
        {
            var size = TextRenderer.MeasureText(label, Theme.SmallBold, Size.Empty, flags);
            x -= size.Width;
            TextRenderer.DrawText(g, label, Theme.SmallBold, new Point(x, LogicalToDeviceUnits(4)), color, flags);
            var dot = LogicalToDeviceUnits(8);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, x - dot - LogicalToDeviceUnits(6), LogicalToDeviceUnits(4) + (size.Height - dot) / 2f, dot, dot);
            x -= dot + LogicalToDeviceUnits(22);
        }

        var top = titleHeight + LogicalToDeviceUnits(26);
        var chart = new RectangleF(0, top, Math.Max(1, Width - 1), Math.Max(1, Height - top - 1));
        using (var background = new SolidBrush(Color.FromArgb(0xf8, 0xfa, 0xfc))) g.FillRectangle(background, chart);
        using (var grid = new Pen(Color.FromArgb(0xec, 0xef, 0xf4)))
            for (var i = 1; i < 4; i++) { var y = chart.Top + chart.Height * i / 4; g.DrawLine(grid, chart.Left, y, chart.Right, y); }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        DrawSeries(g, _cpu.ToArray(), chart, 100f, Theme.Primary);
        DrawSeries(g, _ram.ToArray(), chart, ramScale, Theme.Green);
    }

    private static void DrawSeries(Graphics g, float[] values, RectangleF area, float scale, Color color)
    {
        if (values.Length < 2) return;
        var points = new PointF[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var x = area.Right - area.Width * (values.Length - 1 - i) / (Capacity - 1f);
            var y = area.Bottom - Math.Clamp(values[i] / scale, 0, 1) * (area.Height - 2) - 1;
            points[i] = new PointF(x, y);
        }
        using var pen = new Pen(color, 2) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, points);
    }
}
