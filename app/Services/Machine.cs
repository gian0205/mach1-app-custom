using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Mach1CustomApp.Services;

public enum CpuVendor
{
    Amd,
    Intel,
    Other,
}

/// <summary>
/// O computador onde o app está rodando, do jeito que o resto do código precisa enxergar: qual
/// é o processador, como estão os núcleos e de onde vem cada sensor. Os ajustes só existem em
/// Ryzen com SMU conhecido (<see cref="Tuner"/>): limites de PBO em quase todos, Curve Optimizer
/// da série 5000 em diante. Tudo o mais funciona em qualquer máquina, Intel inclusive, mostrando
/// o que der para ler.
/// </summary>
public sealed class Machine : IDisposable
{
    private readonly SystemSensors _system = new();
    private readonly GpuMonitor? _nvidia;
    private readonly GenericSensors? _generic;
    private readonly EffectiveClock _clocks;
    private readonly float? _genericLimit;

    /// <summary>Abre o acesso ao hardware. Demora um ou dois segundos; chamar fora da thread de UI.</summary>
    /// <param name="forceGeneric">Ignora o caminho Ryzen e o da NVIDIA, para testar o caminho genérico.</param>
    public Machine(bool forceGeneric = false)
    {
        CpuName = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string)?.Trim() ?? "Processador";
        NominalMhz = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz", null) is int mhz ? mhz : 0;
        Vendor = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "VendorIdentifier", null) as string) switch
        {
            "GenuineIntel" => CpuVendor.Intel,
            "AuthenticAMD" => CpuVendor.Amd,
            _ => CpuVendor.Other,
        };
        CoreProcessors = ReadCoreProcessors(out int withSmt, out int withoutSmt);
        // Num processador híbrido da Intel os núcleos P têm duas threads e os E, uma.
        CoreSummary = Vendor == CpuVendor.Intel && withSmt > 0 && withoutSmt > 0
            ? $"{withSmt} núcleos P e {withoutSmt} núcleos E, {Environment.ProcessorCount} threads"
            : $"{CoreProcessors.Length} núcleos, {Environment.ProcessorCount} threads";
        DriverInstalled = CpuTuner.IsDriverInstalled;

        if (!forceGeneric && DriverInstalled)
        {
            try
            {
                // Qualquer Ryzen que a biblioteca conheça entra aqui, com ou sem Curve Optimizer.
                Tuner = new CpuTuner();
            }
            catch (Exception ex)
            {
                // Não é AMD, é um Ryzen que a biblioteca não conhece, ou o driver recusou.
                TunerProblem = ex.Message;
            }
        }

        if (!forceGeneric)
            _nvidia = GpuMonitor.TryCreate();

        // A biblioteca genérica só é aberta para cobrir o que faltou.
        bool needCpu = Tuner is null && DriverInstalled, needGpu = _nvidia is null;
        if (needCpu || needGpu)
        {
            try
            {
                _generic = new GenericSensors(needCpu, needGpu);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        _clocks = new EffectiveClock(NominalMhz);
        _genericLimit = Tuner is null ? _generic?.ReadCpuLimit() : null;
    }

    public CpuVendor Vendor { get; }

    /// <summary>Contagem de núcleos e threads em texto, separando núcleos P e E quando existem.</summary>
    public string CoreSummary { get; }

    public string CpuName { get; }

    /// <summary>Frequência nominal do processador, em MHz.</summary>
    public double NominalMhz { get; }

    /// <summary>O driver PawnIO está instalado; sem ele não há ajuste nem temperatura do processador.</summary>
    public bool DriverInstalled { get; }

    /// <summary>Ajuste da curva e dos limites; null quando o processador não é um Ryzen suportado.</summary>
    public CpuTuner? Tuner { get; }

    /// <summary>Por que <see cref="Tuner"/> não abriu, quando houve erro.</summary>
    public string? TunerProblem { get; }

    /// <summary>Um processador lógico de cada núcleo físico, na ordem dos núcleos.</summary>
    public int[] CoreProcessors { get; }

    public int CoreCount => CoreProcessors.Length;

    public int LogicalCount => Environment.ProcessorCount;

    /// <summary>Temperatura em que o processador começa a se limitar, em °C.</summary>
    public float CpuTemperatureLimit => Tuner?.TemperatureLimit ?? _genericLimit ?? 100;

    public float? ReadCpuTemperature() => Tuner?.GetTemperature() ?? _generic?.ReadCpuTemperature();

    /// <summary>Potência do processador, em watts: a dos núcleos em Ryzen com telemetria, a do encapsulamento nos demais.</summary>
    public float? ReadCorePower() => Tuner is { } tuner ? tuner.GetCorePowerWatts() : _generic?.ReadCpuPower();

    public SensorSnapshot ReadSensors()
    {
        var (ramUsed, ramTotal) = _system.ReadMemory();
        GpuReading gpu = _nvidia?.Read() ?? _generic?.ReadGpu() ?? default;
        return new SensorSnapshot(ReadCpuTemperature(), _system.ReadCpuLoad(), ramUsed, ramTotal, gpu);
    }

    /// <summary>
    /// Clock de cada núcleo, em MHz: o que o SMU informa em Ryzen suportado; nos demais, o clock
    /// efetivo medido pelo Windows.
    /// </summary>
    public double[] ReadCoreClocks()
    {
        if (Tuner is { } tuner)
            return Enumerable.Range(0, tuner.CoreCount).Select(tuner.GetCoreClockMhz).ToArray();

        double[] byProcessor = _clocks.ReadByProcessor();
        return CoreProcessors.Select(p => p < byProcessor.Length ? Math.Round(byProcessor[p]) : 0).ToArray();
    }

    /// <summary>
    /// Maior clock que um núcleo pode estar entregando agora; leituras de clock efetivo acima
    /// disso são falhas do contador do Windows.
    /// </summary>
    public double ClockCeiling() => Tuner is { } tuner
        ? Enumerable.Range(0, tuner.CoreCount).Max(tuner.GetCoreClockMhz)
        : NominalMhz * 1.6;

    /// <summary>Descobre os núcleos físicos pelo Windows; funciona com núcleos de tamanhos diferentes e com ou sem SMT.</summary>
    private static int[] ReadCoreProcessors(out int withSmt, out int withoutSmt)
    {
        withSmt = withoutSmt = 0;
        const int RelationProcessorCore = 0;
        uint length = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref length);
        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformation(buffer, ref length))
                return FallbackCores();

            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION em x64: máscara (8), relação (4 + 4), união (16).
            const int size = 32;
            var cores = new List<int>();
            for (int offset = 0; offset + size <= length; offset += size)
            {
                if (Marshal.ReadInt32(buffer, offset + 8) != RelationProcessorCore)
                    continue;

                ulong mask = (ulong)Marshal.ReadInt64(buffer, offset);
                if (mask == 0)
                    continue;

                cores.Add(System.Numerics.BitOperations.TrailingZeroCount(mask));
                if (System.Numerics.BitOperations.PopCount(mask) > 1)
                    withSmt++;
                else
                    withoutSmt++;
            }

            cores.Sort();
            return cores.Count > 0 ? cores.ToArray() : FallbackCores();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        static int[] FallbackCores() => Enumerable.Range(0, Environment.ProcessorCount).ToArray();
    }

    public void Dispose()
    {
        _clocks.Dispose();
        _generic?.Dispose();
        _nvidia?.Dispose();
        Tuner?.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint length);
}
