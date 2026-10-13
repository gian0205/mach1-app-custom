using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mach1CustomApp.Services;

/// <summary>Um conector de ventoinha da placa-mãe: a rotação lida e o controle de velocidade.</summary>
public sealed class FanChannel
{
    private readonly ISensor? _fan;
    private readonly ISensor _control;

    internal FanChannel(ISensor control, ISensor? fan)
    {
        _control = control;
        _fan = fan;
    }

    /// <summary>Identificador estável do conector, usado para guardar a escolha no perfil.</summary>
    public string Id => _control.Identifier.ToString();

    /// <summary>Nome que a placa-mãe dá ao conector, ou "Fan #N" quando ela não informa.</summary>
    public string Name => _fan?.Name ?? _control.Name;

    /// <summary>Rotação em RPM; null ou 0 quando não há nada ligado no conector.</summary>
    public float? Rpm => _fan?.Value;

    /// <summary>Velocidade pedida, de 0 a 100 %.</summary>
    public float? Percent => _control.Value;

    /// <summary>Fixa a velocidade em uma porcentagem.</summary>
    public void SetPercent(float percent) => _control.Control?.SetSoftware(Math.Clamp(percent, 0, 100));

    /// <summary>Devolve o conector ao controle automático da BIOS.</summary>
    public void SetAutomatic() => _control.Control?.SetDefault();
}

/// <summary>
/// Velocidade das ventoinhas e da bomba ligadas na placa-mãe, pelo chip de monitoramento dela
/// (via LibreHardwareMonitor e o driver PawnIO). Ventoinhas ligadas em um controlador próprio,
/// como as sem fio da Lian Li, não passam por aqui.
/// </summary>
public sealed class FanControl : IDisposable
{
    private readonly Computer _computer;
    private readonly IHardware[] _chips;

    /// <summary>A instância em uso, para o app conseguir devolver as ventoinhas se travar.</summary>
    public static FanControl? Current { get; private set; }

    public FanControl()
    {
        _computer = new Computer { IsMotherboardEnabled = true };
        _computer.Open();

        // O chip de monitoramento aparece como sub-hardware da placa-mãe.
        _chips = _computer.Hardware.SelectMany(h => h.SubHardware).ToArray();
        Update();

        var channels = new List<FanChannel>();
        foreach (IHardware chip in _chips)
        {
            ISensor[] fans = chip.Sensors.Where(s => s.SensorType == SensorType.Fan).ToArray();
            foreach (ISensor control in chip.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control is not null))
            {
                // O controle "#3" comanda a ventoinha "#3"; os dois compartilham o número no nome ou no índice.
                ISensor? fan = fans.FirstOrDefault(f => f.Index == control.Index)
                    ?? fans.FirstOrDefault(f => Number(f.Name) is int n && n == Number(control.Name));
                channels.Add(new FanChannel(control, fan));
            }
        }
        Channels = channels;
        ChipName = string.Join(", ", _chips.Select(c => c.Name));
        Current = this;
    }

    /// <summary>Nome do chip de monitoramento encontrado; vazio quando a placa não é reconhecida.</summary>
    public string ChipName { get; }

    public IReadOnlyList<FanChannel> Channels { get; }

    /// <summary>Lê de novo as rotações e as velocidades.</summary>
    public void Update()
    {
        foreach (IHardware chip in _chips)
            chip.Update();
    }

    private static int? Number(string name) =>
        Regex.Match(name, @"#(\d+)") is { Success: true } match ? int.Parse(match.Groups[1].Value) : null;

    /// <summary>Devolve todos os conectores ao controle da BIOS e fecha o acesso.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(Current, this))
            Current = null;

        foreach (FanChannel channel in Channels)
        {
            try
            {
                channel.SetAutomatic();
            }
            catch (Exception)
            {
                // Seguimos para os demais: o importante é não deixar nenhum preso em velocidade fixa.
            }
        }
        _computer.Close();
    }
}
