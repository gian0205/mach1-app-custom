using HidSharp;
using System;
using System.Text;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Tela LCD do water cooler (USB 1600:3005, "HOTSPOTEKUSB HID DEMO"), 720x1280.
/// Protocolo levantado por captura do tráfego do MACH1-LCD: relatórios HID de saída de 1024
/// bytes, cada comando começando por "CRT\0\0" e três letras:
///   CONNECT  mantém a conexão (o app original manda a cada ~10 s)
///   DIS      liga a tela
///   LIG      brilho, 0 a 100, no byte 10
///   DRA      um quadro JPEG: tamanho (JPEG + 32 do cabeçalho) em big-endian no byte 8, 0xB1 no byte 12, dados a partir
///            do byte 32 e continuando nos pacotes seguintes, com zeros até fechar o último
/// </summary>
public sealed class LcdDevice : IDisposable
{
    public const int Width = 720;
    public const int Height = 1280;

    private const int VendorId = 0x1600;
    private const int ProductId = 0x3005;
    private const int PacketSize = 1024;
    private const int FrameHeaderSize = 32;

    private readonly HidStream _stream;
    private readonly object _gate = new();
    // Byte 0 é o ID do relatório (0); a API HID do Windows exige que ele vá junto.
    private readonly byte[] _report = new byte[PacketSize + 1];

    public static bool IsPresent => DeviceList.Local.GetHidDeviceOrNull(VendorId, ProductId) is not null;

    public LcdDevice()
    {
        HidDevice device = DeviceList.Local.GetHidDeviceOrNull(VendorId, ProductId)
            ?? throw new InvalidOperationException("Tela do water cooler não encontrada.");

        if (!device.TryOpen(out _stream))
            throw new InvalidOperationException("Não foi possível abrir a tela do water cooler.");

        _stream.WriteTimeout = 2000;
    }

    public void KeepAlive() => SendCommand("CONNECT");

    public void Wake() => SendCommand("DIS");

    public void SetBrightness(int percent) =>
        SendCommand("LIG", 0, 0, (byte)Math.Clamp(percent, 0, 100));

    public void SendFrame(ReadOnlySpan<byte> jpeg)
    {
        lock (_gate)
        {
            Span<byte> packet = _report.AsSpan(1);
            packet.Clear();
            WritePrefix(packet, "DRA");
            // O tamanho declarado inclui os 32 bytes do cabeçalho.
            int total = jpeg.Length + FrameHeaderSize;
            packet[8] = (byte)(total >> 24);
            packet[9] = (byte)(total >> 16);
            packet[10] = (byte)(total >> 8);
            packet[11] = (byte)total;
            packet[12] = 0xB1;

            int first = Math.Min(jpeg.Length, PacketSize - FrameHeaderSize);
            jpeg[..first].CopyTo(packet[FrameHeaderSize..]);
            _stream.Write(_report);

            for (int offset = first; offset < jpeg.Length; offset += PacketSize)
            {
                int count = Math.Min(PacketSize, jpeg.Length - offset);
                packet.Clear();
                jpeg.Slice(offset, count).CopyTo(packet);
                _stream.Write(_report);
            }
        }
    }

    private void SendCommand(string command, params byte[] arguments)
    {
        lock (_gate)
        {
            Span<byte> packet = _report.AsSpan(1);
            packet.Clear();
            int length = WritePrefix(packet, command);
            arguments.CopyTo(packet[length..]);
            _stream.Write(_report);
        }
    }

    private static int WritePrefix(Span<byte> packet, string command)
    {
        packet[0] = (byte)'C';
        packet[1] = (byte)'R';
        packet[2] = (byte)'T';
        return 5 + Encoding.ASCII.GetBytes(command, packet[5..]);
    }

    public void Dispose()
    {
        lock (_gate)
            _stream.Dispose();
    }
}
