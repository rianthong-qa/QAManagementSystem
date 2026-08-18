using System.Diagnostics;
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
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(17,24,39), ForeColor = Color.Gainsboro, Font = new Font("Consolas",9), BorderStyle = BorderStyle.None };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private bool _refreshing;

    public MainForm()
    {
        _root = FindRepositoryRoot();
        Text = "ProMaxx2 QA - System Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820,560);
        Size = new Size(920,640);
        BackColor = Color.FromArgb(245,247,251);
        Font = new Font("Tahoma",9);

        var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Color.White, Padding = new Padding(22,13,22,10) };
        header.Controls.Add(new Label { Text = "ProMaxx2 QA System Manager", Font = new Font("Tahoma",17,FontStyle.Bold), AutoSize = true, Location = new Point(22,12), ForeColor = Color.FromArgb(31,41,55) });
        header.Controls.Add(new Label { Text = "จัดการ API และ Web โดยไม่ต้องเปิดหน้าเว็บหลัก", AutoSize = true, Location = new Point(24,45), ForeColor = Color.FromArgb(102,112,133) });

        var services = new TableLayoutPanel { Dock = DockStyle.Top, Height = 190, ColumnCount = 2, Padding = new Padding(16), BackColor = BackColor };
        services.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));services.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        services.Controls.Add(ServiceCard("API Service","Port 5038 · ASP.NET Core",_apiStatus,
            ("Start",async()=>await StartApi()),("Stop",async()=>await StopPort(5038,"API")),("Restart",async()=>{await StopPort(5038,"API");await StartApi();})),0,0);
        services.Controls.Add(ServiceCard("Web Application","Port 5173 · Vite",_webStatus,
            ("Start",async()=>await StartWeb()),("Stop",async()=>await StopPort(5173,"Web")),("Restart",async()=>{await StopPort(5173,"Web");await StartWeb();})),1,0);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(16,7,16,7), BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight };
        toolbar.Controls.Add(ActionButton("Start ทั้งหมด",async()=>{await StartApi();await StartWeb();},true));
        toolbar.Controls.Add(ActionButton("Restart ทั้งหมด",async()=>{await StopPort(5038,"API");await StopPort(5173,"Web");await StartApi();await StartWeb();},false));
        toolbar.Controls.Add(ActionButton("เปิดหน้าเว็บ",()=>{Process.Start(new ProcessStartInfo("http://127.0.0.1:5173"){UseShellExecute=true});return Task.CompletedTask;},false));
        toolbar.Controls.Add(ActionButton("ตรวจสถานะ",RefreshStatus,false));
        toolbar.Controls.Add(ActionButton("ล้าง Log",()=>{_log.Clear();return Task.CompletedTask;},false));

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = BackColor };
        var logCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
        var logTitle = new Label { Text = "Activity Log", Dock = DockStyle.Top, Height = 34, Padding = new Padding(12,8,0,0), Font = new Font("Tahoma",10,FontStyle.Bold), BackColor = Color.White };
        logCard.Controls.Add(_log);logCard.Controls.Add(logTitle);logPanel.Controls.Add(logCard);

        Controls.Add(logPanel);Controls.Add(toolbar);Controls.Add(services);Controls.Add(header);
        _timer.Tick += async (_,_) => await RefreshStatus();
        Shown += async (_,_) => { Log($"Workspace: {_root}");_timer.Start();await RefreshStatus(); };
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
        StartProcess("dotnet.exe",$"run --project \"{Path.Combine(_root,"src","ProMaxx2.QA.Api","ProMaxx2.QA.Api.csproj")}\" --launch-profile http",_root,"API");
        await WaitForPort(5038,"API");
    }

    private async Task StartWeb()
    {
        if(await PortOpen(5173)){Log("Web ทำงานอยู่แล้ว");return;}
        var web=Path.Combine(_root,"src","ProMaxx2.QA.Web");
        StartProcess("cmd.exe","/c npm.cmd run dev",web,"WEB");
        await WaitForPort(5173,"Web");
    }

    private void StartProcess(string file,string args,string workingDirectory,string source)
    {
        var info=new ProcessStartInfo(file,args){WorkingDirectory=workingDirectory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        var process=new Process{StartInfo=info,EnableRaisingEvents=true};
        process.OutputDataReceived+=(_,e)=>{if(e.Data is not null)Log($"[{source}] {e.Data}");};process.ErrorDataReceived+=(_,e)=>{if(e.Data is not null)Log($"[{source}] {e.Data}");};
        process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();Log($"เริ่ม {source} (PID {process.Id})");
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

    private async Task RefreshStatus()
    {
        if(_refreshing)return;_refreshing=true;
        try
        {
            var api=await IsHealthy("http://127.0.0.1:5038/health");var web=await IsHealthy("http://127.0.0.1:5173");
            SetStatus(_apiStatus,api);SetStatus(_webStatus,web);
        }
        finally{_refreshing=false;}
    }

    private async Task<bool> IsHealthy(string url){try{using var response=await _http.GetAsync(url);return response.IsSuccessStatusCode;}catch{return false;}}
    private static async Task<bool> PortOpen(int port){try{using var client=new TcpClient();await client.ConnectAsync("127.0.0.1",port).WaitAsync(TimeSpan.FromMilliseconds(500));return true;}catch{return false;}}
    private static void SetStatus(Label label,bool running){label.Text=running?"● Running":"● Stopped";label.BackColor=running?Color.FromArgb(234,248,241):Color.FromArgb(253,236,236);label.ForeColor=running?Color.FromArgb(22,139,88):Color.FromArgb(200,58,58);}

    private static int? FindListeningPid(int port)
    {
        using var netstat=Process.Start(new ProcessStartInfo("netstat.exe","-ano -p TCP"){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});if(netstat is null)return null;
        var output=netstat.StandardOutput.ReadToEnd();netstat.WaitForExit();
        foreach(var line in output.Split('\n')){var parts=line.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length>=5&&parts[0].Equals("TCP",StringComparison.OrdinalIgnoreCase)&&parts[1].EndsWith($":{port}")&&parts[3].Equals("LISTENING",StringComparison.OrdinalIgnoreCase)&&int.TryParse(parts[4],out var pid))return pid;}
        return null;
    }

    private void Log(string message)
    {
        if(InvokeRequired){BeginInvoke(()=>Log(message));return;}
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");_log.SelectionStart=_log.TextLength;_log.ScrollToCaret();
    }

    private static string FindRepositoryRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null){if(File.Exists(Path.Combine(directory.FullName,"ProMaxx2.QA.slnx")))return directory.FullName;directory=directory.Parent;}
        throw new DirectoryNotFoundException("ไม่พบ ProMaxx2.QA.slnx กรุณาวางโปรแกรมไว้ภายใน Workspace ของระบบ");
    }
}
