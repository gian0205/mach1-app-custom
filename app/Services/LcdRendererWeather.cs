using SkiaSharp;
using System;
using System.Linq;

namespace Mach1CustomApp.Services;

// Página do clima: o tempo agora, as próximas horas e os próximos dias, com desenhos de tempo em
// traço, que seguem as cores do tema e o efeito neon.
public sealed partial class LcdRenderer
{
    private static readonly string[] ShortDays = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];

    private void DrawWeatherPage(SKCanvas canvas, LcdContent content, float w, float h, bool wide)
    {
        WeatherSnapshot? weather = content.Weather;
        DrawText(canvas, weather?.Place ?? "Clima", Margin, 104, 44, _text, _regular);
        DrawText(canvas, content.Time.ToString("HH:mm"), w - Margin, 104, 44, _muted, _regular, SKTextAlign.Right);

        if (weather is null)
        {
            DrawText(canvas, "Sem previsão do tempo", w / 2, h / 2 - 10, 46, _text, _regular, SKTextAlign.Center);
            DrawText(canvas, "Ligue o clima na aba da tela do app", w / 2, h / 2 + 50, 32, _muted, _regular, SKTextAlign.Center);
            return;
        }

        if (wide)
        {
            // Deitada: o tempo agora à esquerda, horas e dias à direita.
            float middle = w / 2;
            float y = DrawWeatherNow(canvas, weather, Margin, middle - 30, 150);
            DrawWeatherDetails(canvas, weather, Margin, middle - 30, y + 50);
            y = DrawWeatherHours(canvas, weather, middle + 30, w - Margin, 140, 6);
            DrawWeatherDays(canvas, weather, middle + 30, w - Margin, y + 30, h - 26);
        }
        else
        {
            // Em pé ou quadrada: uma coluna, e o que não couber na altura fica de fora.
            float y = DrawWeatherNow(canvas, weather, Margin, w - Margin, 150);
            y = DrawWeatherDetails(canvas, weather, Margin, w - Margin, y + 50);
            if (y + 50 + HoursHeight <= h - 20)
                y = DrawWeatherHours(canvas, weather, Margin, w - Margin, y + 50, 8);
            DrawWeatherDays(canvas, weather, Margin, w - Margin, y + 40, h - 30);
        }
    }

    /// <summary>Desenho, temperatura, descrição e máxima e mínima de hoje. Devolve onde termina.</summary>
    private float DrawWeatherNow(SKCanvas canvas, WeatherSnapshot weather, float left, float right, float top)
    {
        const float icon = 190;
        DrawWeatherIcon(canvas, WeatherCodes.Icon(weather.Code), weather.IsDay, left + icon / 2, top + icon / 2 + 6, icon);
        DrawText(canvas, $"{weather.Temperature:0}°", right + 8, top + 178, 200, _accent, _light, SKTextAlign.Right);

        DrawText(canvas, WeatherCodes.Describe(weather.Code), left, top + 252, 42, _text, _regular);
        if (weather.Days.Count > 0)
        {
            WeatherDay today = weather.Days[0];
            DrawText(canvas, $"Máx {today.Max:0}°   Mín {today.Min:0}°", left, top + 302, 34, _muted, _regular);
        }
        return top + 302;
    }

    /// <summary>Sensação, umidade, vento e chance de chuva, lado a lado. Devolve onde termina.</summary>
    private float DrawWeatherDetails(SKCanvas canvas, WeatherSnapshot weather, float left, float right, float top)
    {
        int? rain = weather.Hours.Count > 0 ? weather.Hours[0].RainChance : null;
        (string Label, string Value)[] items =
        [
            ("Sensação", weather.FeelsLike is float feels ? $"{feels:0}°" : "—"),
            ("Umidade", weather.Humidity is int humidity ? $"{humidity} %" : "—"),
            (weather.WindFrom is float from ? "Vento " + WeatherCodes.Compass(from) : "Vento", weather.WindKmh is float wind ? $"{wind:0} km/h" : "—"),
            ("Chuva", rain is int r ? $"{r} %" : "—"),
        ];

        // O vento tem o valor mais comprido; a chance de chuva, o mais curto.
        float[] weights = [1, 1, 1.3f, 0.7f];
        float unit = (right - left) / weights.Sum(), x = left;
        for (int i = 0; i < items.Length; x += unit * weights[i], i++)
        {
            DrawText(canvas, items[i].Label, x, top + 30, 28, _muted, _regular);
            DrawText(canvas, items[i].Value, x, top + 80, 40, _text, _regular);
        }
        return top + 80;
    }

    private const float HoursHeight = 254;

    /// <summary>Colunas de 3 em 3 horas: hora, desenho, temperatura e chance de chuva. Devolve onde termina.</summary>
    private float DrawWeatherHours(SKCanvas canvas, WeatherSnapshot weather, float left, float right, float top, int columns)
    {
        DrawText(canvas, "Próximas horas", left, top + 30, 32, _muted, _regular);

        WeatherHour[] hours = weather.Hours.Where((_, i) => i % 3 == 0).Take(columns).ToArray();
        float column = (right - left) / columns;
        for (int i = 0; i < hours.Length; i++)
        {
            WeatherHour hour = hours[i];
            float x = left + column * (i + 0.5f);
            DrawText(canvas, i == 0 ? "Agora" : $"{hour.Time.Hour}h", x, top + 82, 28, _muted, _regular, SKTextAlign.Center);
            DrawWeatherIcon(canvas, WeatherCodes.Icon(hour.Code), hour.IsDay, x, top + 140, 62);
            DrawText(canvas, $"{hour.Temperature:0}°", x + 6, top + 214, 38, _text, _regular, SKTextAlign.Center);
            // Chance de chuva só quando ela pesa; abaixo de 20 % vira ruído.
            if (hour.RainChance is int rain && rain >= 20)
                DrawText(canvas, $"{rain} %", x, top + 250, 28, _accent, _regular, SKTextAlign.Center);
        }
        return top + HoursHeight;
    }

    /// <summary>Uma linha por dia: nome, desenho, chance de chuva, mínima, faixa do dia na semana e máxima.</summary>
    private void DrawWeatherDays(SKCanvas canvas, WeatherSnapshot weather, float left, float right, float top, float bottom)
    {
        const float title = 52, minRow = 48;
        int rows = Math.Min(weather.Days.Count, (int)((bottom - top - title) / minRow));
        if (rows < 2)
            return;

        float rowHeight = Math.Min(64, (bottom - top - title) / rows);
        DrawText(canvas, "Próximos dias", left, top + 30, 32, _muted, _regular);

        float low = weather.Days.Take(rows).Min(d => d.Min), high = weather.Days.Take(rows).Max(d => d.Max);
        float span = Math.Max(1, high - low), width = right - left;
        float barLeft = left + width * 0.6f, barRight = right - width * 0.14f;

        for (int i = 0; i < rows; i++)
        {
            WeatherDay day = weather.Days[i];
            float center = top + title + rowHeight * (i + 0.5f);
            float baseline = center + 12;

            DrawText(canvas, i == 0 ? "Hoje" : ShortDays[(int)day.Date.DayOfWeek], left, baseline, 34, _text, _regular);
            DrawWeatherIcon(canvas, WeatherCodes.Icon(day.Code), true, left + width * 0.25f, center, Math.Min(46, rowHeight * 0.8f));
            if (day.RainChance is int rain && rain >= 20)
                DrawText(canvas, $"{rain} %", left + width * 0.33f, baseline - 2, 28, _accent, _regular);

            // A faixa mostra onde a mínima e a máxima do dia caem dentro da semana.
            DrawText(canvas, $"{day.Min:0}°", barLeft - 14, baseline, 34, _muted, _regular, SKTextAlign.Right);
            DrawBar(canvas, barLeft, center - 5, barRight - barLeft, 10, null, _accent);
            float from = barLeft + (barRight - barLeft) * (day.Min - low) / span;
            float to = barLeft + (barRight - barLeft) * (day.Max - low) / span;
            DrawFill(canvas, from, center - 5, Math.Max(10, to - from), 10, _accent);
            DrawText(canvas, $"{day.Max:0}°", right, baseline, 34, _text, _regular, SKTextAlign.Right);
        }
    }

    // ---------- Desenhos de tempo ----------

    /// <summary>
    /// Desenho de tempo em traço, num quadro de 100 unidades centrado em (cx, cy): sol ou lua na cor de
    /// destaque, nuvem na cor do texto, chuva, neve e raio na cor de destaque.
    /// </summary>
    private void DrawWeatherIcon(SKCanvas canvas, WeatherIcon icon, bool isDay, float cx, float cy, float size)
    {
        // Traço mais grosso nos desenhos pequenos, para não sumir.
        float stroke = size < 80 ? 7.5f : 5f;
        SKColor sky = _accent, cloud = _text;

        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.Scale(size / 100f);
        switch (icon)
        {
            case WeatherIcon.Clear:
                DrawSky(canvas, isDay, 0, 0, 1.15f, sky, stroke);
                break;

            case WeatherIcon.PartlyCloudy:
                using (SKPath shape = CloudPath(8, 12, 0.8f))
                {
                    // O sol (ou a lua) fica atrás da nuvem: recorta a nuvem e uma folga em volta dela.
                    using SKPath gap = Inflate(shape, stroke * 2.4f);
                    canvas.Save();
                    canvas.ClipPath(gap, SKClipOperation.Difference, antialias: true);
                    DrawSky(canvas, isDay, -16, -18, 0.78f, sky, stroke);
                    canvas.Restore();
                    StrokePath(canvas, shape, cloud, stroke);
                }
                break;

            case WeatherIcon.Fog:
                using (var lines = new SKPath())
                {
                    float[][] fog = [[-34, -22, 30], [-40, -6, 40], [-30, 10, 36], [-38, 26, 22]];
                    foreach (float[] l in fog)
                    {
                        lines.MoveTo(l[0], l[1]);
                        lines.LineTo(l[2], l[1]);
                    }
                    StrokePath(canvas, lines, cloud, stroke);
                }
                break;

            case WeatherIcon.Cloudy:
                using (SKPath shape = CloudPath(0, 4, 1f))
                    StrokePath(canvas, shape, cloud, stroke);
                break;

            default:
                // Chuva, garoa, neve e trovoada: a nuvem sobe e o que cai vai embaixo dela.
                using (SKPath shape = CloudPath(0, -14, 0.86f))
                    StrokePath(canvas, shape, cloud, stroke);
                using (var fall = new SKPath())
                {
                    foreach (float x in new[] { -18f, 2f, 22f })
                    {
                        switch (icon)
                        {
                            case WeatherIcon.Rain:
                                fall.MoveTo(x, 26);
                                fall.LineTo(x - 7, 44);
                                break;
                            case WeatherIcon.Drizzle:
                                fall.MoveTo(x, 28);
                                fall.LineTo(x - 2, 33);
                                fall.MoveTo(x - 5, 40);
                                fall.LineTo(x - 7, 45);
                                break;
                            case WeatherIcon.Snow:
                                fall.MoveTo(x - 6, 36);
                                fall.LineTo(x + 6, 36);
                                fall.MoveTo(x - 3, 31);
                                fall.LineTo(x + 3, 41);
                                fall.MoveTo(x + 3, 31);
                                fall.LineTo(x - 3, 41);
                                break;
                        }
                    }
                    if (icon == WeatherIcon.Storm)
                    {
                        fall.MoveTo(6, 20);
                        fall.LineTo(-6, 36);
                        fall.LineTo(5, 36);
                        fall.LineTo(-5, 50);
                    }
                    StrokePath(canvas, fall, sky, stroke);
                }
                break;
        }
        canvas.Restore();
    }

    /// <summary>Sol com raios, de dia; lua crescente, de noite.</summary>
    private void DrawSky(SKCanvas canvas, bool isDay, float x, float y, float scale, SKColor color, float stroke)
    {
        using var path = new SKPath();
        if (isDay)
        {
            path.AddCircle(x, y, 17 * scale);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
                path.MoveTo(x + c * 27 * scale, y + s * 27 * scale);
                path.LineTo(x + c * 37 * scale, y + s * 37 * scale);
            }
            StrokePath(canvas, path, color, stroke);
            return;
        }

        using var full = new SKPath();
        full.AddCircle(x, y, 30 * scale);
        using var bite = new SKPath();
        bite.AddCircle(x + 15 * scale, y - 11 * scale, 25 * scale);
        using SKPath? moon = full.Op(bite, SKPathOp.Difference);
        if (moon is not null)
            StrokePath(canvas, moon, color, stroke);
    }

    /// <summary>Contorno de nuvem: um corpo arredondado com três bolhas em cima.</summary>
    private static SKPath CloudPath(float dx, float dy, float scale)
    {
        using var body = new SKPath();
        body.AddRoundRect(new SKRect(-42, 0, 42, 30), 15, 15);
        using var left = new SKPath();
        left.AddCircle(-18, -1, 19);
        using var top = new SKPath();
        top.AddCircle(6, -12, 25);
        using var right = new SKPath();
        right.AddCircle(28, 7, 16);

        using SKPath a = body.Op(left, SKPathOp.Union) ?? new SKPath(body);
        using SKPath b = a.Op(top, SKPathOp.Union) ?? new SKPath(a);
        SKPath cloud = b.Op(right, SKPathOp.Union) ?? new SKPath(b);
        cloud.Transform(SKMatrix.CreateScale(scale, scale).PostConcat(SKMatrix.CreateTranslation(dx, dy)));
        return cloud;
    }

    /// <summary>A área de um contorno engordada por <paramref name="margin"/>.</summary>
    private static SKPath Inflate(SKPath path, float margin)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = margin * 2, StrokeJoin = SKStrokeJoin.Round };
        using var outline = new SKPath();
        paint.GetFillPath(path, outline);
        return outline.Op(path, SKPathOp.Union) ?? new SKPath(path);
    }
}
