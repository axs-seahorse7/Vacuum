using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VacuumPumpSystem.Services;

/// <summary>
/// Configures the radio channel of an LG206 LoRa module over its serial port.
///
/// Flow:
///   1. Enter configuration mode (+++ / a / +OK), or detect that the module
///      is already in configuration mode.
///   2. Read the current channel. If it already matches, just leave
///      configuration mode (no flash write, no reboot).
///   3. Otherwise set the channel, verify, save, reboot, then re-enter
///      configuration mode, verify again and leave configuration mode.
///
/// All serial I/O is polled (no DataReceived event), so it works on USB-serial
/// adapters whose drivers do not raise the event reliably.
/// All diagnostics go to %LocalAppData%\VacuumPumpSystem\modbus-rx.log.
/// </summary>
public sealed class LoRaConfigurationService
{
    private const int BaudRate = 115200;

    public async Task<bool> ConfigureChannelAsync(
        string portName,
        int channel,
        CancellationToken cancellationToken = default)
    {
        if (channel is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                "LG206 channel must be between 0 and 127.");
        }

        Log($"Configuring {portName} to CH{channel}");

        bool needsReboot;

        // ------------------------------------------------------------
        // Pass 1: enter config mode, check / set / save.
        // ------------------------------------------------------------
        using (var port = OpenPort(portName))
        {
            await EnterConfigModeAsync(port, cancellationToken);

            string current = await QueryChannelAsync(port, cancellationToken);

            if (ChannelMatches(current, channel))
            {
                Log($"{portName} is already on CH{channel}. No change needed.");

                await LeaveConfigModeAsync(port, cancellationToken);
                return true;
            }

            Log($"{portName} reports '{Clean(current)}'. Changing to CH{channel}.");

            string setResponse = await SendCommandAsync(
                port, $"AT+CH={channel}", 1500, cancellationToken);

            EnsureCommandSucceeded($"AT+CH={channel}", setResponse);

            // Verify before saving.
            string verify = await QueryChannelAsync(port, cancellationToken);
            EnsureChannelMatches(verify, channel);

            // Save configuration.
            string saveResponse = await SendCommandAsync(
                port, "AT+CFGTF", 2000, cancellationToken);

            if (!ContainsIgnoreCase(saveResponse, "SAVED") &&
                !ContainsIgnoreCase(saveResponse, "OK"))
            {
                throw new InvalidOperationException(
                    $"Module did not confirm saving the configuration: {Clean(saveResponse)}");
            }

            // Reboot. The module may drop the connection before it answers,
            // so a missing response is not an error.
            try
            {
                string reboot = await SendCommandAsync(
                    port, "AT+Z", 1000, cancellationToken);

                Log($"[AT+Z] => {Clean(reboot)}");
            }
            catch (Exception ex) when (
                ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
            {
                Log($"Reboot response unavailable: {ex.Message}");
            }

            needsReboot = true;
        }

        if (needsReboot)
        {
            // Allow the module to restart and its serial interface to come back.
            await Task.Delay(2500, cancellationToken);
        }

        // ------------------------------------------------------------
        // Pass 2: verify the saved channel after the reboot.
        // ------------------------------------------------------------
        using (var verifyPort = OpenPort(portName))
        {
            await EnterConfigModeAsync(verifyPort, cancellationToken);

            string after = await QueryChannelAsync(verifyPort, cancellationToken);
            EnsureChannelMatches(after, channel);

            Log($"PASS: {portName} verified on CH{channel} after reboot.");

            await LeaveConfigModeAsync(verifyPort, cancellationToken);
        }

        return true;
    }

    // ================================================================
    // PORT
    // ================================================================

