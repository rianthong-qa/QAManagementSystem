using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace ProMaxx2.ServiceManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly string _root;
    private readonly Label _apiStatus = StatusLabel();
    private readonly Label _webStatus = StatusLabel();
    private readonly Label _scheduleStatus = StatusLabel();
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(17,24,39), ForeColor = Color.Gainsboro, Font = new Font("Consolas",9), BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _logTimer = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _resourceTimer = new() { Interval = 1000 };
    private readonly ResourceChart _resourceChart = new() { Dock = DockStyle.Fill };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly ConcurrentQueue<string> _pendingLogs = new();
    private readonly Dictionary<int,(TimeSpan cpu,DateTime sampledAt)> _processSamples = new();
    private const int MaxQueuedLogLines = 5000;
    private const int MaxLogCharacters = 200_000;
    private int _queuedLogLines;
    private int? _apiPid;
    private int? _webPid;
    private bool _refreshing;

    public MainForm()
    {
        _root = FindRepositoryRoot();
        Text = "ProMaxx2 QA - System Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820,640);
        Size = new Size(920,640);
        BackColor = Color.FromArgb(245,247,251);
        Font = new Font("Tahoma",9);

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White, Padding = new Padding(22,13,22,10) };
        header.Controls.Add(new Label { Text = "ProMaxx2 QA System Manager", Font = new Font("Tahoma",17,FontStyle.Bold), AutoSize = true, Location = new Point(22,12), ForeColor = Color.FromArgb(31,41,55) });
        header.Controls.Add(new Label { Text = "จัดการ API และ Web โดยไม่ต้องเปิดหน้าเว็บหลัก", AutoSize = true, Location = new Point(24,45), ForeColor = Color.FromArgb(102,112,133) });

        var services = new TableLayoutPanel { Dock = DockStyle.Top, Height = 320, ColumnCount = 2, RowCount = 2, Padding = new Padding(16), BackColor = BackColor };
        services.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));services.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        services.RowStyles.Add(new RowStyle(SizeType.Percent,50));services.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        services.Controls.Add(ServiceCard("API Service","Port 5038 · ASP.NET Core",_apiStatus,
            ("Start",async()=>await StartApi()),("Stop",async()=>await StopPort(5038,"API")),("Restart",async()=>{await StopPort(5038,"API");await StartApi();})),0,0);
        services.Controls.Add(ServiceCard("Web Application","Port 5173 · Vite",_webStatus,
            ("Start",async()=>await StartWeb()),("Stop",async()=>await StopPort(5173,"Web")),("Restart",async()=>{await StopPort(5173,"Web");await StartWeb();})),1,0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(16,7,16,7), BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight };
        toolbar.Controls.Add(ActionButton("Start ทั้งหมด",async()=>{await StartApi();await StartWeb();},true));
        toolbar.Controls.Add(ActionButton("Restart ทั้งหมด",async()=>{await StopPort(5038,"API");await StopPort(5173,"Web");await StartApi();await StartWeb();},false));
        toolbar.Controls.Add(ActionButton("เปิดหน้าเว็บ",()=>{Process.Start(new ProcessStartInfo("http://127.0.0.1:5173"){UseShellExecute=true});return Task.CompletedTask;},false));
        toolbar.Controls.Add(ActionButton("ตรวจสถานะ",RefreshStatus,false));
        toolbar.Controls.Add(ActionButton("ล้าง Log",ClearLog,false));
        toolbar.Controls.Add(ActionButton("Start Schedule",async()=>await SetScheduleWorker(true),false));
        toolbar.Controls.Add(ActionButton("Stop Schedule",async()=>await SetScheduleWorker(false),false));

        services.Controls.Add(ServiceCard("Automation Schedule Worker", "API background worker - Poll 30s", _scheduleStatus,
            ("Start", async () => await SetScheduleWorker(true)),
            ("Stop", async () => await SetScheduleWorker(false)),
            ("Refresh", RefreshStatus)), 0, 1);
        var resourceCard=new Panel{Dock=DockStyle.Fill,Margin=new Padding(6),Padding=new Padding(8),BackColor=Color.White};
        resourceCard.Controls.Add(_resourceChart);
        services.Controls.Add(resourceCard,1,1);

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = BackColor };
        var logCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        var logTitle = new Label { Text = "Activity Log", Dock = DockStyle.Top, Height = 34, Padding = new Padding(12,8,0,0), Font = new Font("Tahoma",10,FontStyle.Bold), BackColor = Color.White };
        logCard.Controls.Add(_log);logCard.Controls.Add(logTitle);logPanel.Controls.Add(logCard);

        Controls.Add(logPanel);Controls.Add(toolbar);Controls.Add(services);Controls.Add(header);
        _timer.Tick += async (_,_) => await RefreshStatus();
        _logTimer.Tick += (_,_) => DrainLogs();
        _resourceTimer.Tick += (_,_) => SampleResources();
        Shown += async (_,_) => { Log($"Workspace: {_root}");_logTimer.Start();_resourceTimer.Start();_timer.Start();await RefreshStatus(); };
        FormClosed += (_,_) => { _timer.Stop();_logTimer.Stop();_resourceTimer.Stop();_timer.Dispose();_logTimer.Dispose();_resourceTimer.Dispose();_http.Dispose(); };
    }

    private static Panel ServiceCard(string title,string subtitle,Label status,params (string text,Func<Task> action)[] actions)
    {
        var card=new Panel{Dock=DockStyle.Fill,Margin=new Padding(6),Padding=new Padding(16),BackColor=Color.White};
        card.Controls.Add(new Label{Text=title,Font=new Font("Tahoma",12,FontStyle.Bold),AutoSize=true,Location=new Point(16,15)});
        card.Controls.Add(new Label{Text=subtitle,ForeColor=Color.FromArgb(102,112,133),AutoSize=true,Location=new Point(17,43)});
        status.Location=new Point(17,72);card.Controls.Add(status);
        var buttons=new FlowLayoutPanel{Location=new Point(12,102),AutoSize=true};
        foreach(var item in actions)buttons.Controls.Add(ActionButton(item.text,item.action,item.text=="Start"));
        card.Controls.Add(buttons);return card;
    }

    private static Label StatusLabel()=>new(){Text="กำลังตรวจสอบ...",AutoSize=true,Padding=new Padding(9,5,9,5),BackColor=Color.FromArgb(234,240,255),ForeColor=Color.FromArgb(36,87,214),Font=new Font("Tahoma",9,FontStyle.Bold)};
    private static Button ActionButton(string text,Func<Task> action,bool primary)
    {
        var button=new Button{Text=text,AutoSize=true,Height=33,FlatStyle=FlatStyle.Flat,Margin=new Padding(4),Cursor=Cursors.Hand,BackColor=primary?Color.FromArgb(36,87,214):Color.White,ForeColor=primary?Color.White:Color.FromArgb(31,41,55)};
        button.FlatAppearance.BorderColor=primary?Color.FromArgb(36,87,214):Color.FromArgb(229,231,235);
        button.Click+=async(_,_)=>{button.Enabled=false;try{await action();}catch(Exception ex){MessageBox.Show(ex.Message,"ดำเนินการไม่สำเร็จ",MessageBoxButtons.OK,MessageBoxIcon.Error);}finally{button.Enabled=true;}};
        return button;
    }

    private async Task StartApi()
    {
        if(await PortOpen(5038)){Log("API ทำงานอยู่แล้ว");return;}
        // API สาธารณะต้องรันแบบ Production ด้วย JWT key จาก User environment variable (อ่านใหม่จาก registry ทุกครั้ง
        // เพราะ process ของ Service Manager ที่เปิดค้างไว้จะไม่เห็นตัวแปรที่ตั้งหลังจากเปิดโปรแกรม) — ถ้ายังไม่ได้ตั้ง
        // Jwt__Key จะ fallback เป็น Development profile เดิมเพื่อไม่ให้ API สตาร์ทไม่ขึ้น
        var jwtKey=Environment.GetEnvironmentVariable("Jwt__Key",EnvironmentVariableTarget.User);
        var profile=string.IsNullOrWhiteSpace(jwtKey)?"http":"production";
        if(profile=="http")Log("ไม่พบ User environment variable Jwt__Key — เริ่ม API แบบ Development");
        StartProcess("dotnet.exe",$"run --project \"{Path.Combine(_root,"src","ProMaxx2.QA.Api","ProMaxx2.QA.Api.csproj")}\" --launch-profile {profile}",_root,"API",string.IsNullOrWhiteSpace(jwtKey)?null:new Dictionary<string,string>{["Jwt__Key"]=jwtKey});
        await WaitForPort(5038,"API");
    }

    private async Task StartWeb()
    {
        if(await PortOpen(5173)){Log("Web ทำงานอยู่แล้ว");return;}
        var web=Path.Combine(_root,"src","ProMaxx2.QA.Web");
        StartProcess("cmd.exe","/c npm.cmd run dev",web,"WEB");
        await WaitForPort(5173,"Web");
    }

    private void StartProcess(string file,string args,string workingDirectory,string source,IReadOnlyDictionary<string,string>? environment=null)
    {
        var info=new ProcessStartInfo(file,args){WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        if(environment is not null)foreach(var (name,value) in environment)info.Environment[name]=value;
        var process=new Process{StartInfo=info,EnableRaisingEvents=true};
        process.OutputDataReceived+=(_,e)=>{if(e.Data is not null)Log($"[{source}] {e.Data}");};process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)Log($"[{source}] {e.Data}");};
        process.Start();var pid=process.Id;process.Exited+=(_,_)=>process.Dispose();process.BeginOutputReadLine();process.BeginErrorReadLine();Log($"เริ่ม {source} (PID {pid})");
    }

    private async Task StopPort(int port,string source)
    {
        var pid=FindListeningPid(port);if(pid is null){Log($"{source} หยุดอยู่แล้ว");return;}
        var process=Process.GetProcessById(pid.Value);Log($"หยุด {source} (PID {pid})");process.Kill(true);await process.WaitForExitAsync();await RefreshStatus();
    }

    private async Task WaitForPort(int port,string source)
    {
        for(var i=0;i<40;i++){if(await PortOpen(port)){Log($"{source} พร้อมใช้งานที่ Port {port}");await RefreshStatus();return;}await Task.Delay(250);}
        throw new InvalidOperationException($"{source} ไม่พร้อมใช้งานภายในเวลาที่กำหนด กรุณาตรวจ Activity Log");
    }

    private async Task RefreshScheduleWorkerStatus()
    {
        try
        {
            using var response = await _http.GetAsync("http://127.0.0.1:5038/api/v1/automation/schedules/worker-status");
            var json = await response.Content.ReadAsStringAsync();
            var enabled = json.Contains("\"enabled\":true", StringComparison.OrdinalIgnoreCase);
            _scheduleStatus.Text = enabled ? "● Worker Enabled" : "● Worker Disabled";
        }
        catch { _scheduleStatus.Text = "● Unknown"; }
    }

    private async Task SetScheduleWorker(bool enabled)
    {
        using var response = await _http.PostAsync($"http://127.0.0.1:5038/api/v1/automation/schedules/worker-status?enabled={enabled.ToString().ToLowerInvariant()}", null);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("เปลี่ยนสถานะ Schedule Worker ไม่สำเร็จ");
        Log(enabled ? "เปิด Automation Schedule Worker" : "ปิด Automation Schedule Worker");
        await RefreshScheduleWorkerStatus();
    }

    private async Task RefreshStatus()
    {
        if(_refreshing)return;_refreshing=true;
        try
        {
            var api=await IsHealthy("http://127.0.0.1:5038/health");var web=await IsHealthy("http://127.0.0.1:5173");
            if (api) await RefreshScheduleWorkerStatus();
            _apiPid=api?FindListeningPid(5038):null;_webPid=web?FindListeningPid(5173):null;
            SetStatus(_apiStatus,api);SetStatus(_webStatus,web);
        }
        finally{_refreshing=false;}
    }

    private async Task<bool> IsHealthy(string url){try{using var response=await _http.GetAsync(url);return response.IsSuccessStatusCode;}catch{return false;}}
    private static async Task<bool> PortOpen(int port){try{using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",port).WaitAsync(TimeSpan.FromMilliseconds(500));return true;}catch{return false;}}
    private static void SetStatus(Label label,bool running){label.Text=running?"● Running":"● Stopped";label.BackColor=running?Color.FromArgb(234,248,241):Color.FromArgb(253,236,236);label.ForeColor=running?Color.FromArgb(22,139,88):Color.FromArgb(200,58,58);}

    private void SampleResources()
    {
        var now=DateTime.UtcNow;double cpuPercent=0;long workingSet=0;
        var activePids=new HashSet<int>();
        foreach(var pid in new[]{_apiPid,_webPid}.Where(x=>x.HasValue).Select(x=>x!.Value).Distinct())
        {
            try
            {
                using var process=Process.GetProcessById(pid);process.Refresh();activePids.Add(pid);workingSet+=process.WorkingSet64;
                var cpu=process.TotalProcessorTime;
                if(_processSamples.TryGetValue(pid,out var previous))
                {
                    var elapsed=(now-previous.sampledAt).TotalMilliseconds;
                    if(elapsed>0)cpuPercent+=(cpu-previous.cpu).TotalMilliseconds/(elapsed*Environment.ProcessorCount)*100;
                }
                _processSamples[pid]=(cpu,now);
            }
            catch{_processSamples.Remove(pid);}
        }
        foreach(var stale in _processSamples.Keys.Where(pid=>!activePids.Contains(pid)).ToArray())_processSamples.Remove(stale);
        _resourceChart.AddSample((float)Math.Clamp(cpuPercent,0,100),workingSet/(1024f*1024f));
    }

    private static int? FindListeningPid(int port)
    {
        using var netstat=Process.Start(new ProcessStartInfo("netstat.exe","-ano -p TCP"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});if(netstat is null)return null;
        var output=netstat.StandardOutput.ReadToEnd();netstat.WaitForExit();
        foreach(var line in output.Split('\n')){var parts=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length>=5&&parts[0].Equals("TCP",StringComparison.OrdinalIgnoreCase)&&parts[1].EndsWith($":{port}")&&parts[3].Equals("LISTENING",StringComparison.OrdinalIgnoreCase)&&int.TryParse(parts[4],out var pid))return pid;}
        return null;
    }

    private void Log(string message)
    {
        _pendingLogs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        var queued=Interlocked.Increment(ref _queuedLogLines);
        while(queued>MaxQueuedLogLines&&_pendingLogs.TryDequeue(out _))queued=Interlocked.Decrement(ref _queuedLogLines);
    }

    private void DrainLogs()
    {
        if(IsDisposed||_pendingLogs.IsEmpty)return;
        var keepAtBottom=_log.SelectionStart>=_log.TextLength-2;
        var batch=new StringBuilder();
        for(var i=0;i<250&&_pendingLogs.TryDequeue(out var line);i++){Interlocked.Decrement(ref _queuedLogLines);batch.Append(line);}
        if(batch.Length==0)return;
        _log.AppendText(batch.ToString());
        if(_log.TextLength>MaxLogCharacters){_log.Select(0,_log.TextLength-MaxLogCharacters+20_000);_log.SelectedText="";}
        if(keepAtBottom){_log.SelectionStart=_log.TextLength;_log.ScrollToCaret();}
    }

    private Task ClearLog()
    {
        while(_pendingLogs.TryDequeue(out _))Interlocked.Decrement(ref _queuedLogLines);
        _log.Clear();
        return Task.CompletedTask;
    }

    private static string FindRepositoryRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null){if(File.Exists(Path.Combine(directory.FullName,"ProMaxx2.QA.slnx")))return directory.FullName;directory=directory.Parent;}
        throw new DirectoryNotFoundException("ไม่พบ ProMaxx2.QA.slnx กรุณาวางโปรแกรมไว้ภายใน Workspace ของระบบ");
    }
}

