using SkiaSharp;
using System;
using Mach1CustomApp.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Mach1CustomApp.Services;

public enum LcdPage
{
    /// <summary>Temperaturas grandes e os demais sensores.</summary>
    Temperatures,

    /// <summary>Margem e clock de cada núcleo.</summary>
    Curve,

    /// <summary>Layout personalizado, montado no editor.</summary>
    Custom,
}

/// <summary>Os dados que o painel mostra em um quadro.</summary>
/// <param name="CpuLimit">Temperatura em que o processador começa a se limitar, em °C.</param>
/// <param name="MarginMin">Menor margem do Curve Optimizer neste processador.</param>
public sealed record LcdContent(string CpuName, SensorSnapshot Sensors, int?[] Margins, double[] ClocksMhz, DateTime Time,
    float CpuLimit = 95, int MarginMin = CpuTuner.DefaultMinMargin)
;

/// <summary>Quando e para que cor a temperatura muda conforme sobe.</summary>
/// <param name="Enabled">Desligado, a temperatura fica sempre na cor de destaque do tema.</param>
/// <param name="CpuWarm">Temperatura do processador, em °C, a partir da qual vale a cor de atenção.</param>
/// <param name="CpuHot">A partir da qual vale a cor crítica.</param>
/// <param name="Warn">Cor de atenção, em 0xAARRGGBB.</param>
/// <param name="Hot">Cor crítica.</param>
public readonly record struct LcdTempColors(bool Enabled, float CpuWarm, float CpuHot, float GpuWarm, float GpuHot, uint Warn, uint Hot)
{
    public const uint DefaultWarn = 0xFFF59A3C, DefaultHot = 0xFFF0605A;

    /// <summary>
    /// Os valores automáticos: atenção 20 °C antes do limite do processador e crítico 7 °C antes;
    /// na placa de vídeo, 75 e 85 °C.
    /// </summary>
    public static LcdTempColors Automatic(float cpuLimit) => new(true, cpuLimit - 20, cpuLimit - 7, 75, 85, DefaultWarn, DefaultHot);
}

/// <summary>Cores do painel escolhidas pelo usuário, em 0xAARRGGBB.</summary>
public readonly record struct LcdTheme(uint Background, uint Text, uint Accent)
{
    public static LcdTheme Default { get; } = new(0xFF171B24, 0xFFE6E9F0, 0xFF7CC4F2);
}

/// <summary>Um tema pronto: três cores que combinam, com nome.</summary>
public sealed record LcdThemePreset(string Name, LcdTheme Theme)
{
    /// <summary>
    /// Temas prontos. O destaque nunca é laranja nem vermelho, que ficam para os avisos de
    /// temperatura; texto e destaque têm contraste de pelo menos 4,5:1 com o fundo.
    /// </summary>
    public static IReadOnlyList<LcdThemePreset> All { get; } =
    [
        new("Ardósia", LcdTheme.Default),
        new("Meia-noite", new(0xFF0F1424, 0xFFE3E8F7, 0xFF8C9EFF)),
        new("Oceano", new(0xFF0E2230, 0xFFE2EEF3, 0xFF4FD1D9)),
        new("Floresta", new(0xFF14201B, 0xFFE4EDE6, 0xFF6FD3A0)),
        new("Limão", new(0xFF15190F, 0xFFEEF2E2, 0xFFC6E25A)),
        new("Ametista", new(0xFF1D1826, 0xFFECE6F4, 0xFFB79CF5)),
        new("Vinho", new(0xFF24151B, 0xFFF3E6EA, 0xFFF08BB4)),
        new("Carbono", new(0xFF161616, 0xFFF0F0F0, 0xFFC9CED6)),
        new("Areia", new(0xFFEFEAE0, 0xFF2A2722, 0xFF2F6F8F)),
        new("Papel", new(0xFFF5F6F8, 0xFF1B1F27, 0xFF2563C9)),
    ];
}

