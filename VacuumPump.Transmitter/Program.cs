using System.IO.Ports;

using var port = new SerialPort("COM3", 115200, Parity.None, 8, StopBits.One);

port.Open();

Console.WriteLine("LoRa transmitter started.");

int sequence = 1;

while (true)
{
    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff");
    string packet = $"<PUMP001|SEQ={sequence}|TS={timestamp}>";

    port.Write(packet);

    Console.WriteLine($"[TX] {packet}");

    sequence++;

    Thread.Sleep(1000);
}