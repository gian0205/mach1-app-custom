using System;
using System.Runtime.InteropServices;

namespace Mach1CustomApp.Services;

/// <summary>Leitura da placa de vídeo; cada campo é null quando o driver não informa.</summary>
public readonly record struct GpuReading(float? Temperature, float? Load, float? MemoryUsedGb, float? MemoryTotalGb, float? Watts);

/// <summary>
/// Sensores da placa de vídeo NVIDIA pela NVML, a biblioteca de monitoramento que vem com o
/// driver. A NVML só informa a temperatura em graus inteiros.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;
    private const float Gigabyte = 1024f * 1024f * 1024f;

    private readonly IntPtr _device;

    private GpuMonitor(IntPtr device) => _device = device;

    /// <summary>Devolve null quando não há placa NVIDIA ou o driver não tem a NVML.</summary>
    public static GpuMonitor? TryCreate()
    {
        try
        {
            if (nvmlInit_v2() != Success)
                return null;

            if (nvmlDeviceGetHandleByIndex_v2(0, out IntPtr device) != Success)
            {
                nvmlShutdown();
                return null;
            }

            return new GpuMonitor(device);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    public GpuReading Read()
    {
        float? temperature = nvmlDeviceGetTemperature(_device, TemperatureGpu, out uint celsius) == Success ? celsius : null;
        float? load = nvmlDeviceGetUtilizationRates(_device, out Utilization utilization) == Success ? utilization.Gpu : null;
        float? watts = nvmlDeviceGetPowerUsage(_device, out uint milliwatts) == Success ? milliwatts / 1000f : null;

        float? used = null, total = null;
        if (nvmlDeviceGetMemoryInfo(_device, out Memory memory) == Success)
        {
            used = memory.Used / Gigabyte;
            total = memory.Total / Gigabyte;
        }

        return new GpuReading(temperature, load, used, total, watts);
    }

    public void Dispose() => nvmlShutdown();

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll")]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temperature);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);

    [DllImport("nvml.dll")]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
}