/// <summary>Como desenhar um quadro.</summary>
/// <param name="Flipped">Gira 180°, para tela montada de cabeça para baixo.</param>
/// <param name="AlertFlash">Quadro "aceso" do alerta de temperatura.</param>
/// <param name="ImageDim">Opacidade da cor de fundo sobre a imagem ou o vídeo, de 0 (só a imagem) a 100 (só a cor).</param>
/// <param name="Temps">Cores da temperatura; null usa os valores automáticos para o processador.</param>
public readonly record struct LcdStyle(LcdTheme Theme, bool Flipped, LcdPage Page, bool AlertFlash, int ImageDim, LcdTempColors? Temps = null);

/// <summary>
/// Desenha o painel de 720x1280 e devolve o JPEG.
/// </summary>
public sealed class LcdRenderer : IDisposable
{
    // As páginas prontas são desenhadas em colunas de 720 unidades de largura, que depois são
    // escaladas e arrumadas conforme o formato da tela (ver DrawPages).
    private const float ColumnWidth = 720;
    private const float Left = 56;
    private const float Right = ColumnWidth - 56;

    // Placas de vídeo costumam se limitar perto de 90 °C; o limite do processador vem no conteúdo.
    private const float GpuMax = 90;

    // Avisos de temperatura não seguem o tema: precisam continuar reconhecíveis. Em fundo claro
    // usam versões mais escuras, senão o laranja some.
    private static readonly SKColor EmberOnDark = SKColor.Parse("#F59A3C"), CriticalOnDark = SKColor.Parse("#F0605A");
    private static readonly SKColor EmberOnLight = SKColor.Parse("#B45309"), CriticalOnLight = SKColor.Parse("#C0261F");

    private SKColor Ember, Critical;

    // Cores e faixas da temperatura neste quadro.
    private LcdTempColors _temps;
    private SKColor _warn, _hot;

