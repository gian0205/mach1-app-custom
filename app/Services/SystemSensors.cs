using System.Runtime.InteropServices;

namespace Mach1CustomApp.Services;

/// <summary>Uma leitura de todos os sensores mostrados na janela e na tela.</summary>
public sealed record SensorSnapshot(
    float? CpuTemperature,
    float? CpuLoad,
    float? RamUsedGb,
    float? RamTotalGb,
    GpuReading Gpu);

/// <summary>Uso do processador e da memória, pelo próprio Windows.</summary>
public sealed class SystemSensors
{
    private const float Gigabyte = 1024f * 1024f * 1024f;

    private long _lastIdle, _lastTotal;

    /// <summary>Uso do processador, em %, desde a chamada anterior; null na primeira.</summary>
    public float? ReadCpuLoad()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
            return null;

        // O tempo de kernel já inclui o ocioso.
        long total = kernel + user;
        long idleDelta = idle - _lastIdle, totalDelta = total - _lastTotal;
        bool first = _lastTotal == 0;
        _lastIdle = idle;
        _lastTotal = total;

        if (first || totalDelta <= 0)
            return null;

        return 100f * (1f - (float)idleDelta / totalDelta);
    }

    public (float? UsedGb, float? TotalGb) ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
            return (null, null);

        return ((status.TotalPhysical - status.AvailablePhysical) / Gigabyte, status.TotalPhysical / Gigabyte);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
