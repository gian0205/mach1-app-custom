using Mach1CustomApp.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mach1CustomApp.Services;

/// <param name="Seconds">Duração da etapa de 1 núcleo e da de todos os núcleos.</param>
/// <param name="PerCore">Testar também cada núcleo sozinho.</param>
/// <param name="Memory">Medir também a cópia de memória.</param>
public sealed record BenchmarkOptions(string Name, int Seconds, bool PerCore, bool Memory);

public readonly record struct BenchmarkProgress(double Fraction, string Status);

/// <summary>
/// Benchmark próprio, para comparar o mesmo PC antes e depois de um ajuste. Cada thread repete
/// um laço com três cargas (inteiros, ponto flutuante e ordenação); os pontos são laços por
/// segundo numa escala própria, que não se compara com Cinebench ou Geekbench. Enquanto roda,
/// os sensores são lidos duas vezes por segundo.
/// </summary>
public static class BenchmarkRunner
{
    // Laços por segundo que valem 1000 pontos: um núcleo do 9800X3D de fábrica fica perto disso.
    private const double ReferenceRate = 300;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PauseBetweenPhases = TimeSpan.FromSeconds(2);

    public static BenchmarkRun Run(BenchmarkOptions options, Machine machine,
        IProgress<BenchmarkProgress>? progress, CancellationToken cancellation)
    {
        var duration = TimeSpan.FromSeconds(options.Seconds);
        // Núcleo a núcleo usa etapas mais curtas, senão o teste fica longo demais.
        var coreDuration = TimeSpan.FromSeconds(Math.Max(2, options.Seconds / 4.0));
        var memoryDuration = TimeSpan.FromSeconds(Math.Max(3, options.Seconds / 2.0));

        var phases = new List<(string Phase, TimeSpan Duration)> { (BenchmarkPhase.Single, duration), (BenchmarkPhase.Multi, duration) };
        if (options.PerCore)
            phases.Add((BenchmarkPhase.Cores, coreDuration * machine.CoreCount));
        if (options.Memory)
            phases.Add((BenchmarkPhase.Memory, memoryDuration));
        double totalSeconds = phases.Sum(p => p.Duration.TotalSeconds) + PauseBetweenPhases.TotalSeconds * phases.Count;

        var run = new BenchmarkRun
        {
            Name = options.Name,
            CpuName = machine.CpuName,
            Seconds = options.Seconds,
            // Sem ajuste de curva (processador que não é Ryzen suportado) a lista fica vazia.
            Margins = machine.Tuner is { } tuner ? Enumerable.Range(0, tuner.CoreCount).Select(tuner.GetCoreMargin).ToArray() : [],
        };

        var clock = Stopwatch.StartNew();
        string phase = BenchmarkPhase.Pause;
        bool finished = false;

        var sampler = new Thread(() =>
        {
            using var effectiveClock = new EffectiveClock(machine.NominalMhz);
            double requestedPeak = 0;
            while (!Volatile.Read(ref finished))
            {
                string current = Volatile.Read(ref phase);
                run.Samples.Add(Sample(machine, effectiveClock, ref requestedPeak, clock.Elapsed.TotalSeconds, current));
                progress?.Report(new BenchmarkProgress(Math.Min(1, clock.Elapsed.TotalSeconds / totalSeconds),
                    current == BenchmarkPhase.Pause ? "Pausa entre as etapas…" : $"Etapa: {BenchmarkPhase.Label(current)}…"));
                Thread.Sleep(SampleInterval);
            }
        })
        { IsBackground = true, Name = "Sensores do benchmark", Priority = ThreadPriority.AboveNormal };
        sampler.Start();

        try
        {
            int logical = machine.LogicalCount;

            void Enter(string next)
            {
                Volatile.Write(ref phase, BenchmarkPhase.Pause);
                cancellation.WaitHandle.WaitOne(PauseBetweenPhases);
                cancellation.ThrowIfCancellationRequested();
                Volatile.Write(ref phase, next);
            }

            Enter(BenchmarkPhase.Single);
            run.SingleScore = Score(Load([null], duration, cancellation));

            Enter(BenchmarkPhase.Multi);
            run.MultiScore = Score(Load(Enumerable.Range(0, logical).Select(i => (int?)i).ToArray(), duration, cancellation));

            if (options.PerCore)
            {
                Enter(BenchmarkPhase.Cores);
                run.CoreScores = new double[machine.CoreCount];
                for (int core = 0; core < machine.CoreCount; core++)
                    run.CoreScores[core] = Score(Load([machine.CoreProcessors[core]], coreDuration, cancellation));
            }

            if (options.Memory)
            {
                Enter(BenchmarkPhase.Memory);
                run.MemoryGbps = MemoryCopy(memoryDuration, cancellation);
            }

            cancellation.ThrowIfCancellationRequested();
            return run;
        }
        finally
        {
            Volatile.Write(ref finished, true);
            sampler.Join();
        }
    }

    private static double Score(double loopsPerSecond) => 1000 * loopsPerSecond / ReferenceRate;