    private readonly SKBitmap _bitmap = new(LcdScreen.Width, LcdScreen.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
    private readonly SKTypeface _regular = SKTypeface.FromFamilyName("Bahnschrift", SKFontStyle.Normal);
    private readonly SKTypeface _light = SKTypeface.FromFamilyName("Bahnschrift",
        SKFontStyleWeight.Light, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
    private readonly SKPaint _paint = new() { IsAntialias = true };

    private SKColor _text, _accent, _muted, _track;
    private SKColor? _halo;

    /// <param name="quality">Qualidade do JPEG, de 0 a 100; menor dá quadros menores, que chegam mais rápido à tela.</param>
    /// <param name="layout">Layout personalizado; só é usado na página <see cref="LcdPage.Custom"/>.</param>
    /// <param name="media">Quadro atual de cada elemento do layout (null onde não há imagem).</param>
    public byte[] Render(LcdContent content, LcdStyle style, SKImage? backgroundImage, int quality = 90,
        LcdLayout? layout = null, IReadOnlyList<SKImage?>? media = null)
    {
        var background = new SKColor(style.Theme.Background);
        bool light = 0.2126f * background.Red + 0.7152f * background.Green + 0.0722f * background.Blue > 140;
        Ember = light ? EmberOnLight : EmberOnDark;
        Critical = light ? CriticalOnLight : CriticalOnDark;

        // As cores padrão da temperatura acompanham o fundo (mais escuras em tema claro); uma
        // cor escolhida pelo usuário vai como ele escolheu.
        _temps = style.Temps ?? LcdTempColors.Automatic(content.CpuLimit);
        _warn = _temps.Warn == LcdTempColors.DefaultWarn ? Ember : new SKColor(_temps.Warn);
        _hot = _temps.Hot == LcdTempColors.DefaultHot ? Critical : new SKColor(_temps.Hot);
        if (style.AlertFlash)
            background = Blend(background, Critical, 0.4f);

        _text = new SKColor(style.Theme.Text);
        _accent = new SKColor(style.Theme.Accent);
        // Em fundo claro os rótulos precisam ficar mais perto da cor do texto para dar leitura.
        _muted = Blend(background, _text, light ? 0.72f : 0.58f);
        _track = Blend(background, _text, 0.13f);

        using var canvas = new SKCanvas(_bitmap);
        canvas.Clear(background);
        if (style.Flipped)
            canvas.RotateDegrees(180, LcdScreen.Width / 2f, LcdScreen.Height / 2f);

        // Sobre imagem ou vídeo o texto ganha um contorno na cor de fundo, para não sumir.
        _halo = backgroundImage is null ? null : background.WithAlpha(150);
        if (backgroundImage is not null)
            DrawBackgroundImage(canvas, backgroundImage, background, style.ImageDim);

        if (style.Page == LcdPage.Custom)
        {
            DrawCustomPage(canvas, content, layout ?? new LcdLayout(), media ?? []);
        }
        else
        {
            DrawPages(canvas, content, style.Page);
        }

        if (style.AlertFlash)
        {
            _paint.Color = Critical;
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = 24;
            canvas.DrawRect(12, 12, LcdScreen.Width - 24, LcdScreen.Height - 24, _paint);
            _paint.Style = SKPaintStyle.Fill;
        }

        canvas.Flush();
        using SKImage image = SKImage.FromBitmap(_bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    /// <summary>Preenche a tela com a imagem (cortando as sobras) e a escurece para o texto continuar legível.</summary>
    private void DrawBackgroundImage(SKCanvas canvas, SKImage image, SKColor background, int dim)
    {
        float scale = Math.Max(LcdScreen.Width / (float)image.Width, LcdScreen.Height / (float)image.Height);
        float width = image.Width * scale, height = image.Height * scale;
        var target = SKRect.Create((LcdScreen.Width - width) / 2, (LcdScreen.Height - height) / 2, width, height);
        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

        _paint.Color = background.WithAlpha((byte)(255 * Math.Clamp(dim, 0, 100) / 100));
        canvas.DrawRect(0, 0, LcdScreen.Width, LcdScreen.Height, _paint);
    }

    private void DrawHeader(SKCanvas canvas, LcdContent content)
    {
        // "AMD Ryzen 7 9800X3D 8-Core Processor" → "Ryzen 7 9800X3D";
        // "Intel(R) Core(TM) i7-13700K CPU @ 3.40GHz" → "Intel Core i7-13700K"
        string name = Regex.Replace(content.CpuName, @"\((R|TM)\)|^AMD\s+|\s+\d+-Core Processor$|\s+CPU\s+@.*$|\s+with Radeon.*$|\s+w/ Radeon.*$", "");
        DrawText(canvas, name, Left, 104, 44, _text, _regular);
        DrawText(canvas, content.Time.ToString("HH:mm"), Right, 104, 44, _muted, _regular, SKTextAlign.Right);
    }

    /// <summary>
    /// Arruma a página no formato da tela. Tela em pé: uma coluna, como na IRV360. Tela deitada:
    /// duas colunas lado a lado. Tela quase quadrada: uma coluna com as duas temperaturas lado a
    /// lado, em metade do tamanho. Em todos os casos, o que não cabe na altura fica de fora.
    /// </summary>
    private void DrawPages(SKCanvas canvas, LcdContent content, LcdPage page)
    {
        float width = LcdScreen.Width, height = LcdScreen.Height, ratio = width / height;
        SensorSnapshot sensors = content.Sensors;

        void Temperatures(float top) { DrawCpuTemperature(canvas, content, top); DrawGpuTemperature(canvas, sensors.Gpu, top + 260); }

        canvas.Save();
        if (ratio >= 1.25f)
        {
            // Deitada: cabeçalho e temperaturas à esquerda, o resto à direita.
            float scale = Math.Min(width / (2 * ColumnWidth), height / 700);
            float logicalHeight = height / scale;
            canvas.Translate((width - 2 * ColumnWidth * scale) / 2, 0);
            canvas.Scale(scale);

            DrawHeader(canvas, content);
            Temperatures(150);
            canvas.Translate(ColumnWidth, 0);
            if (page == LcdPage.Curve)
                DrawCurveTable(canvas, content, 104, logicalHeight - 104 - 60);
            else
                DrawSensorRows(canvas, sensors, 104, logicalHeight);
        }
        else
        {
            float scale = width / ColumnWidth;
            float logicalHeight = height / scale;
            canvas.Scale(scale);
            DrawHeader(canvas, content);

            if (ratio > 0.8f)
            {
                // Quase quadrada: as duas temperaturas dividem a largura.
                canvas.Save();
                canvas.Translate(0, 150);
                canvas.Scale(0.5f);
                DrawCpuTemperature(canvas, content, 0);
                canvas.Translate(ColumnWidth, 0);
                DrawGpuTemperature(canvas, sensors.Gpu, 0);
                canvas.Restore();

                if (page == LcdPage.Curve)
                    DrawCurveTable(canvas, content, 350, logicalHeight - 350 - 50);
                else
                    DrawSensorRows(canvas, sensors, 350, logicalHeight);
            }
            else if (page == LcdPage.Curve)
            {
                DrawSmallTemperature(canvas, "Processador", sensors.CpuTemperature, Left, 1, _temps.CpuWarm, _temps.CpuHot);
                DrawSmallTemperature(canvas, "Placa de vídeo", sensors.Gpu.Temperature, 392, 0, _temps.GpuWarm, _temps.GpuHot);
                DrawCurveTable(canvas, content, 372, logicalHeight - 372 - 108);
            }
            else
            {
                Temperatures(150);
                DrawSensorRows(canvas, sensors, 722, logicalHeight);
            }
        }
        canvas.Restore();
    }

    private void DrawCpuTemperature(SKCanvas canvas, LcdContent content, float top) =>
        DrawBigTemperature(canvas, "Processador", content.Sensors.CpuTemperature, top, decimals: 1, _temps.CpuWarm, _temps.CpuHot, content.CpuLimit);

    private void DrawGpuTemperature(SKCanvas canvas, GpuReading gpu, float top) =>
        DrawBigTemperature(canvas, "Placa de vídeo", gpu.Temperature, top, decimals: 0, _temps.GpuWarm, _temps.GpuHot, GpuMax);

    /// <summary>As linhas de sensores a partir de <paramref name="top"/>, quantas couberem até <paramref name="bottom"/>.</summary>
    private void DrawSensorRows(SKCanvas canvas, SensorSnapshot sensors, float top, float bottom)
    {
        GpuReading gpu = sensors.Gpu;
        (string Label, string Value, float? Fraction)[] rows =
        [
            ("Uso do processador", Percent(sensors.CpuLoad), sensors.CpuLoad / 100),
            ("Uso da placa de vídeo", Percent(gpu.Load), gpu.Load / 100),
            ("Memória", Gigabytes(sensors.RamUsedGb, sensors.RamTotalGb), sensors.RamUsedGb / sensors.RamTotalGb),
            ("Memória de vídeo", Gigabytes(gpu.MemoryUsedGb, gpu.MemoryTotalGb), gpu.MemoryUsedGb / gpu.MemoryTotalGb),
            ("Consumo da placa de vídeo", gpu.Watts is float w ? $"{w:0} W" : "—", null),
        ];

        float y = top;
        foreach (var row in rows)
        {
            // Cada linha ocupa do texto até a barra, 30 unidades abaixo.
            if (y + 30 > bottom - 20)
                break;
            DrawSensorRow(canvas, ref y, row.Label, row.Value, row.Fraction);
        }
    }

    private void DrawBigTemperature(SKCanvas canvas, string label, float? temperature, float top,
        int decimals, float warm, float hot, float max)
    {
        SKColor color = TemperatureColor(temperature, warm, hot);
        DrawText(canvas, label, Left, top + 34, 32, _muted, _regular);

        const float size = 190;
        string value = FormatTemperature(temperature, decimals);
        using (var font = new SKFont(_light, size))
        {
            float width = font.MeasureText(value);
            DrawText(canvas, value, Left - 6, top + 196, size, color, _light);
            DrawText(canvas, "°C", Left + width + 10, top + 196, 64, color, _light);
        }

        // Régua de 20 °C até o limite do componente
        float? fraction = temperature is float t ? Math.Max(0.02f, (t - 20) / (max - 20)) : null;
        DrawBar(canvas, Left, top + 222, Right - Left, 12, fraction, color);
    }

    private void DrawSensorRow(SKCanvas canvas, ref float y, string label, string value, float? fraction)
    {
        DrawText(canvas, label, Left, y, 28, _muted, _regular);
        DrawText(canvas, value, Right, y, 34, _text, _regular, SKTextAlign.Right);
        if (fraction is not null)
            DrawBar(canvas, Left, y + 20, Right - Left, 10, fraction, _accent);
        y += 104;
    }

    /// <summary>Título e uma linha por núcleo (barra, margem e clock), dentro da altura disponível.</summary>
    private void DrawCurveTable(SKCanvas canvas, LcdContent content, float top, float available)
    {
        const float barLeft = 132, barRight = 410, marginRight = 524;
        float min = content.MarginMin, max = CpuTuner.MaxMargin;

        DrawText(canvas, "Curve Optimizer", Left, top, 40, _text, _regular);
        DrawText(canvas, "margem", marginRight, top, 26, _muted, _regular, SKTextAlign.Right);
        DrawText(canvas, "MHz", Right, top, 26, _muted, _regular, SKTextAlign.Right);

        int count = content.Margins.Length;
        if (count == 0)
            return;

        float rowHeight = Math.Min(98, Math.Max(10, available - 30) / count);
        float fontSize = Math.Min(44, rowHeight * 0.5f);
        float thickness = Math.Min(14, rowHeight * 0.2f);
        float zero = barLeft + (barRight - barLeft) * (0 - min) / (max - min);

        for (int i = 0; i < count; i++)
        {
            float center = top + 30 + rowHeight * (i + 0.5f);
            float baseline = center + fontSize * 0.36f;

            DrawText(canvas, i.ToString(), Left, baseline, fontSize, _muted, _regular);
            DrawBar(canvas, barLeft, center - thickness / 2, barRight - barLeft, thickness, null, _accent);

            if (content.Margins[i] is int margin)
            {
                float x = barLeft + (barRight - barLeft) * (Math.Clamp(margin, min, max) - min) / (max - min);
                if (Math.Abs(x - zero) >= 1)
                {
                    _paint.Color = margin < 0 ? _accent : Ember;
                    canvas.DrawRoundRect(Math.Min(zero, x), center - thickness / 2, Math.Abs(x - zero), thickness,
                        thickness / 2, thickness / 2, _paint);
                }
                // A Bahnschrift não tem o sinal de menos tipográfico; usa o hífen.
                DrawText(canvas, margin.ToString("+0;-0;0"), marginRight, baseline, fontSize, _text, _regular, SKTextAlign.Right);
            }

            _paint.Color = _muted;
            canvas.DrawRect(zero - 1, center - thickness, 2, thickness * 2, _paint);

            if (i < content.ClocksMhz.Length && content.ClocksMhz[i] > 0)
                DrawText(canvas, content.ClocksMhz[i].ToString("0"), Right, baseline, fontSize, _muted, _regular, SKTextAlign.Right);
        }
    }

    /// <summary>Desenha os elementos do layout personalizado, na ordem da lista.</summary>
    private void DrawCustomPage(SKCanvas canvas, LcdContent content, LcdLayout layout, IReadOnlyList<SKImage?> media)
    {
        for (int i = 0; i < layout.Elements.Count; i++)
        {
            LcdElement element = layout.Elements[i];
            float x = (float)element.X, y = (float)element.Y, width = (float)element.Width, height = (float)element.Height;
            SKColor? custom = element.Color is uint argb ? new SKColor(argb) : null;

            switch (element.Kind)
            {
                case LcdElementKind.Text:
                case LcdElementKind.Sensor:
                    string text = element.Kind == LcdElementKind.Text ? element.Text ?? "" : LcdSensors.Format(element.Sensor, content);
                    // A altura da caixa define a letra; a linha de base fica a 78 % dela.
                    (float anchor, SKTextAlign align) = element.Align switch
                    {
                        1 => (x + width / 2, SKTextAlign.Center),
                        2 => (x + width, SKTextAlign.Right),
                        _ => (x, SKTextAlign.Left),
                    };
                    DrawText(canvas, text, anchor, y + height * 0.78f, height * 0.8f, custom ?? _text, _regular, align);
                    break;

                case LcdElementKind.Bar:
                    DrawBar(canvas, x, y, width, height, LcdSensors.Fraction(element.Sensor, content) ?? 0, custom ?? _accent);
                    break;

                case LcdElementKind.Image when i < media.Count && media[i] is { } image:
                    // Cabe inteira dentro da caixa, sem distorcer, centralizada.
                    float scale = Math.Min(width / image.Width, height / image.Height);
                    float w = image.Width * scale, h = image.Height * scale;
                    canvas.DrawImage(image, SKRect.Create(x + (width - w) / 2, y + (height - h) / 2, w, h),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    break;
            }
        }
    }

    private void DrawSmallTemperature(SKCanvas canvas, string label, float? temperature, float x,
        int decimals, float warm, float hot)
    {
        DrawText(canvas, label, x, 180, 28, _muted, _regular);
        DrawText(canvas, FormatTemperature(temperature, decimals) + " °C", x - 3, 262, 78, TemperatureColor(temperature, warm, hot), _light);
    }

    /// <summary>Trilho arredondado com a fração preenchida; fração null desenha só o trilho.</summary>
    private void DrawBar(SKCanvas canvas, float x, float y, float width, float height, float? fraction, SKColor color)
    {
        float radius = height / 2;
        _paint.Color = _track;
        canvas.DrawRoundRect(x, y, width, height, radius, radius, _paint);
        if (fraction is float f && !float.IsNaN(f))
        {
            _paint.Color = color;
            canvas.DrawRoundRect(x, y, Math.Max(height, width * Math.Clamp(f, 0, 1)), height, radius, radius, _paint);
        }
    }

    private void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKColor color,
        SKTypeface typeface, SKTextAlign align = SKTextAlign.Left)
    {
        using var font = new SKFont(typeface, size) { Subpixel = true };
        if (_halo is SKColor halo)
        {
            _paint.Color = halo;
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = Math.Max(3, size * 0.09f);
            _paint.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawText(text, x, y, align, font, _paint);
            _paint.Style = SKPaintStyle.Fill;
        }
        _paint.Color = color;
        canvas.DrawText(text, x, y, align, font, _paint);
    }

    private SKColor TemperatureColor(float? temperature, float warm, float hot) => temperature switch
    {
        null => _muted,
        _ when !_temps.Enabled => _accent,
        var t when t >= hot => _hot,
        var t when t >= warm => _warn,
        _ => _accent,
    };

    private static string FormatTemperature(float? temperature, int decimals) => temperature is float t
        ? t.ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)
        : "—";

    private static string Percent(float? value) => value is float v ? $"{v:0} %" : "—";

    private static string Gigabytes(float? used, float? total) => used is float u && total is float t
        ? string.Create(CultureInfo.InvariantCulture, $"{u:0.0} de {t:0.0} GB")
        : "—";

    private static SKColor Blend(SKColor from, SKColor to, float amount) => new(
        (byte)(from.Red + (to.Red - from.Red) * amount),
        (byte)(from.Green + (to.Green - from.Green) * amount),
        (byte)(from.Blue + (to.Blue - from.Blue) * amount));

    public void Dispose()
    {
        _paint.Dispose();
        _regular.Dispose();
        _light.Dispose();
        _bitmap.Dispose();
    }
}
