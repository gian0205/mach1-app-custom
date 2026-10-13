using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mach1CustomApp.Services;

/// <summary>Os últimos minutos de cada sensor, do mais antigo para o mais novo, uma amostra por segundo.</summary>
/// <param name="Ram">Memória em uso, em % do total.</param>
public sealed record LcdHistory(float?[] CpuTemp, float?[] GpuTemp, float?[] CpuLoad, float?[] GpuLoad, float?[] Ram, float?[] GpuWatts)
{
    /// <summary>Amostras guardadas: três minutos.</summary>
    public const int Capacity = 180;

    public static LcdHistory Empty { get; } = new([], [], [], [], [], []);
}

/// <summary>Junta as leituras de cada segundo no histórico que as páginas com gráfico desenham.</summary>
public sealed class LcdHistoryBuffer
{
    private readonly List<SensorSnapshot> _samples = [];

    public LcdHistory Add(SensorSnapshot sample)
    {
        _samples.Add(sample);
        if (_samples.Count > LcdHistory.Capacity)
            _samples.RemoveAt(0);

        float?[] Series(Func<SensorSnapshot, float?> pick)
        {
            var values = new float?[_samples.Count];
            for (int i = 0; i < values.Length; i++)
                values[i] = pick(_samples[i]);
            return values;
        }

        return new LcdHistory(
            Series(s => s.CpuTemperature), Series(s => s.Gpu.Temperature),
            Series(s => s.CpuLoad), Series(s => s.Gpu.Load),
            Series(s => s.RamUsedGb / s.RamTotalGb * 100), Series(s => s.Gpu.Watts));
    }
}

// Páginas de estilo: gráfico, medidores, relógio, minimalista e blocos. Todas são desenhadas em
// unidades em que o lado menor da tela vale 720, e se arrumam conforme a tela é em pé ou deitada.
public sealed partial class LcdRenderer
{
    private const float Margin = 56;

