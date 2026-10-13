using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Controls;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// Ventoinhas e bomba ligadas na placa-mãe.
public partial class MainViewModel
{
    private FanControl? _fans;
    private bool _fanSafetyActive;

    /// <summary>Conectores com alguma coisa ligada.</summary>
    public ObservableCollection<FanChannelViewModel> Fans { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FansUnavailable))]
    private bool _fansAvailable;

    [ObservableProperty]
    private string _fanStatus = "Procurando as ventoinhas…";

    /// <summary>Há ventoinhas com controlador próprio, que este app não alcança.</summary>
    [ObservableProperty]
    private bool _hasWirelessFans;

    public bool FansUnavailable => !FansAvailable;

    /// <summary>Abre o chip de monitoramento da placa-mãe e monta a lista de conectores.</summary>
    private async Task StartFansAsync()
    {
        _fans?.Dispose();
        _fans = null;
        Fans.Clear();
        FansAvailable = false;
        HasWirelessFans = HasLianLiController();

        if (_machine is not { DriverInstalled: true })
        {
            FanStatus = "Sem o driver PawnIO não dá para ler nem ajustar as ventoinhas.";
            return;
        }

        try
        {
            FanControl fans = await Task.Run(() => new FanControl());
            _fans = fans;

            // Conectores que uma execução encerrada à força deixou presos em velocidade fixa.
            IReadOnlyCollection<string> orphans = FanTakeover.ReadOrphans();

            // Só os conectores com rotação: os vazios não têm o que ajustar.
            foreach (FanChannel channel in fans.Channels.Where(c => c.Rpm is > 0))
            {
                FanSetting? saved = _stored.Fans.FirstOrDefault(f => f.Id == channel.Id);
                Fans.Add(new FanChannelViewModel(channel, saved, OnFanChanged) { IsOrphaned = orphans.Contains(channel.Id) });
            }

            if (Fans.Any(f => f.IsOrphaned))
                AddLog("O app foi encerrado à força com ventoinhas sob o controle dele. Esses conectores ficaram em velocidade fixa; " +
                       "o app segue cuidando deles pela curva até o PC reiniciar, quando a BIOS reassume.");

            FansAvailable = Fans.Count > 0;
            FanStatus = fans.Channels.Count == 0
                ? "A placa-mãe não expõe controle de ventoinha que o app reconheça."
                : Fans.Count == 0
                    ? $"Chip {fans.ChipName} encontrado, mas nenhum conector está girando."
                    : $"Chip de monitoramento: {fans.ChipName}. {Fans.Count} conectores com ventoinha ou bomba.";
        }
        catch (Exception ex)
        {
            FanStatus = "Não foi possível acessar o controle de ventoinhas: " + ex.Message;
        }
    }

    /// <summary>O Windows já viu o controlador sem fio da Lian Li (USB 0416:804x) neste PC.</summary>
    private static bool HasLianLiController()
    {
        using RegistryKey? usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        return usb?.GetSubKeyNames().Any(name => name.StartsWith("VID_0416&PID_804", StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>O usuário mudou algo em um conector: grava; a velocidade é acertada na próxima leitura, em até 1 s.</summary>
    private void OnFanChanged(FanChannelViewModel fan)
    {
        if (!_loading)
            SaveSettings();
    }

    [RelayCommand]
    private void ResetFans()
    {
        foreach (FanChannelViewModel fan in Fans)
            fan.Mode = FanChannelViewModel.Automatic;
        AddLog("Ventoinhas e bomba de volta ao controle automático da placa-mãe.");
    }

    /// <summary>A cada leitura dos sensores: atualiza rotações, calcula as curvas e acerta as velocidades.</summary>
    private async Task UpdateFansAsync(Machine machine, SensorSnapshot sensors)
    {
        if (_fans is not { } fans || Fans.Count == 0)
            return;

        await Task.Run(fans.Update);

        // Velocidade fixa não acompanha a carga, e uma curva pode ter sido desenhada baixa demais.
        // Se o processador chegar perto do limite, o app devolve tudo à placa-mãe e só retoma
        // depois de esfriar.
        float? cpu = sensors.CpuTemperature;
        float limit = machine.CpuTemperatureLimit;
        bool anyControlled = Fans.Any(f => f.Mode != FanChannelViewModel.Automatic);
        if (!_fanSafetyActive && anyControlled && cpu >= limit - 8)
        {
            _fanSafetyActive = true;
            AddLog($"Processador a {cpu:0.0} °C: controle das ventoinhas devolvido à placa-mãe até esfriar.");
        }
        else if (_fanSafetyActive && cpu < limit - 20)
        {
            _fanSafetyActive = false;
            AddLog("Temperatura normalizada: velocidades fixas e curvas das ventoinhas retomadas.");
        }

        foreach (FanChannelViewModel fan in Fans)
        {
            fan.Refresh();
            double? target = _fanSafetyActive ? null : fan.Target(cpu, sensors.Gpu.Temperature);
            if (fan.NeedsApply(target))
                Apply(fan, target);
        }
    }

    private static void Apply(FanChannelViewModel fan, double? target)
    {
        fan.Applied = target;
        Task.Run(() =>
        {
            try
            {
                if (target is double percent)
                {
                    // Anota antes de assumir: se o app for encerrado à força, a próxima abertura sabe.
                    FanTakeover.Mark(fan.Channel.Id);
                    fan.Channel.SetPercent((float)percent);
                }
                else
                {
                    fan.Channel.SetAutomatic();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        });
    }

    /// <summary>Saída normal: devolve tudo à placa-mãe e limpa a anotação do que estava sob o app.</summary>
    private void ReleaseFans()
    {
        if (_fans is not { } fans)
            return;

        fans.Dispose();
        FanTakeover.Release(Fans.Where(f => f.IsOrphaned).Select(f => f.Channel.Id).ToArray());
        _fans = null;
    }
}

/// <summary>Um conector de ventoinha na janela.</summary>
public partial class FanChannelViewModel : ViewModelBase
{
    public const int Automatic = 0, Fixed = 1, Curve = 2;

    // Abaixo disso uma ventoinha pode parar; a bomba precisa de bem mais para manter o fluxo.
    public const double MinFanPercent = 20, MinPumpPercent = 60;

    private static readonly Point[] DefaultFanCurve = [new(35, 30), new(50, 40), new(65, 60), new(75, 80), new(85, 100)];
    private static readonly Point[] DefaultPumpCurve = [new(35, 70), new(50, 75), new(65, 85), new(75, 100), new(85, 100)];

    private readonly Action<FanChannelViewModel> _changed;
    // Temperatura suavizada que alimenta a curva, para a ventoinha não ficar subindo e descendo.
    private double _smoothed = double.NaN;

    public FanChannelViewModel(FanChannel channel, FanSetting? saved, Action<FanChannelViewModel> changed)
    {
        Channel = channel;
        _changed = changed;
        _label = saved?.Label is { Length: > 0 } label ? label : channel.Name;
        _isPump = saved?.IsPump ?? false;
        _percent = saved?.Percent is > 0 ? saved.Percent : Math.Round(channel.Percent ?? 60);
        // Perfis antigos só tinham "Manual" (velocidade fixa).
        _mode = saved is null ? Automatic : saved.Mode != Automatic ? Math.Clamp(saved.Mode, Automatic, Curve) : saved.Manual ? Fixed : Automatic;
        _source = Math.Clamp(saved?.Source ?? 0, 0, 1);
        _points = saved?.Curve is { Count: >= 2 } curve
            ? curve.OrderBy(p => p.Temperature).Select(p => new Point(p.Temperature, p.Percent)).ToArray()
            : _isPump ? DefaultPumpCurve : DefaultFanCurve;
        Refresh();
    }

    public FanChannel Channel { get; }

    /// <summary>
    /// O conector ficou preso em velocidade fixa por uma execução encerrada à força. Até o PC
    /// reiniciar, "automático" não existe para ele: o app o mantém na curva.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsCurve))]
    private bool _isOrphaned;

    /// <summary>O que o app mandou por último para o conector: uma porcentagem, ou null para automático.</summary>
    public double? Applied { get; set; } = double.NaN;

    /// <summary>Nome dado pelo usuário, como "Radiador" ou "Bomba".</summary>
    [ObservableProperty]
    private string _label;

    /// <summary>Marcado, o conector não aceita velocidade abaixo do mínimo da bomba.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Minimum))]
    private bool _isPump;

    /// <summary>0 = automático da placa-mãe, 1 = velocidade fixa, 2 = curva por temperatura.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFixed), nameof(IsCurve), nameof(ShowsCurve))]
    private int _mode;

    /// <summary>Velocidade fixa escolhida, em %.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _percent;

    /// <summary>Temperatura que a curva segue: 0 = processador, 1 = placa de vídeo.</summary>
    [ObservableProperty]
    private int _source;

    /// <summary>Pontos da curva: X em °C, Y em %.</summary>
    [ObservableProperty]
    private IReadOnlyList<Point> _points;

    [ObservableProperty]
    private string _rpmText = "—";

    [ObservableProperty]
    private string _currentText = "";

    /// <summary>Temperatura que a curva está usando agora; NaN sem leitura.</summary>
    [ObservableProperty]
    private double _curveTemperature = double.NaN;

    [ObservableProperty]
    private string _curveText = "";

    public bool IsFixed => Mode == Fixed;

    public bool IsCurve => Mode == Curve;

    /// <summary>A curva aparece quando é o modo escolhido e também quando o app está cuidando de um conector órfão.</summary>
    public bool ShowsCurve => IsCurve || IsOrphaned;

    public double Minimum => IsPump ? MinPumpPercent : MinFanPercent;

    public string PercentText => $"{Math.Clamp(Percent, Minimum, 100):0} %";

    public FanSetting ToSetting() => new()
    {
        Id = Channel.Id,
        Label = Label,
        IsPump = IsPump,
        Mode = Mode,
        Percent = Math.Clamp(Percent, Minimum, 100),
        Source = Source,
        Curve = Points.Select(p => new FanCurvePoint { Temperature = p.X, Percent = p.Y }).ToList(),
    };

    /// <summary>Atualiza a rotação e a velocidade lidas do chip.</summary>
    public void Refresh()
    {
        RpmText = Channel.Rpm is > 0 ? $"{Channel.Rpm:0} RPM" : "parada";
        CurrentText = Channel.Percent is float percent ? $"{percent:0} %" : "";
    }

    /// <summary>
    /// A velocidade que o conector deve ter agora, ou null para deixar com a placa-mãe. Na curva,
    /// a temperatura sobe rápido e desce devagar, para a ventoinha não oscilar.
    /// </summary>
    public double? Target(float? cpuTemperature, float? gpuTemperature)
    {
        if (Mode == Fixed)
            return Math.Clamp(Percent, Minimum, 100);
        if (Mode != Curve && !IsOrphaned)
            return null;

        float? reading = Source == 1 ? gpuTemperature : cpuTemperature;
        if (reading is not float temperature)
        {
            // Sem a temperatura que a curva segue, quem decide é a placa-mãe.
            CurveTemperature = double.NaN;
            CurveText = "Sem leitura dessa temperatura: o conector fica no automático.";
            _smoothed = double.NaN;
            return null;
        }

        _smoothed = double.IsNaN(_smoothed) ? temperature
            : _smoothed + (temperature - _smoothed) * (temperature > _smoothed ? 0.6 : 0.12);

        double percent = Math.Clamp(Math.Round(FanCurveEditor.Evaluate(Points, _smoothed)), Minimum, 100);
        CurveTemperature = _smoothed;
        CurveText = $"Agora: {_smoothed:0} °C, {percent:0} %.";
        return percent;
    }

    /// <summary>Só reenvia quando muda de fato; a curva oscila em frações que não valem um comando.</summary>
    public bool NeedsApply(double? target) => target is double percent
        ? Applied is not double applied || double.IsNaN(applied) || Math.Abs(applied - percent) >= 1
        : Applied is not null;

    partial void OnIsPumpChanged(bool value)
    {
        // O mínimo mudou: nada da curva nem da velocidade fixa pode ficar abaixo dele.
        if (Percent < Minimum)
            Percent = Minimum;
        Points = Points.Select(p => new Point(p.X, Math.Max(p.Y, Minimum))).ToArray();
        _changed(this);
    }

    partial void OnModeChanged(int value) => _changed(this);

    partial void OnPercentChanged(double value) => _changed(this);

    partial void OnSourceChanged(int value)
    {
        _smoothed = double.NaN;
        _changed(this);
    }

    partial void OnPointsChanged(IReadOnlyList<Point> value) => _changed(this);

    partial void OnLabelChanged(string value) => _changed(this);

    /// <summary>Volta a curva para a sugerida para ventoinha ou para bomba.</summary>
    [RelayCommand]
    private void ResetCurve() => Points = IsPump ? DefaultPumpCurve : DefaultFanCurve;
}
