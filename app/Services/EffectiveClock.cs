using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Clock efetivo de cada processador lógico, pelo contador "% Processor Performance" do Windows
/// (o mesmo número que o Gerenciador de Tarefas mostra como velocidade). É o que o núcleo
/// realmente entregou, já descontado o "clock stretching" de uma curva agressiva demais; o
/// clock que o SMU informa é só o pedido.
/// </summary>
public sealed class EffectiveClock : IDisposable
{
    private const uint FormatDouble = 0x200;
    private const int MoreData = unchecked((int)0x800007D2);
    // PDH_FMT_COUNTERVALUE_ITEM_W em x64: ponteiro do nome (8), status (4 + 4 de alinhamento), valor (8).
    private const int ItemSize = 24, ValueOffset = 16;

    private readonly double _nominalMhz;
    private readonly IntPtr _query;
    private readonly IntPtr _counter;

    /// <param name="nominalMhz">Frequência nominal do processador; 100 % do contador equivale a ela.</param>
    public EffectiveClock(double nominalMhz)
    {
        _nominalMhz = nominalMhz;
        // O caminho em inglês funciona em qualquer idioma do Windows.
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0 ||
            PdhAddEnglishCounterW(_query, @"\Processor Information(*)\% Processor Performance", IntPtr.Zero, out _counter) != 0)
        {
            _query = IntPtr.Zero;
            return;
        }

        // O contador é uma taxa: a primeira coleta só marca o ponto de partida.
        PdhCollectQueryData(_query);
    }

    /// <summary>Clock efetivo, em MHz, dos processadores lógicos desde a leitura anterior; vazio se indisponível.</summary>
    public IReadOnlyList<double> Read() => ReadByProcessor().Where(mhz => mhz > 0).ToArray();

    /// <summary>O mesmo, em um vetor indexado pelo número do processador lógico (0 onde não há leitura).</summary>
    public double[] ReadByProcessor()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
            return [];

        uint size = 0;
        if (PdhGetFormattedCounterArrayW(_counter, FormatDouble, ref size, out _, IntPtr.Zero) != MoreData)
            return [];

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, FormatDouble, ref size, out uint count, buffer) != 0)
                return [];

            var clocks = new double[Environment.ProcessorCount];
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buffer + i * ItemSize;
                // O nome é "grupo,processador"; "0,_Total" e "_Total" são médias do próprio Windows.
                string[] name = (Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "").Split(',');
                if (name.Length != 2 || name[0] != "0" || !int.TryParse(name[1], out int processor) || processor >= clocks.Length)
                    continue;

                double percent = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ValueOffset));
                clocks[processor] = _nominalMhz * percent / 100;
            }
            return clocks;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
            PdhCloseQuery(_query);
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}
