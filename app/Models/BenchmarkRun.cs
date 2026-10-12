using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mach1DriverCustom.Models;

/// <summary>Uma leitura dos sensores durante o benchmark.</summary>
public sealed class BenchmarkSample
{
    /// <summary>Segundos desde o início do teste.</summary>
    public double Time { get; set; }

    /// <summary>Etapa em andamento; uma das constantes de <see cref="BenchmarkPhase"/>.</summary>
    public string Phase { get; set; } = "";

    public float? Temperature { get; set; }

    /// <summary>Clock efetivo do núcleo mais rápido, em MHz.</summary>
    public double ClockMax { get; set; }

    /// <summary>Clock efetivo médio de todos os núcleos, em MHz.</summary>
    public double ClockAvg { get; set; }

    /// <summary>Potência dos núcleos, em watts; null quando a telemetria não informa.</summary>
    public float? Watts { get; set; }
}

public static class BenchmarkPhase
{
    public const string Single = "single";
    public const string Multi = "multi";
    public const string Cores = "cores";
    public const string Memory = "memory";
    public const string Pause = "pause";

    public static string Label(string phase) => phase switch
    {
        Single => "1 núcleo",
        Multi => "todos os núcleos",
        Cores => "núcleo a núcleo",
        Memory => "memória",
        _ => "",
    };
}

/// <summary>Resultado de um benchmark, como fica gravado em disco.</summary>
public sealed class BenchmarkRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public DateTime Time { get; set; } = DateTime.Now;

    public string CpuName { get; set; } = "";

    /// <summary>Margem do Curve Optimizer em vigor em cada núcleo durante o teste.</summary>
    public int?[] Margins { get; set; } = [];

    /// <summary>Duração pedida para cada etapa, em segundos.</summary>
    public int Seconds { get; set; }

    /// <summary>Pontos com uma thread só, no núcleo que o Windows escolher.</summary>
    public double SingleScore { get; set; }

    /// <summary>Pontos com uma thread em cada processador lógico.</summary>
    public double MultiScore { get; set; }

    /// <summary>Pontos de cada núcleo testado sozinho; vazio se a etapa não rodou.</summary>
    public double[] CoreScores { get; set; } = [];

    /// <summary>Cópia de memória, em GB/s; 0 se a etapa não rodou.</summary>
    public double MemoryGbps { get; set; }

    public List<BenchmarkSample> Samples { get; set; } = [];

    [JsonIgnore]
    public float? TemperatureMax => Samples.Max(s => s.Temperature);

    /// <summary>Maior clock efetivo visto na etapa de 1 núcleo.</summary>
    [JsonIgnore]
    public double SingleClock => In(BenchmarkPhase.Single).Select(s => s.ClockMax).DefaultIfEmpty().Max();

    /// <summary>Clock efetivo médio de todos os núcleos na etapa de todos os núcleos.</summary>
    [JsonIgnore]
    public double MultiClock => In(BenchmarkPhase.Multi).Select(s => s.ClockAvg).DefaultIfEmpty().Average();

    [JsonIgnore]
    public float? MultiWatts
    {
        get
        {
            float[] watts = In(BenchmarkPhase.Multi).Where(s => s.Watts is not null).Select(s => s.Watts!.Value).ToArray();
            return watts.Length > 0 ? watts.Average() : null;
        }
    }

    // As duas primeiras leituras de cada etapa ainda pegam a subida do clock.
    private IEnumerable<BenchmarkSample> In(string phase) => Samples.Where(s => s.Phase == phase).Skip(2);
}