internal sealed class ResourceChart : Control
{
    private const int Capacity = 120;
    private readonly Queue<float> _cpu = new();
    private readonly Queue<float> _ram = new();
    private float _currentCpu;
    private float _currentRam;

    public ResourceChart()
    {
        DoubleBuffered=true;
        ResizeRedraw=true;
        BackColor=Color.White;
        ForeColor=Color.FromArgb(31,41,55);
    }

    public void AddSample(float cpu,float ram)
    {
        _currentCpu=cpu;_currentRam=ram;
        _cpu.Enqueue(cpu);_ram.Enqueue(ram);
        while(_cpu.Count>Capacity)_cpu.Dequeue();
        while(_ram.Count>Capacity)_ram.Dequeue();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var titleFont=new Font("Tahoma",11,FontStyle.Bold);
        using var valueFont=new Font("Tahoma",8,FontStyle.Bold);
        using var smallFont=new Font("Tahoma",7);
        g.DrawString("Service Resources",titleFont,Brushes.Black,8,4);
        using var cpuBrush=new SolidBrush(Color.FromArgb(36,87,214));
        using var ramBrush=new SolidBrush(Color.FromArgb(5,150,105));
        g.DrawString($"CPU {_currentCpu:0.0}%",valueFont,cpuBrush,8,25);
        g.DrawString($"RAM {_currentRam:0} MB",valueFont,ramBrush,88,25);
        var chart=new RectangleF(8,44,Math.Max(1,Width-16),Math.Max(1,Height-52));
        using var background=new SolidBrush(Color.FromArgb(248,250,252));g.FillRectangle(background,chart);
        using var gridPen=new Pen(Color.FromArgb(230,234,241),1);
        for(var i=1;i<4;i++){var y=chart.Top+chart.Height*i/4;g.DrawLine(gridPen,chart.Left,y,chart.Right,y);}
        var ramScale=Math.Max(256f,(float)Math.Ceiling(Math.Max(_currentRam,_ram.DefaultIfEmpty().Max())/256f)*256f);
        DrawSeries(g,_cpu.ToArray(),chart,100f,Color.FromArgb(36,87,214));
        DrawSeries(g,_ram.ToArray(),chart,ramScale,Color.FromArgb(5,150,105));
        using var scaleBrush=new SolidBrush(Color.FromArgb(102,112,133));
        g.DrawString("ย้อนหลัง 2 นาที · API + Web",smallFont,scaleBrush,chart.Left+3,chart.Bottom-13);
    }

    private static void DrawSeries(Graphics g,float[] values,RectangleF area,float scale,Color color)
    {
        if(values.Length<2)return;
        var points=new PointF[values.Length];
        for(var i=0;i<values.Length;i++)
        {
            var x=area.Right-area.Width*(values.Length-1-i)/(Capacity-1f);
            var y=area.Bottom-Math.Clamp(values[i]/scale,0,1)*area.Height;
            points[i]=new PointF(x,y);
        }
        using var pen=new Pen(color,2);pen.LineJoin=System.Drawing.Drawing2D.LineJoin.Round;g.DrawLines(pen,points);
    }
}
