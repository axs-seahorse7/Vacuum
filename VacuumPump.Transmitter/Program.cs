using System.IO.Ports;

using var port = new SerialPort("COM7", 115200, Parity.None, 8, StopBits.One);

port.Open();

Console.WriteLine("COM7 transmitter started.");

while (true)
{
    Console.Write("Message: ");
    string? message = Console.ReadLine();

    if (string.IsNullOrEmpty(message))
        break;

    port.Write(message);
    Console.WriteLine($"[COM7 TX] {message}");
}