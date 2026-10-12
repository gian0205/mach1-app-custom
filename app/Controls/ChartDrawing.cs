using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Mach1DriverCustom.Controls;

/// <summary>Uma série de um gráfico: o teste que ela representa, a cor e os valores.</summary>
/// <param name="Values">Um valor por categoria (núcleo) ou um só, conforme o gráfico.</param>
public sealed record ChartSeries(string Name, Color Color, IReadOnlyList<double> Values);

/// <summary>Uma linha de um gráfico no tempo; X em segundos.</summary>
public sealed record TimeSeries(string Name, Color Color, IReadOnlyList<Point> Points);

/// <summary>Uma marca no eixo do tempo, como o início de uma etapa.</summary>
public sealed record TimeMarker(double Time, string Label);

/// <summary>Cores e utilidades comuns aos gráficos.</summary>
public static class ChartDrawing
{
    // Cores das séries, na ordem fixa em que são atribuídas. Validadas contra o fundo do
    // painel (#1F2531) para daltonismo e contraste; por isso a comparação vai até três testes.
    public static readonly Color[] SeriesColors =
    [
        Color.Parse("#3987E5"),
        Color.Parse("#D95926"),
        Color.Parse("#199E70"),
    ];

    public static readonly IBrush Text = new SolidColorBrush(Color.Parse("#E6E9F0"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#95A0B5"));
    public static readonly IBrush Grid = new SolidColorBrush(Color.Parse("#343D50"));
    public static readonly IBrush Surface = new SolidColorBrush(Color.Parse("#1F2531"));
    public static readonly IBrush Raised = new SolidColorBrush(Color.Parse("#2A3242"));
    public static readonly IPen GridPen = new Pen(Grid, 1);

    public static FormattedText Label(string text, double size, IBrush brush, FontWeight weight = FontWeight.Normal) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    /// <summary>Número com as casas pedidas e ponto decimal, sem separador de milhar (que se confunde com o decimal).</summary>
    public static string Number(double value, int decimals = 0) =>
        value.ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture);

    /// <summary>Escala com marcas em números redondos que cobre o intervalo dado.</summary>
    public static (double Min, double Max, double Step) NiceScale(double min, double max, int targetTicks = 4)
    {
        if (max <= min)
            max = min + 1;

        double rough = (max - min) / targetTicks;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double step = new[] { 1.0, 2.0, 2.5, 5.0, 10.0 }.Select(m => m * magnitude).First(s => s >= rough);
        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    /// <summary>Caixa de dica com uma linha por série: bolinha na cor da série e o texto ao lado.</summary>
    public static void Tooltip(DrawingContext context, Rect bounds, Point anchor, string title,
        IReadOnlyList<(Color Color, string Text)> lines)
    {
        const double padding = 8, lineHeight = 18, dot = 8;
        FormattedText titleText = Label(title, 12, Muted);
        FormattedText[] texts = lines.Select(l => Label(l.Text, 12.5, Text)).ToArray();

        double width = Math.Max(titleText.Width, texts.Select(t => t.Width + dot + 6).DefaultIfEmpty().Max()) + padding * 2;
        double height = padding * 2 + lineHeight * (lines.Count + 1);

        // Fica à direita do cursor; se não couber, passa para a esquerda.
        double x = anchor.X + 14;
        if (x + width > bounds.Right)
            x = anchor.X - 14 - width;
        double y = Math.Clamp(anchor.Y - height / 2, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));

        context.DrawRectangle(Raised, GridPen, new Rect(x, y, width, height), 6, 6);
        context.DrawText(titleText, new Point(x + padding, y + padding));
        for (int i = 0; i < texts.Length; i++)
        {
            double lineY = y + padding + lineHeight * (i + 1);
            context.DrawEllipse(new SolidColorBrush(lines[i].Color), null,
                new Point(x + padding + dot / 2, lineY + lineHeight / 2 - 1), dot / 2, dot / 2);
            context.DrawText(texts[i], new Point(x + padding + dot + 6, lineY));
        }
    }
}
