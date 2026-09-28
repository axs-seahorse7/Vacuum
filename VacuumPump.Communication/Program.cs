using System.IO.Ports;
using System.Text;

using var port = new SerialPort("COM6", 115200, Parity.None, 8, StopBits.One);

var buffer = new StringBuilder();

port.DataReceived += (s, e) =>
{
    buffer.Append(port.ReadExisting());

    while (true)
    {
        string current = buffer.ToString();

        int start = current.IndexOf('<');
        int end = current.IndexOf('>', start + 1);

        if (start < 0 || end < 0)
            break;

        string packet = current.Substring(start, end - start + 1);

        buffer.Remove(0, end + 1);

        Console.WriteLine(
            $"[RX {DateTime.UtcNow:HH:mm:ss.fff}] {packet}"
        );
    }
};

port.Open();

Console.WriteLine("Receiver started.");
Console.ReadLine();