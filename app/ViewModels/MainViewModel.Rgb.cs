using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// LEDs do PC, pelo OpenRGB.
public partial class MainViewModel
{
    private const int RgbTemperatureMode = 2;
    // Entre uma tentativa de conexão e outra, em segundos.
    private const int RgbRetrySeconds = 5;

    private readonly RgbControl _rgb = new();
    private HashSet<string> _rgbSkipped = [];
    private (byte R, byte G, byte B)? _rgbSent;
    private bool _rgbBusy;
    private int _rgbWait;

    /// <summary>Dispositivos que o OpenRGB encontrou.</summary>
    public ObservableCollection<RgbDeviceViewModel> RgbDevices { get; } = [];

    [ObservableProperty]
    private bool _rgbEnabled;

    /// <summary>0 = cor fixa, 1 = cor de destaque da tela, 2 = conforme a temperatura do processador.</summary>
    [ObservableProperty]
    private int _rgbMode;

    [ObservableProperty]
    private Color _rgbColor = Color.FromUInt32(LcdTheme.Default.Accent);

    [ObservableProperty]
    private double _rgbBrightness = 100;

    [ObservableProperty]
    private decimal? _rgbPort = RgbControl.DefaultPort;

    [ObservableProperty]
    private string _rgbStatus = "Desligado.";

    [ObservableProperty]
    private bool _rgbConnected;

    private void LoadRgbSettings(Profile profile)
    {
        RgbEnabled = profile.RgbEnabled;
        RgbMode = Math.Clamp(profile.RgbMode, 0, RgbTemperatureMode);
        if (Color.TryParse(profile.RgbColor, out Color color)) RgbColor = color;
        RgbBrightness = Math.Clamp(profile.RgbBrightness, 5, 100);
        RgbPort = profile.RgbPort is > 0 and < 65536 ? profile.RgbPort : RgbControl.DefaultPort;
        _rgbSkipped = [.. profile.RgbSkipped];
    }

    private void StoreRgbSettings(Profile profile)
    {
        profile.RgbEnabled = RgbEnabled;
        profile.RgbMode = RgbMode;
        profile.RgbColor = RgbColor.ToString();
        profile.RgbBrightness = (int)RgbBrightness;
        profile.RgbPort = (int)(RgbPort ?? RgbControl.DefaultPort);
        profile.RgbSkipped = [.. _rgbSkipped];
    }

    partial void OnRgbEnabledChanged(bool value)
    {
        if (_loading)
            return;

        if (value)
        {
            _rgbWait = 0;
            RgbStatus = "Procurando o OpenRGB...";
        }
        else
        {
            DropRgb("Desligado. Os LEDs ficam na última cor enviada até outro programa mudar.");
        }
    }

    partial void OnRgbModeChanged(int value) => _rgbSent = null;

    partial void OnRgbPortChanged(decimal? value)
    {
        if (!_loading && RgbEnabled)
            Reconnect();
    }

    /// <summary>Refaz a conexão e a lista de dispositivos.</summary>
    [RelayCommand]
    private void Reconnect()
    {
        DropRgb("Procurando o OpenRGB...");
        _rgbWait = 0;
    }

    private void DropRgb(string status)
    {
        _rgb.Disconnect();
        RgbConnected = false;
        RgbDevices.Clear();
        _rgbSent = null;
        RgbStatus = status;
    }

    private void OnRgbDeviceToggled(RgbDeviceViewModel device)
    {
        if (device.Enabled)
            _rgbSkipped.Remove(device.Name);
        else
            _rgbSkipped.Add(device.Name);

        _rgbSent = null;
        SaveSettings();
    }

    /// <summary>Chamado a cada segundo: conecta se preciso e manda a cor quando ela muda.</summary>
    private async Task UpdateRgbAsync(SensorSnapshot sensors)
    {
        if (!RgbEnabled || _rgbBusy)
            return;

        _rgbBusy = true;
        try
        {
            if (!RgbConnected)
            {
                if (_rgbWait-- > 0)
                    return;

                _rgbWait = RgbRetrySeconds;
                await ConnectRgbAsync();
                if (!RgbConnected)
                    return;
            }

            (byte R, byte G, byte B) color = RgbTarget(sensors);
            if (color == _rgbSent)
                return;

            RgbDevice[] targets = RgbDevices.Where(d => d.Enabled).Select(d => d.Device).ToArray();
            await Task.Run(() =>
            {
                foreach (RgbDevice device in targets)
                    _rgb.SetColor(device, color.R, color.G, color.B);
            });
            _rgbSent = color;
        }
        catch (Exception ex)
        {
            DropRgb("A conexão com o OpenRGB caiu: " + ex.Message + " O app tenta de novo em alguns segundos.");
        }
        finally
        {
            _rgbBusy = false;
        }
    }

    private async Task ConnectRgbAsync()
    {
        int port = (int)(RgbPort ?? RgbControl.DefaultPort);
        try
        {
            IReadOnlyList<RgbDevice> devices = await Task.Run(() => _rgb.Connect(port));
            RgbDevices.Clear();
            foreach (RgbDevice device in devices)
                RgbDevices.Add(new RgbDeviceViewModel(device, !_rgbSkipped.Contains(device.Name), OnRgbDeviceToggled));

            RgbConnected = true;
            _rgbSent = null;
            RgbStatus = devices.Count == 0
                ? "Conectado ao OpenRGB, mas ele não encontrou nenhum dispositivo com LEDs."
                : $"Conectado ao OpenRGB: {devices.Count} dispositivo(s).";
        }
        catch (Exception)
        {
            RgbConnected = false;
            RgbStatus = $"O OpenRGB não respondeu na porta {port}. Abra o OpenRGB e ligue o servidor SDK; o app tenta de novo a cada {RgbRetrySeconds} segundos.";
        }
    }

    /// <summary>A cor a mandar agora, já com o brilho aplicado.</summary>
    private (byte R, byte G, byte B) RgbTarget(SensorSnapshot sensors)
    {
        Color color = RgbMode == 1 ? LcdAccent : RgbColor;
        if (RgbMode == RgbTemperatureMode && sensors.CpuTemperature is float temperature)
        {
            // Mesmas faixas e cores da tela: a cor escolhida até 10 °C antes da atenção, daí passa
            // aos poucos para a cor de atenção e depois para a crítica.
            float warm = (float)(CpuWarmAt ?? 75), hot = Math.Max(warm + 1, (float)(CpuHotAt ?? 88));
            color = temperature >= warm
                ? Mix(TempWarnColor, TempHotColor, (temperature - warm) / (hot - warm))
                : Mix(RgbColor, TempWarnColor, (temperature - (warm - 10)) / 10);
        }

        double level = RgbBrightness / 100;
        return ((byte)Math.Round(color.R * level), (byte)Math.Round(color.G * level), (byte)Math.Round(color.B * level));
    }

    private static Color Mix(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }
}

/// <summary>Um dispositivo do OpenRGB na lista, com a opção de deixá-lo de fora.</summary>
public partial class RgbDeviceViewModel(RgbDevice device, bool enabled, Action<RgbDeviceViewModel> toggled) : ObservableObject
{
    public RgbDevice Device { get; } = device;

    public string Name => Device.Name;

    public string Detail => Device.LedCount == 1 ? "1 LED" : $"{Device.LedCount} LEDs";

    [ObservableProperty]
    private bool _enabled = enabled;

    partial void OnEnabledChanged(bool value) => toggled(this);
}
