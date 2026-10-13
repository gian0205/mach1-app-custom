using Mach1CustomApp.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ZenStates.Core;

namespace Mach1CustomApp.Services;

/// <summary>
/// Fala com o SMU do processador pela ZenStates-Core. Todas as chamadas são serializadas,
/// porque o SMU atende um comando por vez.
/// </summary>
public sealed class CpuTuner : IDisposable
{
    /// <summary>Menor margem em Zen 4 e Zen 5: o SMU aceita até -50, mas além de -40 raramente é estável.</summary>
    public const int DefaultMinMargin = -40;

    public const int MaxMargin = 10;

    private readonly Cpu _cpu;
    private readonly object _gate = new();

    public static bool IsDriverInstalled => ZenStates.Core.PawnIo.PawnIo.IsInstalled;

    public CpuTuner()
    {
        // Só precisamos do SMU; desliga as leituras periódicas de memória e sensores.
        var options = new CoreOptions
        {
            Timings = new CoreOptions.ModuleSettings { Enabled = true, Autorefresh = false },
            Sensors = new CoreOptions.ModuleSettings { Enabled = true, Autorefresh = false },
        };
        _cpu = new Cpu(options);

        int cores = (int)_cpu.info.topology.physicalCores;
        CoreCount = cores > 0 ? cores : Math.Max(1, Environment.ProcessorCount / 2);
        ThreadsPerCore = Math.Max(1, (int)_cpu.info.topology.threadsPerCore);

        // Zen 4 e Zen 5 (mesmo tipo de SMU) vão a -50 e limitam em 95 °C; os anteriores, a -30 e 90 °C.
        bool zen4OrNewer = _cpu.smu.SMU_TYPE == ZenStates.Core.SMU.SmuType.TYPE_CPU4;
        MinMargin = zen4OrNewer ? DefaultMinMargin : -30;
        TemperatureLimit = LimitFor(_cpu.info.codeName, CpuName);

        // MACH1_NO_CO=1 finge um Ryzen sem Curve Optimizer (Zen 2 ou anterior), para testar esse caminho.
        SupportsCurveOptimizer = Environment.GetEnvironmentVariable("MACH1_NO_CO") is null &&
            (_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin > 0 || _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin > 0);
        SupportsLimits = _cpu.smu.Rsmu.SMU_MSG_SetFastLimit > 0 || _cpu.smu.Mp1Smu.SMU_MSG_SetFastLimit > 0;
    }

    /// <summary>
    /// Temperatura máxima de operação por geração, conforme as especificações da AMD. É uma regra
    /// geral por família; modelos específicos fogem dela por alguns graus.
    /// </summary>
    private static float LimitFor(Cpu.CodeName codeName, string cpuName) => codeName switch
    {
        // Zen 4 e Zen 5 de mesa e seus derivados.
        Cpu.CodeName.Raphael or Cpu.CodeName.GraniteRidge or Cpu.CodeName.DragonRange => 95,
        // Zen 3 de mesa: 90 °C, menos os de 65 W (5600X, 5600, 5700), que vão a 95 °C.
        Cpu.CodeName.Vermeer => cpuName.Contains("5600") || cpuName.Contains("5700 ") ? 95 : 90,
        // Zen+ de 105 W.
        Cpu.CodeName.PinnacleRidge => cpuName.Contains("2700X") ? 85 : 95,
        // Threadripper das primeiras gerações: 68 °C.
        Cpu.CodeName.Whitehaven or Cpu.CodeName.Colfax or Cpu.CodeName.CastlePeak => 68,
        Cpu.CodeName.Chagall or Cpu.CodeName.StormPeak or Cpu.CodeName.ShimadaPeak => 95,
        _ => 95,
    };

    /// <summary>Menor margem que o app deixa aplicar neste processador.</summary>
    public int MinMargin { get; }

    /// <summary>Temperatura em que este processador começa a se limitar, em °C.</summary>
    public float TemperatureLimit { get; }

    public string CpuName => _cpu.info.cpuName?.Trim() ?? "?";
    // "GraniteRidge" → "Granite Ridge"
    public string CodeName => Regex.Replace(_cpu.info.codeName.ToString(), "(?<=[a-z])(?=[A-Z])", " ");
    public string SmuVersion => _cpu.smu.Version.ToString("X8");
    public int CoreCount { get; }
    public int ThreadsPerCore { get; }

