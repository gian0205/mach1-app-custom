using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1CustomApp.Controls;

/// <summary>
/// Pontos por categoria (um núcleo por coluna, um ponto por teste). O eixo não começa no zero
/// de propósito: as diferenças entre núcleos são de poucos por cento e barras as esconderiam.
/// </summary>
public class DotPlot : Control
{
    private const double Left = 56, Right = 12, Top = 10, Bottom = 26, Radius = 5, Spread = 14;

    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<DotPlot, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<string> CategoryNameProperty =
        AvaloniaProperty.Register<DotPlot, string>(nameof(CategoryName), "Núcleo");

    private Point? _pointer;

    /// <summary>De 0 a 1: o tamanho dos pontos enquanto aparecem.</summary>
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<DotPlot, double>(nameof(Reveal), 1);

    static DotPlot()
    {
        AffectsRender<DotPlot>(SeriesProperty, CategoryNameProperty, RevealProperty);
        SeriesProperty.Changed.AddClassHandler<DotPlot>((chart, _) => Motion.Replay(chart, RevealProperty, TimeSpan.FromMilliseconds(550)));
    }

    public IReadOnlyList<ChartSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    public string CategoryName { get => GetValue(CategoryNameProperty); set => SetValue(CategoryNameProperty, value); }

    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _pointer = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        _pointer = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        // Fundo transparente para a área toda responder ao mouse.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        ChartSeries[] series = Series?.Where(s => s.Values.Any(v => v > 0)).ToArray() ?? [];
        if (series.Length == 0)
            return;

        int categories = series.Max(s => s.Values.Count);
        double[] all = series.SelectMany(s => s.Values).Where(v => v > 0).ToArray();
        double span = Math.Max(all.Max() - all.Min(), all.Max() * 0.01);
        var (min, max, step) = ChartDrawing.NiceScale(all.Min() - span * 0.15, all.Max() + span * 0.15);

        var plot = new Rect(Left, Top, Bounds.Width - Left - Right, Bounds.Height - Top - Bottom);
        double Y(double value) => plot.Bottom - plot.Height * (value - min) / (max - min);
        double X(int category, int index) =>
            plot.Left + plot.Width * (category + 0.5) / categories + (index - (series.Length - 1) / 2.0) * Spread;

        for (double tick = min; tick <= max + step / 2; tick += step)
        {
            double y = Math.Round(Y(tick)) + 0.5;
            context.DrawLine(ChartDrawing.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            FormattedText label = ChartDrawing.Label(ChartDrawing.Number(tick), 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        for (int category = 0; category < categories; category++)
        {
            FormattedText label = ChartDrawing.Label(category.ToString(), 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(plot.Left + plot.Width * (category + 0.5) / categories - label.Width / 2, plot.Bottom + 6));
        }

        // Anel de 2 px na cor do fundo para os pontos não se fundirem quando encostam.
        var ring = new Pen(ChartDrawing.Surface, 2);
        int? hovered = null;
        for (int index = 0; index < series.Length; index++)
        {
            var brush = new SolidColorBrush(series[index].Color);
            for (int category = 0; category < series[index].Values.Count; category++)
            {
                double value = series[index].Values[category];
                if (value <= 0)
                    continue;

                var center = new Point(X(category, index), Y(value));
                context.DrawEllipse(brush, ring, center, (Radius + 1) * Reveal, (Radius + 1) * Reveal);
                if (_pointer is { } pointer && Math.Abs(pointer.X - (plot.Left + plot.Width * (category + 0.5) / categories)) < plot.Width / categories / 2)
                    hovered = category;
            }
        }

        if (hovered is int column && _pointer is { } position && plot.Contains(position))
        {
            var lines = series.Where(s => column < s.Values.Count && s.Values[column] > 0)
                .Select(s => (s.Color, $"{s.Name}: {ChartDrawing.Number(s.Values[column])} pontos")).ToList();
            ChartDrawing.Tooltip(context, new Rect(Bounds.Size), position, $"{CategoryName} {column}", lines);
        }
    }
}