    private readonly SKPaint _stroke = new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };

    private SKColor _background;

    private void DrawStylePage(SKCanvas canvas, LcdContent content, LcdPage page)
    {
        float width = LcdScreen.Width, height = LcdScreen.Height;
        bool wide = width / height >= 1.25f;
        float scale = wide ? height / 720f : width / 720f;
        float w = width / scale, h = height / scale;
        LcdHistory history = content.History ?? LcdHistory.Empty;

        canvas.Save();
        canvas.Scale(scale);
        switch (page)
        {
            case LcdPage.Chart: DrawChartPage(canvas, content, history, w, h, wide); break;
            case LcdPage.Gauges: DrawGaugesPage(canvas, content, w, h); break;
            case LcdPage.Clock: DrawClockPage(canvas, content, w, h); break;
            case LcdPage.Minimal: DrawMinimalPage(canvas, content, history, w, h); break;
            default: DrawTilesPage(canvas, content, history, w, h, wide); break;
        }
        canvas.Restore();
    }

    // ---------- Gráfico ----------

    private void DrawChartPage(SKCanvas canvas, LcdContent content, LcdHistory history, float w, float h, bool wide)
    {
        SensorSnapshot sensors = content.Sensors;
        float right = w - Margin;
        DrawHeader(canvas, content, right);

        // O processador vai na cor de destaque (ou de aviso); a placa de vídeo, numa cor mais perto do texto.
        SKColor cpuColor = TemperatureColor(sensors.CpuTemperature, _temps.CpuWarm, _temps.CpuHot);
        SKColor gpuColor = Blend(_accent, _text, 0.72f);
        // Em tema de uma cor só (destaque quase igual ao texto) as duas linhas se confundiriam: a da placa vai apagada.
        if (Math.Abs(gpuColor.Red - _accent.Red) + Math.Abs(gpuColor.Green - _accent.Green) + Math.Abs(gpuColor.Blue - _accent.Blue) < 90)
            gpuColor = _muted;
        float half = (right - Margin) / 2;
        DrawLegendValue(canvas, Margin, 150, "Processador", FormatTemperature(sensors.CpuTemperature, 1) + " °C", cpuColor);
        DrawLegendValue(canvas, Margin + half + 10, 150, "Placa de vídeo", FormatTemperature(sensors.Gpu.Temperature, 0) + " °C", gpuColor);

        float top = 372, bottom = h - 70;
        SKRect temps, loads;
        // Sem altura para empilhar (tela deitada ou quadrada), os dois gráficos ficam lado a lado.
        if (wide || h < 1000)
        {
            float split = Margin + (right - Margin) * 0.58f;
            temps = new SKRect(Margin, top, split - 30, bottom);
            loads = new SKRect(split + 30, top, right, bottom);
        }
        else
        {
            const float gap = 110;
            float middle = top + (bottom - top - gap) * 0.6f;
            temps = new SKRect(Margin, top, right, middle);
            loads = new SKRect(Margin, middle + gap, right, bottom);
        }

        (float min, float max) = TemperatureRange(history);
        DrawText(canvas, "Temperatura, em °C", temps.Left, temps.Top - 20, 26, _muted, _regular);
        DrawChartFrame(canvas, temps, min, max, max - min > 60 ? 20 : 10);
        DrawSeries(canvas, temps, history.GpuTemp, min, max, gpuColor, 4, fill: false);
        DrawSeries(canvas, temps, history.CpuTemp, min, max, cpuColor, 5, fill: true);

        DrawText(canvas, "Uso, em %", loads.Left, loads.Top - 20, 26, _muted, _regular);
        DrawChartFrame(canvas, loads, 0, 100, 50);
        DrawSeries(canvas, loads, history.GpuLoad, 0, 100, gpuColor, 4, fill: false);
        DrawSeries(canvas, loads, history.CpuLoad, 0, 100, _accent, 5, fill: true);
    }

    private void DrawLegendValue(SKCanvas canvas, float x, float top, string label, string value, SKColor color)
    {
        // A pastilha na cor da linha liga o número à série do gráfico.
        _paint.Color = color;
        canvas.DrawRoundRect(x, top + 14, 26, 8, 4, 4, _paint);
        DrawText(canvas, label, x + 38, top + 28, 28, _muted, _regular);
        DrawText(canvas, value, x - 3, top + 128, 88, color, _light);
    }

    /// <summary>Faixa do eixo de temperatura: múltiplos de 10 em volta do que foi medido, com pelo menos 30 °C de altura.</summary>
    private static (float Min, float Max) TemperatureRange(LcdHistory history)
    {
        float low = float.MaxValue, high = float.MinValue;
        foreach (float?[] series in new[] { history.CpuTemp, history.GpuTemp })
        {
            foreach (float? value in series)
            {
                if (value is not float v || float.IsNaN(v))
                    continue;
                low = Math.Min(low, v);
                high = Math.Max(high, v);
            }
        }

        if (low > high)
            return (30, 60);

        float min = MathF.Floor((low - 4) / 10) * 10, max = MathF.Ceiling((high + 4) / 10) * 10;
        while (max - min < 30)
        {
            max += 10;
            if (max - min < 30)
                min -= 10;
        }
        return (min, max);
    }

    /// <summary>Linhas de grade com o valor de cada uma e, embaixo, de quando são as pontas do gráfico.</summary>
    private void DrawChartFrame(SKCanvas canvas, SKRect area, float min, float max, float step)
    {
        _stroke.Color = _track;
        _stroke.StrokeWidth = 2;
        for (float value = min; value <= max + 0.01f; value += step)
        {
            float y = area.Bottom - area.Height * (value - min) / (max - min);
            canvas.DrawLine(area.Left, y, area.Right, y, _stroke);
            DrawText(canvas, value.ToString("0", CultureInfo.InvariantCulture), area.Right, y - 7, 20, _muted, _regular, SKTextAlign.Right);
        }

        DrawText(canvas, "3 min atrás", area.Left, area.Bottom + 30, 20, _muted, _regular);
        DrawText(canvas, "agora", area.Right, area.Bottom + 30, 20, _muted, _regular, SKTextAlign.Right);
    }

    /// <summary>
    /// Uma série como linha, com a amostra mais nova encostada na direita. O gráfico só ocupa a
    /// largura toda depois de três minutos de leituras.
    /// </summary>
    private void DrawSeries(SKCanvas canvas, SKRect area, float?[] values, float min, float max, SKColor color, float thickness, bool fill)
    {
        float step = area.Width / (LcdHistory.Capacity - 1);
        using var path = new SKPath();
        bool open = false;
        int points = 0;
        float firstX = 0, lastX = 0, lastY = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is not float value || float.IsNaN(value))
            {
                open = false;
                continue;
            }

            float x = area.Right - (values.Length - 1 - i) * step;
            float y = area.Bottom - area.Height * Math.Clamp((value - min) / (max - min), 0, 1);
            if (open)
                path.LineTo(x, y);
            else
                path.MoveTo(x, y);

            if (points++ == 0)
                firstX = x;
            open = true;
            lastX = x;
            lastY = y;
        }

        if (points == 0)
            return;

        if (fill && points > 1)
        {
            using var below = new SKPath(path);
            below.LineTo(lastX, area.Bottom);
            below.LineTo(firstX, area.Bottom);
            below.Close();
            using SKShader fade = SKShader.CreateLinearGradient(new SKPoint(0, area.Top), new SKPoint(0, area.Bottom),
                [color.WithAlpha(_neon ? (byte)70 : (byte)95), color.WithAlpha(0)], SKShaderTileMode.Clamp);
            _paint.Shader = fade;
            canvas.DrawPath(below, _paint);
            _paint.Shader = null;
        }

        StrokePath(canvas, path, color, thickness);

        // A ponta marca a leitura de agora.
        _paint.Color = color;
        canvas.DrawCircle(lastX, lastY, thickness * 1.3f, _paint);
    }

    /// <summary>Traço de linha ou arco; em tema neon, com halo e miolo claro, como as barras.</summary>
    private void StrokePath(SKCanvas canvas, SKPath path, SKColor color, float thickness)
    {
        if (_neon)
        {
            using SKMaskFilter glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, thickness * 1.5f);
            _stroke.MaskFilter = glow;
            _stroke.Color = color.WithAlpha(190);
            _stroke.StrokeWidth = thickness * 2.2f;
            canvas.DrawPath(path, _stroke);
            _stroke.MaskFilter = null;
        }

        _stroke.Color = color;
        _stroke.StrokeWidth = thickness;
        canvas.DrawPath(path, _stroke);

        if (_neon)
        {
            _stroke.Color = Blend(color, SKColors.White, 0.8f);
            _stroke.StrokeWidth = thickness * 0.36f;
            canvas.DrawPath(path, _stroke);
        }
    }

    // ---------- Medidores ----------

    private void DrawGaugesPage(SKCanvas canvas, LcdContent content, float w, float h)
    {
        SensorSnapshot sensors = content.Sensors;
        GpuReading gpu = sensors.Gpu;
        DrawHeader(canvas, content, w - Margin);

        SKColor cpuColor = TemperatureColor(sensors.CpuTemperature, _temps.CpuWarm, _temps.CpuHot);
        SKColor gpuColor = TemperatureColor(gpu.Temperature, _temps.GpuWarm, _temps.GpuHot);
        float? cpuFraction = sensors.CpuTemperature is float c ? (c - 20) / (content.CpuLimit - 20) : null;
        float? gpuFraction = gpu.Temperature is float g ? (g - 20) / (GpuMax - 20) : null;

        float smallY, smallRadius;
        if (h >= 1100)
        {
            // Em pé: os dois medidores grandes um embaixo do outro.
            DrawGauge(canvas, w / 2, 372, 196, 26, cpuFraction, cpuColor, FormatTemperature(sensors.CpuTemperature, 1), "Processador, °C");
            DrawGauge(canvas, w / 2, 800, 196, 26, gpuFraction, gpuColor, FormatTemperature(gpu.Temperature, 0), "Placa de vídeo, °C");
            smallY = h - 150;
            smallRadius = 84;
        }
        else
        {
            float radius = Math.Min(Math.Min(170, (h - 330) / 2.2f), w * 0.2f);
            float y = 150 + radius;
            DrawGauge(canvas, w * 0.28f, y, radius, 22, cpuFraction, cpuColor, FormatTemperature(sensors.CpuTemperature, 1), "Processador, °C");
            DrawGauge(canvas, w * 0.72f, y, radius, 22, gpuFraction, gpuColor, FormatTemperature(gpu.Temperature, 0), "Placa de vídeo, °C");
            smallRadius = Math.Min(64, (h - y - radius - 40) / 2.1f);
            smallY = h - 40 - smallRadius;
        }

        float? ram = sensors.RamUsedGb / sensors.RamTotalGb;
        DrawGauge(canvas, w * 0.19f, smallY, smallRadius, 13, sensors.CpuLoad / 100, _accent, Number(sensors.CpuLoad), "Uso CPU, %");
        DrawGauge(canvas, w * 0.5f, smallY, smallRadius, 13, gpu.Load / 100, _accent, Number(gpu.Load), "Uso GPU, %");
        DrawGauge(canvas, w * 0.81f, smallY, smallRadius, 13, ram, _accent, Number(ram * 100), "Memória, %");
    }

    private static string Number(float? value) => value is float v && !float.IsNaN(v) ? v.ToString("0", CultureInfo.InvariantCulture) : "—";

    /// <summary>Medidor em arco de 270°, com o valor no meio e o rótulo na abertura de baixo.</summary>
    private void DrawGauge(SKCanvas canvas, float cx, float cy, float radius, float thickness, float? fraction,
        SKColor color, string value, string label)
    {
        var box = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        using (var track = new SKPath())
        {
            track.AddArc(box, 135, 270);
            _stroke.Color = _track;
            _stroke.StrokeWidth = thickness;
            canvas.DrawPath(track, _stroke);
        }

        if (fraction is float f && !float.IsNaN(f))
        {
            using var arc = new SKPath();
            arc.AddArc(box, 135, 270 * Math.Clamp(f, 0.01f, 1));
            StrokePath(canvas, arc, color, thickness);
        }

        float size = radius * (value.Length > 3 ? 0.62f : 0.74f);
        DrawText(canvas, value, cx, cy + size * 0.34f, size, color, _light, SKTextAlign.Center);
        DrawText(canvas, label, cx, cy + radius * 0.86f, Math.Max(18, radius * 0.15f), _muted, _regular, SKTextAlign.Center);
    }

    // ---------- Relógio ----------

    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    private void DrawClockPage(SKCanvas canvas, LcdContent content, float w, float h)
    {
        SensorSnapshot sensors = content.Sensors;
        float block = 300;                      // altura reservada às temperaturas, embaixo
        float free = h - block;
        float size = Math.Min(w * 0.37f, free * 0.5f);
        float baseline = free * 0.5f + size * 0.3f;

        // A hora na cor do texto: aqui ela é o assunto, e a cor de destaque fica para as temperaturas.
        DrawText(canvas, content.Time.ToString("HH:mm"), w / 2, baseline, size, _text, _light, SKTextAlign.Center);
        string date = content.Time.ToString("dddd, d 'de' MMMM", Portuguese);
        DrawText(canvas, date, w / 2, baseline + Math.Max(54, size * 0.26f), Math.Max(30, size * 0.15f), _muted, _regular, SKTextAlign.Center);

        float top = h - block, half = (w - 2 * Margin - 40) / 2;
        DrawClockTemperature(canvas, Margin, top, half, "Processador", sensors.CpuTemperature, 1,
            _temps.CpuWarm, _temps.CpuHot, content.CpuLimit);
        DrawClockTemperature(canvas, Margin + half + 40, top, half, "Placa de vídeo", sensors.Gpu.Temperature, 0,
            _temps.GpuWarm, _temps.GpuHot, GpuMax);
    }

    private void DrawClockTemperature(SKCanvas canvas, float x, float top, float width, string label, float? temperature,
        int decimals, float warm, float hot, float max)
    {
        SKColor color = TemperatureColor(temperature, warm, hot);
        DrawText(canvas, label, x, top + 34, 28, _muted, _regular);
        DrawText(canvas, FormatTemperature(temperature, decimals) + " °C", x - 3, top + 146, 84, color, _light);
        float? fraction = temperature is float t ? Math.Max(0.02f, (t - 20) / (max - 20)) : null;
        DrawBar(canvas, x, top + 186, width, 12, fraction, color);
    }

    // ---------- Minimalista ----------

    private void DrawMinimalPage(SKCanvas canvas, LcdContent content, LcdHistory history, float w, float h)
    {
        SensorSnapshot sensors = content.Sensors;
        SKColor color = TemperatureColor(sensors.CpuTemperature, _temps.CpuWarm, _temps.CpuHot);

        float chart = Math.Min(260, h * 0.26f);  // faixa do gráfico, embaixo
        float free = h - chart - 60;
        float size = Math.Min(w * 0.36f, free * 0.5f);
        float baseline = free * 0.52f + size * 0.34f;

        DrawText(canvas, "Processador", w / 2, baseline - size * 0.92f, Math.Max(30, size * 0.12f), _muted, _regular, SKTextAlign.Center);
        DrawText(canvas, FormatTemperature(sensors.CpuTemperature, 1) + "°", w / 2 + size * 0.05f, baseline, size, color, _light, SKTextAlign.Center);
        string gpu = "Placa de vídeo  " + FormatTemperature(sensors.Gpu.Temperature, 0) + " °C";
        DrawText(canvas, gpu, w / 2, baseline + Math.Max(60, size * 0.26f), Math.Max(32, size * 0.13f), _text, _regular, SKTextAlign.Center);

        // Só a linha, sem grade nem eixo: mostra para onde a temperatura está indo.
        (float min, float max) = TemperatureRange(history);
        var area = new SKRect(Margin, h - chart - 40, w - Margin, h - 60);
        DrawSeries(canvas, area, history.CpuTemp, min, max, color, 5, fill: true);
    }

    // ---------- Blocos ----------

    private void DrawTilesPage(SKCanvas canvas, LcdContent content, LcdHistory history, float w, float h, bool wide)
    {
        SensorSnapshot sensors = content.Sensors;
        GpuReading gpu = sensors.Gpu;
        DrawHeader(canvas, content, w - Margin);

        (float min, float max) = TemperatureRange(history);
        float watts = 100;
        foreach (float? value in history.GpuWatts)
            watts = Math.Max(watts, (value ?? 0) * 1.1f);

        (string Label, string Value, float?[] Series, float Min, float Max, SKColor Color)[] tiles =
        [
            ("Processador", FormatTemperature(sensors.CpuTemperature, 1) + " °C", history.CpuTemp, min, max,
                TemperatureColor(sensors.CpuTemperature, _temps.CpuWarm, _temps.CpuHot)),
            ("Placa de vídeo", FormatTemperature(gpu.Temperature, 0) + " °C", history.GpuTemp, min, max,
                TemperatureColor(gpu.Temperature, _temps.GpuWarm, _temps.GpuHot)),
            ("Uso do processador", Percent(sensors.CpuLoad), history.CpuLoad, 0, 100, _accent),
            ("Uso da placa de vídeo", Percent(gpu.Load), history.GpuLoad, 0, 100, _accent),
            ("Memória", sensors.RamUsedGb is float used ? string.Create(CultureInfo.InvariantCulture, $"{used:0.0} GB") : "—",
                history.Ram, 0, 100, _accent),
            ("Consumo da GPU", gpu.Watts is float now ? $"{now:0} W" : "—", history.GpuWatts, 0, watts, _accent),
        ];

        int columns = wide ? 3 : 2, rows = tiles.Length / columns;
        const float gap = 20, top = 150;
        float tileWidth = (w - 2 * Margin - gap * (columns - 1)) / columns;
        float tileHeight = (h - top - 50 - gap * (rows - 1)) / rows;

        // Sobre imagem ou vídeo o bloco é um véu da cor de fundo; sem imagem, um tom acima dela.
        SKColor tileColor = _halo is null ? Blend(_background, _text, 0.06f) : _background.WithAlpha(150);
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i];
            float x = Margin + (i % columns) * (tileWidth + gap), y = top + (i / columns) * (tileHeight + gap);
            _paint.Color = tileColor;
            canvas.DrawRoundRect(x, y, tileWidth, tileHeight, 20, 20, _paint);

            float valueSize = Math.Min(64, tileHeight * 0.24f);
            DrawText(canvas, tile.Label, x + 22, y + 40, 24, _muted, _regular);
            DrawText(canvas, tile.Value, x + 20, y + 48 + valueSize, valueSize, tile.Color, _light);

            var area = new SKRect(x + 22, y + 64 + valueSize, x + tileWidth - 22, y + tileHeight - 22);
            if (area.Height > 24)
                DrawSeries(canvas, area, tile.Series, tile.Min, tile.Max, tile.Color, 3.5f, fill: true);
        }
    }
}
