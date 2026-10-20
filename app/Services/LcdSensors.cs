using System;
using System.Globalization;
using System.Linq;

namespace Mach1CustomApp.Services;

/// <summary>Os sensores que um elemento do layout personalizado pode mostrar, pelo índice.</summary>
public static class LcdSensors
{
    public static readonly string[] Names =
    [
        "Temperatura do processador",
        "Temperatura da placa de vídeo",
        "Uso do processador",
        "Uso da placa de vídeo",
        "Memória em uso",
        "Memória de vídeo em uso",
        "Consumo da placa de vídeo",
        "Clock do núcleo mais rápido",
        "Hora",
        "Data",
        "Temperatura lá fora",
        "Tempo agora",
        "Umidade lá fora",
    ];

    /// <summary>O valor do sensor já com a unidade, ou "—" quando não há leitura.</summary>
    public static string Format(int sensor, LcdContent content)
    {
        SensorSnapshot s = content.Sensors;
        return sensor switch
        {
            0 => Number(s.CpuTemperature, "0.0", " °C"),
            1 => Number(s.Gpu.Temperature, "0", " °C"),
            2 => Number(s.CpuLoad, "0", " %"),
            3 => Number(s.Gpu.Load, "0", " %"),
            4 => Number(s.RamUsedGb, "0.0", " GB"),
            5 => Number(s.Gpu.MemoryUsedGb, "0.0", " GB"),
            6 => Number(s.Gpu.Watts, "0", " W"),
            7 => content.ClocksMhz.Length > 0 && content.ClocksMhz.Max() > 0 ? $"{content.ClocksMhz.Max():0} MHz" : "—",
            8 => content.Time.ToString("HH:mm"),
            9 => content.Time.ToString("dd/MM"),
            // Clima: só com a previsão ligada na aba da tela.
            10 => content.Weather is { } w ? Number(w.Temperature, "0.0", " °C") : "—",
            11 => content.Weather is { } w ? WeatherCodes.Describe(w.Code) : "—",
            12 => content.Weather?.Humidity is int humidity ? $"{humidity} %" : "—",
            _ => "—",
        };
    }

    /// <summary>Quanto da barra preencher, de 0 a 1; null para sensores sem escala (hora, data, consumo, clock).</summary>
    public static float? Fraction(int sensor, LcdContent content)
    {
        SensorSnapshot s = content.Sensors;
        float? value = sensor switch
        {
            // Temperaturas: de 20 °C até o limite do componente.
            0 => (s.CpuTemperature - 20) / (content.CpuLimit - 20),
            1 => (s.Gpu.Temperature - 20) / 70,
            2 => s.CpuLoad / 100,
            3 => s.Gpu.Load / 100,
            4 => s.RamUsedGb / s.RamTotalGb,
            5 => s.Gpu.MemoryUsedGb / s.Gpu.MemoryTotalGb,
            12 => content.Weather?.Humidity / 100f,
            _ => null,
        };
        return value is float v && !float.IsNaN(v) ? Math.Clamp(v, 0, 1) : null;
    }

    private static string Number(float? value, string format, string unit) =>
        value is float v ? v.ToString(format, CultureInfo.InvariantCulture) + unit : "—";
}
