using System.IO.Ports;

using var port = new SerialPort("COM3", 115200, Parity.None, 8, StopBits.One);

port.Open();

Console.WriteLine("Transmitter started.");

while (true)
{
    Console.Write("Send: ");
    string message = Console.ReadLine() ?? "";

    port.Write(message);

    Console.WriteLine($"TX: {message}");
}