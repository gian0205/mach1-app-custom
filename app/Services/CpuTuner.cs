using Mach1DriverCustom.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ZenStates.Core;

namespace Mach1DriverCustom.Services;

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
        TemperatureLimit = zen4OrNewer ? 95 : 90;
    }

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

    /// <summary>Só famílias com Curve Optimizer por núcleo via SMU (Zen 3 em diante).</summary>
    public bool SupportsCurveOptimizer =>
        _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin > 0 || _cpu.smu.Mp1Smu.SMU_MSG_SetDldoPsmMargin > 0;

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

        for (int core = 0; core < Math.Min(CoreCount, profile.CoreMargins.Length); core++)
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