    private static SerialPort OpenPort(string portName)
    {
        var port = new SerialPort(
            portName,
            BaudRate,
            Parity.None,
            8,
            StopBits.One)
        {
            ReadTimeout = 500,
            WriteTimeout = 2000,
            Encoding = Encoding.ASCII,
            DtrEnable = false,
            RtsEnable = false,
            NewLine = "\r\n"
        };

        try
        {
            port.Open();
            Thread.Sleep(200);
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            return port;
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    // ================================================================
    // CONFIGURATION MODE
    // ================================================================

    private static async Task EnterConfigModeAsync(
        SerialPort port,
        CancellationToken cancellationToken)
    {
        // A previous run may have left the module in configuration mode.
        // In that case "+++" gets no reply, so check for that first.
        port.DiscardInBuffer();

        string probe = await SendCommandAsync(port, "AT+CH", 700, cancellationToken);

        if (ContainsIgnoreCase(probe, "+CH"))
        {
            Log("Module is already in configuration mode.");
            return;
        }

        Log($"[Probe] => '{Clean(probe)}' (not in configuration mode yet)");

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Silence before "+++" so the module sees it as a standalone token.
            await Task.Delay(300, cancellationToken);
            port.DiscardInBuffer();

            string first = await SendAndWaitAsync(
                port,
                "+++",
                r => r.Contains('a'),
                TimeSpan.FromSeconds(2),
                cancellationToken);

            Log($"[Handshake {attempt}] +++ => '{Clean(first)}'");

            if (!first.Contains('a'))
                continue;

            string second = await SendAndWaitAsync(
                port,
                "a",
                r => ContainsIgnoreCase(r, "+OK"),
                TimeSpan.FromSeconds(2),
                cancellationToken);

            Log($"[Handshake {attempt}] a => '{Clean(second)}'");

            if (ContainsIgnoreCase(second, "+OK"))
            {
                Log("Configuration mode entered.");
                return;
            }
        }

        throw new InvalidOperationException(
            $"Cannot enter configuration mode on {port.PortName}. " +
            "Check that this is the LG206 receiver, that its UART is 115200 8N1, " +
            "that no other program is using the port, then power-cycle the module. " +
            "Details are in modbus-rx.log.");
    }

    private static async Task LeaveConfigModeAsync(
        SerialPort port,
        CancellationToken cancellationToken)
    {
        try
        {
            string response = await SendCommandAsync(
                port, "AT+ENTM", 800, cancellationToken);

            Log($"[AT+ENTM] => {Clean(response)}");
        }
        catch (Exception ex) when (
            ex is TimeoutException || ex is IOException || ex is InvalidOperationException)
        {
            Log($"Leaving configuration mode: {ex.Message}");
        }
    }

    private static Task<string> QueryChannelAsync(
        SerialPort port,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(port, "AT+CH", 1200, cancellationToken);
    }

    // ================================================================
    // SERIAL I/O (polled)
    // ================================================================

    /// <summary>Writes raw text (no line ending) and waits until the predicate matches or the timeout expires.</summary>
    private static async Task<string> SendAndWaitAsync(
        SerialPort port,
        string text,
        Func<string, bool> responseReceived,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = new StringBuilder();

        port.Write(text);

        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (port.IsOpen && port.BytesToRead > 0)
            {
                buffer.Append(port.ReadExisting());

                if (responseReceived(buffer.ToString()))
                    return buffer.ToString();
            }

            await Task.Delay(20, cancellationToken);
        }

        return buffer.ToString();
    }

    /// <summary>Sends an AT command (with CRLF) and returns as soon as OK/ERR arrives, or after the timeout.</summary>
    private static async Task<string> SendCommandAsync(
        SerialPort port,
        string command,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        port.DiscardInBuffer();
        Log($"TX: {command}");

        string response = await SendAndWaitAsync(
            port,
            command + "\r\n",
            r => ContainsIgnoreCase(r, "OK") || ContainsIgnoreCase(r, "ERR"),
            TimeSpan.FromMilliseconds(timeoutMilliseconds),
            cancellationToken);

        Log($"[{command}] => {Clean(response)}");

        return response;
    }

    // ================================================================
    // VALIDATION
    // ================================================================

    private static void EnsureCommandSucceeded(string command, string response)
    {
        if (ContainsIgnoreCase(response, "ERR") ||
            !ContainsIgnoreCase(response, "OK"))
        {
            throw new InvalidOperationException(
                $"Command failed: {command}. Response: {Clean(response)}");
        }
    }

    private static bool ChannelMatches(string response, int expectedChannel)
    {
        string normalized = response.Replace(" ", "");
        return ContainsIgnoreCase(normalized, $"+CH:{expectedChannel}");
    }

    private static void EnsureChannelMatches(string response, int expectedChannel)
    {
        if (!ChannelMatches(response, expectedChannel))
        {
            throw new InvalidOperationException(
                $"Channel verification failed. Expected +CH:{expectedChannel}; " +
                $"received: {Clean(response)}");
        }
    }

    private static bool ContainsIgnoreCase(string text, string value)
        => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    // ================================================================
    // LOGGING  (Console output is invisible in a WPF app)
    // ================================================================

    private static string Clean(string text)
        => text.Replace("\r", "\\r").Replace("\n", "\\n").Trim();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VacuumPumpSystem",
        "modbus-rx.log");

    private static readonly object LogLock = new();

    private static void Log(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [LoRa] {message}";

        Debug.WriteLine(line);

        try
        {
            lock (LogLock)
            {
                string? directory = Path.GetDirectoryName(LogPath);

                if (directory != null)
                    Directory.CreateDirectory(directory);

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never break configuration.
        }
    }
}