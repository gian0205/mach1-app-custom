using OpenRGB.NET;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1CustomApp.Services;

/// <summary>Um dispositivo com LEDs que o OpenRGB encontrou.</summary>
public sealed record RgbDevice(int Index, string Name, int LedCount);

/// <summary>
/// Manda cores para os LEDs do PC pelo servidor SDK do OpenRGB, que precisa estar aberto nesta
/// máquina. O app não fala com os LEDs direto: quem conhece cada placa, memória e controlador é o
/// OpenRGB. As chamadas são serializadas, porque o servidor atende um comando por vez.
/// </summary>
public sealed class RgbControl : IDisposable
{
    public const int DefaultPort = 6742;

    private readonly object _gate = new();
    private readonly HashSet<int> _prepared = [];
    private OpenRgbClient? _client;

    public bool Connected
    {
        get
        {
            lock (_gate)
                return _client?.Connected == true;
        }
    }

    /// <summary>Conecta ao servidor e devolve os dispositivos com LEDs. Lança se o servidor não responde.</summary>
    public IReadOnlyList<RgbDevice> Connect(int port)
    {
        lock (_gate)
        {
            Close();
            var client = new OpenRgbClient("127.0.0.1", port, "MACH1 custom app", false, 1500);
            try
            {
                client.Connect();
                List<RgbDevice> devices = client.GetAllControllerData()
                    .Where(d => d.Leds.Length > 0)
                    .Select(d => new RgbDevice(d.Index, d.Name, d.Leds.Length))
                    .ToList();
                _client = client;
                return devices;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    /// <summary>Pinta todos os LEDs do dispositivo com uma cor. Lança se a conexão caiu.</summary>
    public void SetColor(RgbDevice device, byte red, byte green, byte blue)
    {
        lock (_gate)
        {
            if (_client is not { Connected: true } client)
                throw new InvalidOperationException("Sem conexão com o OpenRGB.");

            // O modo personalizado é o que aceita cor por LED; basta pedir uma vez por dispositivo.
            if (_prepared.Add(device.Index))
                client.SetCustomMode(device.Index);

            var colors = new Color[device.LedCount];
            Array.Fill(colors, new Color(red, green, blue));
            client.UpdateLeds(device.Index, colors);
        }
    }

    public void Disconnect()
    {
        lock (_gate)
            Close();
    }

    private void Close()
    {
        _prepared.Clear();
        try
        {
            _client?.Dispose();
        }
        catch (Exception)
        {
            // Conexão já caída: não há o que fechar.
        }
        _client = null;
    }

    public void Dispose() => Disconnect();
}
