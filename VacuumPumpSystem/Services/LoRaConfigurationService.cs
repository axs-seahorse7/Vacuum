
using System.Diagnostics;
using System.IO.Ports;
using System.Text;

namespace VacuumPumpSystem.Services;

public sealed class LoRaConfigurationService
{
    public async Task<bool> ConfigureChannelAsync(
        string portName,
        int channel,
        CancellationToken cancellationToken = default)
    {
        if (channel is < 0 or > 127)
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                "LG206 channel must be between 0 and 127.");

        using var port = new SerialPort(
            portName, 115200, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = 200,
            WriteTimeout = 2000,
            DtrEnable = false,
            RtsEnable = false,
            NewLine = "\r\n"
        };

        port.Open();

        try
        {
           port.DiscardInBuffer();
port.DiscardOutBuffer();

Console.WriteLine("[LoRa] Sending +++");
port.Write("+++");

// Wait for the module's first handshake character.
string firstResponse = await ReadUntilAsync(
    port, "a", TimeSpan.FromSeconds(3), cancellationToken);

Console.WriteLine($"[LoRa] First response: {firstResponse}");

if (!firstResponse.Trim().EndsWith("a", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Expected 'a' after +++. Received: {firstResponse}");
}

// Send the second handshake character immediately.
Console.WriteLine("[LoRa] Sending a");
port.Write("a");

string secondResponse = await ReadUntilAsync(
    port, "+OK", TimeSpan.FromSeconds(3), cancellationToken);

Console.WriteLine($"[LoRa] Second response: {secondResponse}");

if (!secondResponse.Contains("+OK", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        $"Expected +OK after second 'a'. Received: {secondResponse}");
}
            // Step 3: Set and verify the channel.
            string setResponse = await SendCommandAsync(port, $"AT+CH={channel}", cancellationToken);

            Console.WriteLine($"[LoRa] Set channel response: {setResponse}");

            if (!setResponse.Contains("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"LG206 rejected channel setting: {setResponse}");

            string queryResponse = await SendCommandAsync(port, "AT+CH", cancellationToken);

            Console.WriteLine($"[LoRa] Query channel response: {queryResponse}");

            string expected = $"+CH:{channel}";

            if (!queryResponse.Replace(" ", "").Contains(
                    expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Channel verification failed. Expected {expected}; received: {queryResponse}");

            // Save the module configuration.
            string saveResponse = await SendCommandAsync(
                port, "AT+CFGTF", cancellationToken);

            Console.WriteLine($"[LoRa] Save response: {saveResponse}");

            if (!saveResponse.Contains("OK", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Could not save LG206 configuration: {saveResponse}");

            return true;
        }
        finally
        {
            if (port.IsOpen)
                port.Close();
        }
    }

    private static async Task<string> SendCommandAsync(SerialPort port, string command, CancellationToken cancellationToken)
    {
        port.DiscardInBuffer();
        Console.WriteLine($"[LoRa] TX: {command}");

        port.Write(command + "\r\n");

        string response = await ReadUntilAsync(port, "OK", TimeSpan.FromSeconds(3), cancellationToken);

        if (string.IsNullOrWhiteSpace(response))
            throw new TimeoutException($"No response received for {command}.");

        return response;
    }

    private static async Task<string> ReadAvailableAsync(SerialPort port, TimeSpan duration, CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string chunk = port.ReadExisting();

            if (!string.IsNullOrEmpty(chunk))
                result.Append(chunk);

            await Task.Delay(20, cancellationToken);
        }

        return result.ToString();
    }

    private static async Task<string> ReadUntilAsync(
        SerialPort port,
        string terminator,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string chunk = port.ReadExisting();

            if (!string.IsNullOrEmpty(chunk))
            {
                result.Append(chunk);
                string text = result.ToString();

                if (text.Contains(terminator, StringComparison.OrdinalIgnoreCase))
                    return text;
            }

            await Task.Delay(20, cancellationToken);
        }

        return result.ToString();
    }
}