    private static BenchmarkSample Sample(Machine machine, EffectiveClock effectiveClock, ref double requestedPeak, double time, string phase)
    {
        // O clock efetivo não passa do clock pedido; leituras acima disso são falhas do contador
        // do Windows (acontecem quando uma thread troca de núcleo) e ficam de fora.
        // O clock pedido oscila; vale o maior já visto neste teste.
        requestedPeak = Math.Max(requestedPeak, machine.ClockCeiling());
        double ceiling = 1.03 * requestedPeak;
        double[] clocks = effectiveClock.Read().Where(mhz => ceiling <= 0 || mhz <= ceiling).ToArray();

        return new BenchmarkSample
        {
            Time = Math.Round(time, 2),
            Phase = phase,
            Temperature = machine.ReadCpuTemperature(),
            ClockMax = clocks.Length > 0 ? Math.Round(clocks.Max()) : 0,
            ClockAvg = clocks.Length > 0 ? Math.Round(clocks.Average()) : 0,
            Watts = machine.ReadCorePower(),
        };
    }

    /// <summary>
    /// Roda a carga em uma thread por item de <paramref name="affinities"/> (o processador lógico
    /// em que a thread fica presa, ou null para deixar o Windows escolher) e devolve a soma dos
    /// laços por segundo.
    /// </summary>
    private static double Load(int?[] affinities, TimeSpan duration, CancellationToken cancellation)
    {
        double total = 0;
        object gate = new();
        using var start = new ManualResetEventSlim(false);

        Thread[] threads = affinities.Select(affinity => new Thread(() =>
        {
            if (affinity is int processor)
                SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << processor));

            var workload = new Workload();
            start.Wait();

            var clock = Stopwatch.StartNew();
            long loops = 0;
            while (clock.Elapsed < duration && !cancellation.IsCancellationRequested)
            {
                workload.Loop();
                loops++;
            }

            double rate = loops / clock.Elapsed.TotalSeconds;
            lock (gate)
                total += rate;
        })
        { IsBackground = true, Name = "Carga do benchmark" }).ToArray();

        foreach (Thread thread in threads)
            thread.Start();
        start.Set();
        foreach (Thread thread in threads)
            thread.Join();

        cancellation.ThrowIfCancellationRequested();
        return total;
    }

    /// <summary>Copia um bloco de 128 MB de um lado para o outro e devolve os GB/s.</summary>
    private static double MemoryCopy(TimeSpan duration, CancellationToken cancellation)
    {
        const int size = 128 * 1024 * 1024;
        byte[] from = GC.AllocateUninitializedArray<byte>(size);
        byte[] to = GC.AllocateUninitializedArray<byte>(size);
        // Toca as páginas antes de medir, para o tempo não incluir a alocação pelo Windows.
        Array.Fill(from, (byte)1);
        Array.Fill(to, (byte)2);

        var clock = Stopwatch.StartNew();
        long copied = 0;
        while (clock.Elapsed < duration && !cancellation.IsCancellationRequested)
        {
            Buffer.BlockCopy(from, 0, to, 0, size);
            (from, to) = (to, from);
            copied += size;
        }

        return copied / clock.Elapsed.TotalSeconds / 1e9;
    }

    /// <summary>Um laço da carga, com o estado de uma thread. Leva poucos milissegundos.</summary>
    private sealed class Workload
    {
        private const int SortLength = 32 * 1024;

        private readonly int[] _template = new int[SortLength];
        private readonly int[] _scratch = new int[SortLength];
        private ulong _sink;

        public Workload()
        {
            var random = new Random(12345);
            for (int i = 0; i < _template.Length; i++)
                _template[i] = random.Next();
        }

        public void Loop()
        {
            _sink ^= Integers();
            _sink ^= FloatingPoint();
            _sink ^= Sorting();
        }

        // Aritmética inteira encadeada: multiplicações, rotações e XOR.
        private static ulong Integers()
        {
            ulong x = 0x9E3779B97F4A7C15, hash = 0;
            for (int i = 0; i < 1_500_000; i++)
            {
                x = x * 6364136223846793005UL + 1442695040888963407UL;
                hash = BitOperations.RotateLeft(hash ^ x, 13) * 0xFF51AFD7ED558CCDUL;
            }
            return hash;
        }

        // Conjunto de Mandelbrot em uma grade de 96x96: multiplicações e somas em double.
        private static ulong FloatingPoint()
        {
            const int grid = 96, limit = 160;
            ulong total = 0;
            for (int py = 0; py < grid; py++)
            {
                double cy = -1.2 + 2.4 * py / grid;
                for (int px = 0; px < grid; px++)
                {
                    double cx = -2.1 + 3.0 * px / grid, x = 0, y = 0;
                    int n = 0;
                    while (n < limit && x * x + y * y <= 4)
                    {
                        double next = x * x - y * y + cx;
                        y = 2 * x * y + cy;
                        x = next;
                        n++;
                    }
                    total += (ulong)n;
                }
            }
            return total;
        }

        // Ordena 32 mil inteiros embaralhados: desvios difíceis de prever e acesso à memória.
        private ulong Sorting()
        {
            _template.CopyTo(_scratch, 0);
            Array.Sort(_scratch);
            return (ulong)_scratch[SortLength / 2];
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
}
