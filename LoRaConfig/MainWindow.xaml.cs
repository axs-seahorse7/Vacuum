using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace LoRaConfig;

public partial class MainWindow : Window
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VacuumPumpSystem", "LoRaConfig");
    private string LogPath => Path.Combine(root, "lora-config.log");
    private bool busy;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(root);
        RefreshPorts();
        Log("LoRaConfig started. NID is preserved; factory reset is never used.");
    }

    private void RefreshPorts()
    {
        string old = PortCombo.Text;
        PortCombo.Items.Clear();
        foreach (string p in SerialPort.GetPortNames().OrderBy(x => x)) PortCombo.Items.Add(p);
        if (!string.IsNullOrWhiteSpace(old)) PortCombo.Text = old;
        else if (PortCombo.Items.Count > 0) PortCombo.SelectedIndex = 0;
        Log("Available ports: " + string.Join(", ", PortCombo.Items.Cast<string>()));
    }

    private string PortName()
    {
        string p = PortCombo.Text.Trim().ToUpperInvariant();
        if (!p.StartsWith("COM")) throw new InvalidOperationException("Select a valid COM port.");
        return p;
    }
    private int Channel() => int.Parse(((ComboBoxItem)ChannelCombo.SelectedItem).Content.ToString()!.Replace("CH", ""));
    private string LineName() => ((ComboBoxItem)LineCombo.SelectedItem).Content.ToString()!;
    private string RoleName() => ((ComboBoxItem)RoleCombo.SelectedItem).Content.ToString()!;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private async void ReadButton_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { SetBusy(true); await Task.Run(() => ReadCurrent(PortName())); StatusText.Text = "Read complete — see log."; }
        catch (Exception ex) { Log("ERROR: " + ex); StatusText.Text = "Read failed"; MessageBox.Show(ex.Message); }
        finally { SetBusy(false); }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        string port = PortName(); int ch = Channel(); string line = LineName(); string role = RoleName();
        if (MessageBox.Show($"Configure module on {port}?\nLine: {line}\nRole: {role}\nChannel: CH{ch}\n\nThis changes radio settings, saves and reboots the module. Existing NID is preserved. Confirm the channel is approved and unused.", "Confirm configuration", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { SetBusy(true); await Task.Run(() => Apply(port, ch, line, role)); StatusText.Text = "PASS — saved and verified"; MessageBox.Show("Required settings verified after reboot. Review the log."); }
        catch (Exception ex) { Log("ERROR: " + ex); StatusText.Text = "FAILED — inspect log"; MessageBox.Show(ex.Message, "LoRaConfig failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
    }

    private SerialPort Open(string name)
    {
        var p = new SerialPort(name, 115200, Parity.None, 8, StopBits.One) { ReadTimeout = 500, WriteTimeout = 500, Encoding = Encoding.ASCII, DtrEnable = false, RtsEnable = false };
        p.Open(); Thread.Sleep(200); p.DiscardInBuffer(); return p;
    }

    private void ReadCurrent(string name)
    {
        using var p = Open(name); Log($"Opened {name}; querying current settings."); EnterConfig(p);
        foreach (string cmd in new[] { "AT+VER", "AT+WMODE", "AT+NID", "AT+RSNUM", "AT+PMODE", "AT+WTM", "AT+UART", "AT+SPD", "AT+CH", "AT+ADDR", "AT+FEC", "AT+PWR" })
            LogResponse(cmd, Command(p, cmd, 900));
        ExitConfig(p); Log("Read completed.");
    }

    private void Apply(string name, int ch, string line, string role)
    {
        Log($"===== APPLY: Port={name}; Line={line}; Role={role}; Channel=CH{ch} =====");
        using var p = Open(name); EnterConfig(p);
        var before = new Dictionary<string,string>();
        foreach (string cmd in new[] { "AT+VER", "AT+WMODE", "AT+NID", "AT+UART", "AT+SPD", "AT+CH", "AT+ADDR", "AT+FEC", "AT+PWR", "AT+RSNUM", "AT+PMODE" })
        {
            string r = Command(p, cmd, 900); before[cmd] = r; LogResponse(cmd, r);
        }
        string backup = Path.Combine(root, $"backup-{Safe(line)}-{Safe(role)}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(backup, before.Select(x => $"{x.Key}\r\n{x.Value}"));
        Log("Backup saved: " + backup);

        string[] changes = { "AT+WMODE=TRANS", "AT+RSNUM=0", "AT+PMODE=RUN", "AT+UART=115200,8,1,NONE,NFC", "AT+SPD=8", $"AT+CH={ch}", "AT+FEC=OFF", "AT+PWR=20", "AT+ADDR=888" };
        foreach (string cmd in changes)
        {
            string r = Command(p, cmd, 1400); LogResponse(cmd, r);
            if (!r.Contains("OK", StringComparison.OrdinalIgnoreCase) || r.Contains("ERR", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Command failed: {cmd}\nResponse: {r}\nInspect the log; remaining commands were not sent.");
        }
        string save = Command(p, "AT+CFGTF", 1600); LogResponse("AT+CFGTF", save);
        if (!save.Contains("SAVED", StringComparison.OrdinalIgnoreCase) && !save.Contains("OK", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module did not confirm saving settings.");
        Log("Rebooting module using AT+Z.");
        LogResponse("AT+Z", Command(p, "AT+Z", 900));
        p.Close(); Thread.Sleep(2500);
        using var verify = Open(name); EnterConfig(verify);
        var expected = new Dictionary<string,string> {
            ["AT+WMODE"]="+WMODE:TRANS", ["AT+UART"]="+UART:115200,8,1,NONE,NFC", ["AT+SPD"]="+SPD:8",
            ["AT+CH"]=$"+CH:{ch}", ["AT+ADDR"]="+ADDR:888", ["AT+FEC"]="+FEC:OFF", ["AT+PWR"]="+PWR:20",
            ["AT+RSNUM"]="+RSNUM:0", ["AT+PMODE"]="+PMODE:RUN"
        };
        bool pass = true;
        foreach (var item in expected)
        {
            string r = Command(verify, item.Key, 1000); LogResponse(item.Key + " VERIFY", r);
            bool ok = r.Contains(item.Value, StringComparison.OrdinalIgnoreCase);
            Log($"{(ok ? "PASS" : "FAIL")} {item.Key} expected {item.Value}"); pass &= ok;
        }
        LogResponse("AT+NID PRESERVED", Command(verify, "AT+NID", 1000));
        ExitConfig(verify);
        Log(pass ? "FINAL RESULT: PASS" : "FINAL RESULT: FAIL");
        if (!pass) throw new InvalidOperationException("Post-reboot verification failed. See log.");
    }

    private void EnterConfig(SerialPort p)
    {
        for (int attempt=1; attempt<=3; attempt++)
        {
            p.DiscardInBuffer(); p.Write("+++"); string a = ReadFor(p, 450);
            Log($"Handshake {attempt}: [{Clean(a)}]");
            if (!a.Contains('a')) { Thread.Sleep(500); continue; }
            p.Write("a"); string ok = ReadFor(p, 700); Log("Handshake confirm: ["+Clean(ok)+"]");
            if (ok.Contains("+OK", StringComparison.OrdinalIgnoreCase)) { Log("Configuration mode entered."); return; }
            Thread.Sleep(500);
        }
        throw new InvalidOperationException($"Cannot enter config mode on {p.PortName}. Close other apps using this COM port and retry.");
    }
    private string Command(SerialPort p, string cmd, int ms) { p.DiscardInBuffer(); p.Write(cmd+"\r\n"); return ReadFor(p, ms); }
    private string ReadFor(SerialPort p, int ms)
    {
        var b = new StringBuilder(); var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { if (p.BytesToRead > 0) b.Append(p.ReadExisting()); Thread.Sleep(10); }
        return b.ToString();
    }
    private void ExitConfig(SerialPort p) { try { LogResponse("AT+ENTM", Command(p, "AT+ENTM", 700)); } catch (Exception ex) { Log("Exit config note: "+ex.Message); } }
    private void LogResponse(string c, string r) => Log($"[{c}] => {Clean(r)}");
    private static string Clean(string s) => s.Replace("\r","\\r").Replace("\n","\\n").Trim();
    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
    private void Log(string s)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {s}";
        Dispatcher.Invoke(() => { LogBox.AppendText(line+Environment.NewLine); LogBox.ScrollToEnd(); });
        try { Directory.CreateDirectory(root); File.AppendAllText(LogPath, line+Environment.NewLine); } catch { }
    }
    private void SetBusy(bool b) { busy=b; RefreshButton.IsEnabled=ReadButton.IsEnabled=ApplyButton.IsEnabled=!b; PortCombo.IsEnabled=LineCombo.IsEnabled=RoleCombo.IsEnabled=ChannelCombo.IsEnabled=!b; StatusText.Text=b?"Working...":"Ready"; }
    private void OpenLogFolder_Click(object sender, RoutedEventArgs e) { Directory.CreateDirectory(root); Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute=true }); }
}