    /// <summary>Curve Optimizer por núcleo via SMU: Zen 3 (série 5000) em diante.</summary>
    public bool SupportsCurveOptimizer { get; }

    /// <summary>Limites de PBO (PPT, TDC, EDC) via SMU: praticamente todo Ryzen.</summary>
    public bool SupportsLimits { get; }

    private uint CoreMask(int core) =>
        _cpu.MakeCoreMaskForLogicalCore(core) ?? _cpu.MakeCoreMask((uint)core);

    public int? GetCoreMargin(int core)
    {
        lock (_gate)
        {
            uint? raw = _cpu.GetPsmMarginSingleCore(CoreMask(core));
            if (raw is not uint value)
                return null;

            // O SMU devolve a margem como inteiro com sinal de 32 bits; alguns firmwares usam 16.
            if (value is > 0x7FFF and <= 0xFFFF)
                return (int)value - 0x10000;
            return unchecked((int)value);
        }
    }

    public bool SetCoreMargin(int core, int margin)
    {
        margin = Math.Clamp(margin, MinMargin, MaxMargin);
        lock (_gate)
            return _cpu.SetPsmMarginSingleCore(CoreMask(core), margin);
    }

    /// <summary>Limites de fábrica do processador, em W e A, ou null quando o SMU não informa.</summary>
    public (int Ppt, int Tdc)? GetStockLimits()
    {
        lock (_gate)
        {
            Cpu.PboFusedLimits? limits = _cpu.GetPboFusedLimits();
            if (limits is not { PowerLimit: > 0 } l)
                return null;

            return (FromMilli(l.PowerLimit), FromMilli(l.VrmVddTdcCurrent));
        }

        static int FromMilli(int value) => value >= 10_000 ? value / 1000 : value;
    }

    public float? GetTemperature()
    {
        lock (_gate)
            return _cpu.GetCpuTemperature();
    }

    public double GetCoreClockMhz(int core)
    {
        lock (_gate)
            return _cpu.GetCoreClockMhz(core); // a biblioteca converte para o índice lógico
    }

    /// <summary>Potência entregue aos núcleos (tensão x corrente do trilho VDD), em watts, ou null se a telemetria não informa.</summary>
    public float? GetCorePowerWatts()
    {
        lock (_gate)
        {
            if (_cpu.RefreshPowerTable() != ZenStates.Core.SMU.Status.OK)
                return null;

            float watts = _cpu.powerTable.VDDCR_VDD_VOLTAGE * _cpu.powerTable.VDDCR_VDD_CURRENT;
            return watts > 0 && !float.IsNaN(watts) ? watts : null;
        }
    }

    /// <summary>Aplica o perfil e devolve uma linha de log por operação.</summary>
    public IReadOnlyList<string> Apply(Profile profile)
    {
        var log = new List<string>();

        // Sem Curve Optimizer não há margem a aplicar, só os limites.
        int cores = SupportsCurveOptimizer ? Math.Min(CoreCount, profile.CoreMargins.Length) : 0;
        for (int core = 0; core < cores; core++)
        {
            int margin = Math.Clamp(profile.CoreMargins[core], MinMargin, MaxMargin);
            bool ok = SetCoreMargin(core, margin);
            log.Add($"Núcleo {core}: margem {margin:+0;-0;0} → {(ok ? "OK" : "rejeitado pelo SMU")}");
        }

        if (profile.ChangeLimits)
        {
            lock (_gate)
            {
                if (profile.Ppt > 0)
                    log.Add($"PPT {profile.Ppt} W → {_cpu.SetPPTLimit((uint)profile.Ppt)}");
                if (profile.Tdc > 0)
                    log.Add($"TDC {profile.Tdc} A → {_cpu.SetTDCVDDLimit((uint)profile.Tdc)}");
                if (profile.Edc > 0)
                    log.Add($"EDC {profile.Edc} A → {_cpu.SetEDCVDDLimit((uint)profile.Edc)}");
            }
        }

        return log;
    }

    public void Dispose() => _cpu.Dispose();
}
