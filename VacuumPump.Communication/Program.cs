using System.IO.Ports;
using System.Text;

using var port = new SerialPort("COM6", 115200, Parity.None, 8, StopBits.One);

var buffer = new StringBuilder();

port.DataReceived += (s, e) =>
{
    string chunk = port.ReadExisting();

    Console.WriteLine($"[CHUNK] {chunk}");

    buffer.Append(chunk);

    while (true)
    {
        string data = buffer.ToString();

        int start = data.IndexOf('<');
        int end = data.IndexOf('>', start + 1);

        if (start < 0 || end < 0)
            break;

        string packet = data.Substring(start, end - start + 1);

        buffer.Remove(0, end + 1);

        Console.WriteLine($"[COMPLETE PACKET] {packet}");
    }
};

port.Open();

Console.WriteLine("Receiver COM6 started.");
Console.WriteLine("Waiting for packets...");

Console.ReadLine();