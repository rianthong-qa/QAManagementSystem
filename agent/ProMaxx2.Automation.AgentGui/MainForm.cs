using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ProMaxx2.Automation.AgentGui;

public sealed class MainForm : Form
{
    private readonly AgentConfigStore _store = new();
    private readonly TextBox _hubUrl = new(), _username = new(), _password = new(), _agentCode = new(), _heartbeat = new();
    private readonly TextBox _autExe = new(), _autExe2 = new(), _autUser = new(), _autPassword = new();
    private readonly ComboBox _dbType = new();
    private readonly TextBox _dbHost = new(), _dbPort = new(), _dbUser = new(), _dbPassword = new(), _dbDatabase = new();
    private readonly Button _btnStart = new(), _btnStop = new(), _btnSave = new(), _btnTest = new();
    private readonly Label _status = new();
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F) };
    private Process? _process;
    private Process? _process2;
    private readonly object _logLock = new();

    public MainForm()
    {
        Text = "ProMaxx2 Automation Agent — ตั้งค่าและเปิดทำงาน";
        Width = 760;
        Height = 830;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(620, 620);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Tahoma", 9.5F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildConnectionGroup(), 0, 0);
        root.Controls.Add(BuildAutGroup(), 0, 1);
        root.Controls.Add(BuildDatabaseGroup(), 0, 2);
        root.Controls.Add(BuildActionsBar(), 0, 3);

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        logPanel.Controls.Add(_log);
        _log.Dock = DockStyle.Fill;
        root.Controls.Add(logPanel, 0, 4);

        Controls.Add(root);

        _btnStart.Click += (_, _) => StartAgentsAsync();
        _btnStop.Click += (_, _) => StopAgent();
        _btnSave.Click += (_, _) => SaveConfig();
        _btnTest.Click += async (_, _) => await TestConnectionAsync();
        FormClosing += (_, e) => { StopAgent(); };

        LoadConfig();
    }

    private GroupBox BuildConnectionGroup()
    {
        var g = new GroupBox { Text = "การเชื่อมต่อ QA Hub", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        for (var i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Absolute : SizeType.Percent, i % 2 == 0 ? 120 : 50));
        for (var i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        t.Controls.Add(MakeLabel("Base URL"), 0, 0);
        t.Controls.Add(_hubUrl, 1, 0);
        t.SetColumnSpan(_hubUrl, 3);
        _hubUrl.Dock = DockStyle.Fill;
        t.Controls.Add(MakeLabel("Username"), 0, 1);
        t.Controls.Add(_username, 1, 1);
        t.Controls.Add(MakeLabel("Password"), 2, 1);
        t.Controls.Add(_password, 3, 1);
        _username.Dock = DockStyle.Fill; _password.Dock = DockStyle.Fill; _password.UseSystemPasswordChar = true;
        t.Controls.Add(MakeLabel("Agent Code"), 0, 2);
        t.Controls.Add(_agentCode, 1, 2);
        t.Controls.Add(MakeLabel("Heartbeat (s)"), 2, 2);
        t.Controls.Add(_heartbeat, 3, 2);
        _agentCode.Dock = DockStyle.Fill; _heartbeat.Dock = DockStyle.Fill;
        g.Controls.Add(t);
        return g;
    }

    private GroupBox BuildAutGroup()
    {
        var g = new GroupBox { Text = "แอปพลิเคชันที่ทดสอบ (AUT) — รองรับ 2 แอป (POS + App)", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        for (var i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Absolute : SizeType.Percent, i % 2 == 0 ? 120 : 50));
        for (var i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        t.Controls.Add(MakeLabel("AUT EXE 1"), 0, 0);
        t.Controls.Add(_autExe, 1, 0);
        t.SetColumnSpan(_autExe, 2);
        _autExe.Dock = DockStyle.Fill;
        var browse1 = new Button { Text = "Browse...", Width = 90 };
        browse1.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = "เลือก exe ตัวที่ 1 (Pos)" }; if (d.ShowDialog(this) == DialogResult.OK) _autExe.Text = d.FileName; };
        t.Controls.Add(browse1, 3, 0);

        t.Controls.Add(MakeLabel("AUT EXE 2"), 0, 1);
        t.Controls.Add(_autExe2, 1, 1);
        t.SetColumnSpan(_autExe2, 2);
        _autExe2.Dock = DockStyle.Fill;
        var browse2 = new Button { Text = "Browse...", Width = 90 };
        browse2.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = "เลือก exe ตัวที่ 2 (App)" }; if (d.ShowDialog(this) == DialogResult.OK) _autExe2.Text = d.FileName; };
        t.Controls.Add(browse2, 3, 1);

        t.Controls.Add(MakeLabel("AUT User"), 0, 2);
        t.Controls.Add(_autUser, 1, 2);
        t.Controls.Add(MakeLabel("AUT Password"), 2, 2);
        t.Controls.Add(_autPassword, 3, 2);
        _autUser.Dock = DockStyle.Fill; _autPassword.Dock = DockStyle.Fill; _autPassword.UseSystemPasswordChar = true;
        g.Controls.Add(t);
        return g;
    }

    private GroupBox BuildDatabaseGroup()
    {
        var g = new GroupBox { Text = "Database Validator (ไม่บังคับ — ใช้กับ EXPECT_STOCK/EXPECT_DB_VALUE)", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3 };
        for (var i = 0; i < 6; i++) t.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Absolute : SizeType.Percent, i % 2 == 0 ? 90 : 28));
        for (var i = 0; i < 3; i++) t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        _dbType.Items.AddRange(new object[] { "Firebird", "SqlServer" });
        _dbType.DropDownStyle = ComboBoxStyle.DropDownList;

        t.Controls.Add(MakeLabel("Type"), 0, 0); t.Controls.Add(_dbType, 1, 0);
        t.Controls.Add(MakeLabel("Host"), 2, 0); t.Controls.Add(_dbHost, 3, 0);
        t.Controls.Add(MakeLabel("Port"), 4, 0); t.Controls.Add(_dbPort, 5, 0);
        t.Controls.Add(MakeLabel("User"), 0, 1); t.Controls.Add(_dbUser, 1, 1);
        t.Controls.Add(MakeLabel("Password"), 2, 1); t.Controls.Add(_dbPassword, 3, 1);
        _dbType.Dock = DockStyle.Fill; _dbHost.Dock = DockStyle.Fill; _dbPort.Dock = DockStyle.Fill;
        _dbUser.Dock = DockStyle.Fill; _dbPassword.Dock = DockStyle.Fill; _dbPassword.UseSystemPasswordChar = true;

        t.Controls.Add(MakeLabel("Database"), 0, 2);
        t.Controls.Add(_dbDatabase, 1, 2);
        t.SetColumnSpan(_dbDatabase, 4);
        _dbDatabase.Dock = DockStyle.Fill;
        var browse = new Button { Text = "Browse...", Width = 90, Dock = DockStyle.Fill };
        browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog
            {
                Title = "เลือกไฟล์ฐานข้อมูล (Firebird .FDB หรือ SQL Server)",
                Filter = "Firebird DB (*.fdb)|*.fdb|All files (*.*)|*.*"
            };
            if (d.ShowDialog(this) == DialogResult.OK) _dbDatabase.Text = d.FileName;
        };
        t.Controls.Add(browse, 5, 2);

        g.Controls.Add(t);
        return g;
    }

    private Panel BuildActionsBar()
    {
        var p = new Panel { Dock = DockStyle.Fill, Height = 44 };
        _btnSave.Text = "บันทึกตั้งค่า"; _btnSave.Width = 110; _btnSave.Left = 0; _btnSave.Top = 8;
        _btnTest.Text = "ทดสอบเชื่อมต่อ"; _btnTest.Width = 110; _btnTest.Left = 118; _btnTest.Top = 8;
        _status.Text = "● หยุด"; _status.ForeColor = Color.FromArgb(214, 69, 69); _status.Font = new Font("Tahoma", 10F, FontStyle.Bold); _status.Left = 236; _status.Top = 14; _status.AutoSize = true;
        _btnStart.Text = "▶ เริ่ม Agent (Pos + App)"; _btnStart.Width = 190; _btnStart.Top = 8; _btnStart.Left = 420; _btnStart.BackColor = Color.FromArgb(22, 156, 99); _btnStart.ForeColor = Color.White;
        _btnStop.Text = "■ หยุด"; _btnStop.Width = 70; _btnStop.Top = 8; _btnStop.Left = 620; _btnStop.Enabled = false;
        p.Controls.AddRange(new Control[] { _btnSave, _btnTest, _status, _btnStart, _btnStop });
        return p;
    }

    private static Label MakeLabel(string text) => new() { Text = text, TextAlign = ContentAlignment.MiddleLeft, Width = 110, AutoSize = false, ForeColor = Color.FromArgb(102, 112, 133) };

    private void LoadConfig()
    {
        var c = _store.Load();
        _hubUrl.Text = c.HubBaseUrl;
        _username.Text = c.Username;
        _password.Text = c.Password;
        _agentCode.Text = c.AgentCode;
        _heartbeat.Text = c.HeartbeatSeconds.ToString();
        _autExe.Text = c.AutExe;
        _autExe2.Text = c.AutExe2;
        _autUser.Text = c.AutUser;
        _autPassword.Text = c.AutPassword;
        _dbType.SelectedItem = c.DbType == "SqlServer" ? "SqlServer" : "Firebird";
        _dbHost.Text = c.DbHost;
        _dbPort.Text = c.DbPort.ToString();
        _dbUser.Text = c.DbUser;
        _dbPassword.Text = c.DbPassword;
        _dbDatabase.Text = c.DbDatabase;
    }

    private AgentConfig CollectConfig()
    {
        _ = int.TryParse(_heartbeat.Text, out var heartbeat);
        _ = int.TryParse(_dbPort.Text, out var port);
        return new AgentConfig
        {
            HubBaseUrl = _hubUrl.Text.Trim().TrimEnd('/'),
            Username = _username.Text.Trim(),
            Password = _password.Text,
            AgentCode = string.IsNullOrWhiteSpace(_agentCode.Text) ? Environment.MachineName : _agentCode.Text.Trim(),
            HeartbeatSeconds = Math.Clamp(heartbeat, 5, 120),
            ActionTimeoutSeconds = 25,
            AutExe = _autExe.Text.Trim(),
            AutExe2 = _autExe2.Text.Trim(),
            AutUser = _autUser.Text.Trim(),
            AutPassword = _autPassword.Text,
            DbType = _dbType.SelectedItem?.ToString() ?? "Firebird",
            DbHost = _dbHost.Text.Trim(),
            DbPort = port <= 0 ? 3050 : port,
            DbUser = _dbUser.Text.Trim(),
            DbPassword = _dbPassword.Text,
            DbDatabase = _dbDatabase.Text.Trim(),
            RunnerExe = AgentConfigStore.FindRunnerExe() ?? "",
        };
    }

    private void SaveConfig()
    {
        _store.Save(CollectConfig());
        AppendLog($"[config] บันทึกตั้งค่าแล้ว → agent-config.json");
        MessageBox.Show("บันทึกตั้งค่าแล้ว", "ProMaxx2 Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task TestConnectionAsync()
    {
        _btnTest.Enabled = false;
        try
        {
            var c = CollectConfig();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await http.PostAsJsonAsync($"{c.HubBaseUrl}/auth/login", new { username = c.Username, password = c.Password });
            AppendLog(response.IsSuccessStatusCode
                ? $"[test] เชื่อมต่อ QA Hub สำเร็จ ({c.HubBaseUrl})"
                : $"[test] เชื่อมต่อล้มเหลว: HTTP {(int)response.StatusCode} — ตรวจ URL / Username / Password");
        }
        catch (Exception ex)
        {
            AppendLog($"[test] เชื่อมต่อไม่สำเร็จ: {ex.Message}");
        }
        finally
        {
            _btnTest.Enabled = true;
        }
    }

    private void StartAgentsAsync()
    {
        var config = CollectConfig();
        var started = 0;

        if (string.IsNullOrWhiteSpace(config.AutExe) && string.IsNullOrWhiteSpace(config.AutExe2))
        {
            MessageBox.Show("กรุณากำหนด AUT EXE 1 และ/หรือ AUT EXE 2 ก่อนเริ่ม Agent", "ProMaxx2 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.AutExe) && _process is not { HasExited: false })
        {
            var c1 = Clone(config);
            c1.AgentCode = string.IsNullOrWhiteSpace(config.AgentCode) ? Environment.MachineName : config.AgentCode.Trim();
            _process = LaunchAgent(c1, c1.AgentCode);
            if (_process is not null) started++;
        }

        if (!string.IsNullOrWhiteSpace(config.AutExe2) && _process2 is not { HasExited: false })
        {
            var c2 = Clone(config);
            c2.AutExe = config.AutExe2;
            var code = string.IsNullOrWhiteSpace(config.AgentCode) ? Environment.MachineName : config.AgentCode.Trim();
            c2.AgentCode = code.EndsWith("-APP", StringComparison.OrdinalIgnoreCase) ? code : code + "-APP";
            _process2 = LaunchAgent(c2, c2.AgentCode);
            if (_process2 is not null) started++;
        }

        if (started == 0) AppendLog("[gui] Agent ทั้ง 2 กำลังรันอยู่แล้ว (หรือไม่พบ Runner.exe)");
    }

    private static AgentConfig Clone(AgentConfig c) => new()
    {
        HubBaseUrl = c.HubBaseUrl,
        Username = c.Username,
        Password = c.Password,
        AgentCode = c.AgentCode,
        HeartbeatSeconds = c.HeartbeatSeconds,
        ActionTimeoutSeconds = c.ActionTimeoutSeconds,
        AutExe = c.AutExe,
        AutExe2 = c.AutExe2,
        AutUser = c.AutUser,
        AutPassword = c.AutPassword,
        DbType = c.DbType,
        DbHost = c.DbHost,
        DbPort = c.DbPort,
        DbUser = c.DbUser,
        DbPassword = c.DbPassword,
        DbDatabase = c.DbDatabase,
        RunnerExe = c.RunnerExe,
    };

    private Process? LaunchAgent(AgentConfig config, string label)
    {
        if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        {
            MessageBox.Show("กรุณากรอก Username และ Password ของ QA Hub", "ProMaxx2 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        var runner = config.RunnerExe;
        if (string.IsNullOrEmpty(runner) || !File.Exists(runner))
        {
            MessageBox.Show("ไม่พบ ProMaxx2.Automation.Runner.exe — ตรวจ path หรือ build ก่อน", "ProMaxx2 Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        _store.Save(config);

        var psi = new ProcessStartInfo
        {
            FileName = runner,
            WorkingDirectory = Path.GetDirectoryName(runner),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["QAHUB_BASE_URL"] = config.HubBaseUrl;
        psi.Environment["QAHUB_USERNAME"] = config.Username;
        psi.Environment["QAHUB_PASSWORD"] = config.Password;
        psi.Environment["AGENT_CODE"] = config.AgentCode;
        psi.Environment["HEARTBEAT_SECONDS"] = config.HeartbeatSeconds.ToString();
        psi.Environment["ACTION_TIMEOUT_SECONDS"] = config.ActionTimeoutSeconds.ToString();
        psi.Environment["AUT_EXE"] = config.AutExe;
        psi.Environment["AUT_USER"] = config.AutUser;
        psi.Environment["AUT_PASSWORD"] = config.AutPassword;
        psi.Environment["AUT_DB_TYPE"] = config.DbType;
        psi.Environment["AUT_DB_HOST"] = config.DbHost;
        psi.Environment["AUT_DB_PORT"] = config.DbPort.ToString();
        psi.Environment["AUT_DB_USER"] = config.DbUser;
        psi.Environment["AUT_DB_PASSWORD"] = config.DbPassword;
        psi.Environment["AUT_DB_DATABASE"] = config.DbDatabase;

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[{label}] {e.Data}"); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog($"[{label}][err] {e.Data}"); };
        proc.Exited += (_, _) =>
        {
            AppendLog($"[{label}] กระบวนการสิ้นสุด");
            BeginInvoke(() => UpdateButtons());
        };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        UpdateButtons();
        AppendLog($"[{label}] เริ่มทำงาน (Agent Code: {config.AgentCode}) → {runner}");
        return proc;
    }

    private void StopAgent()
    {
        if (_process is { HasExited: false }) { try { _process.Kill(entireProcessTree: true); AppendLog("[agent1] สั่งหยุดแล้ว"); } catch { } }
        if (_process2 is { HasExited: false }) { try { _process2.Kill(entireProcessTree: true); AppendLog("[agent2] สั่งหยุดแล้ว"); } catch { } }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var running1 = _process is { HasExited: false };
        var running2 = _process2 is { HasExited: false };
        _btnStart.Enabled = !(running1 && running2);
        _btnStop.Enabled = running1 || running2;
        if (running1 || running2)
        {
            _status.Text = "● กำลังรัน";
            _status.ForeColor = Color.FromArgb(22, 156, 99);
        }
        else
        {
            _status.Text = "● หยุด";
            _status.ForeColor = Color.FromArgb(214, 69, 69);
        }
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        try
        {
            BeginInvoke(() =>
            {
                lock (_logLock)
                {
                    _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
                    if (_log.TextLength > 200_000) _log.Clear();
                    _log.SelectionStart = _log.TextLength;
                    _log.ScrollToCaret();
                }
            });
        }
        catch { }
    }
}