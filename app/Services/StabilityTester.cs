using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mach1CustomApp.Services;

/// <summary>
/// Teste rápido da curva, um núcleo por vez: uma thread presa ao núcleo repete o mesmo cálculo
/// e confere se o resultado é sempre igual. Com um núcleo só em carga o clock vai ao máximo,
/// que é onde uma margem agressiva demais costuma errar. É uma triagem; não substitui horas de
/// uso ou um teste longo como CoreCycler ou OCCT.
/// </summary>
public static class StabilityTester
{
    private static string FlagPath => Path.Combine(ProfileStore.Directory, "testing.flag");

    /// <summary>Roda o cálculo no processador lógico indicado; false se algum resultado divergiu.</summary>
    public static bool TestCore(int logicalProcessor, TimeSpan duration, Action<double> progress, CancellationToken cancellation)
    {
        bool consistent = true;
        var thread = new Thread(() =>
        {
            SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)(1UL << logicalProcessor));

            var clock = Stopwatch.StartNew();
            ulong reference = Chunk();
            while (clock.Elapsed < duration && !cancellation.IsCancellationRequested)
            {
                if (Chunk() != reference)
                {
                    consistent = false;
                    return;
                }
                progress(clock.Elapsed / duration);
            }
        })
        { IsBackground = true, Name = "Teste de estabilidade" };

        thread.Start();
        thread.Join();
        return consistent;
    }

    /// <summary>
    /// Anota em disco qual núcleo está em teste. Se o PC cair no meio, o arquivo sobrevive e o
    /// app avisa na próxima abertura.
    /// </summary>
    public static void MarkTesting(int core)
    {
        Directory.CreateDirectory(ProfileStore.Directory);
        File.WriteAllText(FlagPath, core.ToString());
    }

    public static void ClearMark() => File.Delete(FlagPath);

    /// <summary>Núcleo que estava em teste quando o app foi interrompido, ou null.</summary>
    public static int? TakeInterruptedCore()
    {
        if (!File.Exists(FlagPath))
            return null;

        int? core = int.TryParse(File.ReadAllText(FlagPath), out int value) ? value : null;
        File.Delete(FlagPath);
        return core;
    }

    // Cerca de 100 ms de inteiros, raiz quadrada e FMA. Determinístico: qualquer diferença entre
    // duas execuções é erro de cálculo do núcleo.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong Chunk()
    {
        ulong x = 0x9E3779B97F4A7C15, hash = 0;
        double d = 1.0;
        for (int i = 0; i < 6_000_000; i++)
        {
            x = x * 6364136223846793005UL + 1442695040888963407UL;
            d = Math.Sqrt(d * 1.000001 + (x >> 44));
            double f = Math.FusedMultiplyAdd(d, 1.0000003, (x & 0xFFFF) * 0.5);
            hash = BitOperations.RotateLeft(hash, 7) ^ BitConverter.DoubleToUInt64Bits(f) ^ x;
        }
        return hash;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
}
