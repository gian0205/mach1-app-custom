using LibreHardwareMonitor.Hardware;
using System;
using System.Linq;

namespace Mach1CustomApp.Services;

/// <summary>
/// Sensores pela LibreHardwareMonitor, para o que o caminho principal não cobre: temperatura de
/// processadores que não são Ryzen com SMU conhecido (Intel, por exemplo) e placas de vídeo que
/// não são NVIDIA. A temperatura do processador por aqui também depende do driver PawnIO.
/// </summary>
public sealed class GenericSensors : IDisposable
{
    private const float Megabyte = 1024f;

    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;

    public GenericSensors(bool cpu, bool gpu)
    {
        _computer = new Computer { IsCpuEnabled = cpu, IsGpuEnabled = gpu };
        _computer.Open();

        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Placa dedicada antes da integrada.
        _gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
            ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
            ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
    }

    public bool HasGpu => _gpu is not null;

    public float? ReadCpuTemperature()
    {
        if (_cpu is null)
            return null;

        _cpu.Update();
        ISensor[] temperatures = _cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is > 0).ToArray();
        // A leitura do encapsulamento quando existe; senão, o núcleo mais quente.
        ISensor? package = temperatures.FirstOrDefault(s =>
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));
        return package?.Value ?? (temperatures.Length > 0 ? temperatures.Max(s => s.Value) : null);
    }

    public GpuReading ReadGpu()
    {
        if (_gpu is null)
            return default;

        _gpu.Update();
        float? Find(SensorType type, params string[] names) => names
            .Select(name => _gpu.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase))?.Value)
            .FirstOrDefault(value => value is not null);

        float? used = Find(SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
        float? total = Find(SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
        return new GpuReading(
            Find(SensorType.Temperature, "GPU Core", "GPU"),
            Find(SensorType.Load, "GPU Core", "D3D 3D", "GPU"),
            used / Megabyte,
            total / Megabyte,
            Find(SensorType.Power, "GPU Package", "GPU Power", "GPU"));
    }

    public void Dispose() => _computer.Close();
}
