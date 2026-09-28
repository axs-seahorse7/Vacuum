using System.IO.Ports;

using var port = new SerialPort("COM6", 115200, Parity.None, 8, StopBits.One)
{
    ReadTimeout = 1000
};

port.DataReceived += (s, e) =>
{
    string data = port.ReadExisting();
    Console.WriteLine($"[COM6 RX] {data}");
};

port.Open();

Console.WriteLine("COM6 receiver started.");
Console.WriteLine("Waiting for LoRa data...");

Console.ReadLine();