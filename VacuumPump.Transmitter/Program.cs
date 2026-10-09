
using System.IO.Ports;
using System.Threading;

using var port = new SerialPort("COM7", 115200, Parity.None, 8, StopBits.One);

port.Open();

Console.WriteLine("Modbus Transmitter COM7 started.");
Console.WriteLine("Radio channel must already be configured on the LG206.");

while (true)
{
    Console.WriteLine();
    Console.WriteLine("1. Send Modbus address 1");
    Console.WriteLine("2. Send Modbus address 2");
    Console.WriteLine("3. Send all 10 Modbus addresses");
    Console.WriteLine("4. Exit");
    Console.Write("Select: ");

    string choice = Console.ReadLine() ?? "";

    if (choice == "4")
        break;

    if (choice == "1" || choice == "2")
    {
        byte address = choice == "1" ? (byte)1 : (byte)2;
        uint value = address == 1 ? 39u : 40u;

        SendFrame(port, address, value);
    }
    else if (choice == "3")
    {
        for (byte address = 1; address <= 10; address++)
        {
            uint value = 38u + address;
            SendFrame(port, address, value);

            Thread.Sleep(100);
        }

        Console.WriteLine("Finished sending 10 Modbus responses.");
    }
    else
    {
        Console.WriteLine("Invalid option.");
    }
}

static void SendFrame(SerialPort port, byte address, uint value)
{
    byte[] frame =
    {
        address, 0x03, 0x04,
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value,
        0x00, 0x00
    };

    ushort crc = CalculateCrc(frame, 7);
    frame[7] = (byte)(crc & 0xFF);
    frame[8] = (byte)(crc >> 8);

    port.Write(frame, 0, frame.Length);

    Console.WriteLine(
        $"TX Address {address:D2} | Raw: {value} | " +
        $"Pressure: {value / 1000.0:F3} mbar | " +
        $"{BitConverter.ToString(frame)}");
}

static ushort CalculateCrc(byte[] data, int length)
{
    ushort crc = 0xFFFF;

    for (int i = 0; i < length; i++)
    {
        crc ^= data[i];

        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0
                ? (ushort)((crc >> 1) ^ 0xA001)
                : (ushort)(crc >> 1);
        }
    }

    return crc;
}
