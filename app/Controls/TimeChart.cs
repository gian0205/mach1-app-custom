using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1DriverCustom.Controls;

/// <summary>
/// Linhas no tempo, uma por teste, com uma só medida por gráfico. Passar o mouse mostra o valor
/// de cada teste naquele instante.
/// </summary>
public class TimeChart : Control
{
    private const double Left = 56, Right = 12, Top = 22, Bottom = 26;

    public static readonly StyledProperty<IReadOnlyList<TimeSeries>?> SeriesProperty =
        AvaloniaProperty.Register<TimeChart, IReadOnlyList<TimeSeries>?>(nameof(Series));

    public static readonly StyledProperty<IReadOnlyList<TimeMarker>?> MarkersProperty =
        AvaloniaProperty.Register<TimeChart, IReadOnlyList<TimeMarker>?>(nameof(Markers));

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<TimeChart, string>(nameof(Unit), "");

    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<TimeChart, int>(nameof(Decimals));

    private Point? _pointer;

    /// <summary>De 0 a 1: quanto das linhas já foi traçado, da esquerda para a direita.</summary>
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<TimeChart, double>(nameof(Reveal), 1);

    static TimeChart()
    {
        AffectsRender<TimeChart>(SeriesProperty, MarkersProperty, UnitProperty, DecimalsProperty, RevealProperty);
        SeriesProperty.Changed.AddClassHandler<TimeChart>((chart, _) => Motion.Replay(chart, RevealProperty, TimeSpan.FromMilliseconds(900)));
    }

    public IReadOnlyList<TimeSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    /// <summary>Inícios de etapa, desenhados como linhas verticais com rótulo.</summary>
    public IReadOnlyList<TimeMarker>? Markers { get => GetValue(MarkersProperty); set => SetValue(MarkersProperty, value); }

    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

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

        TimeSeries[] series = Series?.Where(s => s.Points.Count > 1).ToArray() ?? [];
        if (series.Length == 0)
            return;

        Point[] all = series.SelectMany(s => s.Points).ToArray();
        double duration = all.Max(p => p.X);
        double span = Math.Max(all.Max(p => p.Y) - all.Min(p => p.Y), 1);
        var (min, max, step) = ChartDrawing.NiceScale(all.Min(p => p.Y) - span * 0.08, all.Max(p => p.Y) + span * 0.08);

        var plot = new Rect(Left, Top, Bounds.Width - Left - Right, Bounds.Height - Top - Bottom);
        double X(double time) => plot.Left + plot.Width * time / duration;
        double Y(double value) => plot.Bottom - plot.Height * (value - min) / (max - min);

        for (double tick = min; tick <= max + step / 2; tick += step)
        {
            double y = Math.Round(Y(tick)) + 0.5;
            context.DrawLine(ChartDrawing.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            FormattedText label = ChartDrawing.Label(ChartDrawing.Number(tick, Decimals), 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }

        var (_, _, timeStep) = ChartDrawing.NiceScale(0, duration, 6);
        for (double tick = 0; tick <= duration; tick += timeStep)
        {
            FormattedText label = ChartDrawing.Label($"{tick:0} s", 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(X(tick) - label.Width / 2, plot.Bottom + 6));
        }

        foreach (TimeMarker marker in Markers ?? [])
        {
            if (marker.Time > duration)
                continue;

            double x = Math.Round(X(marker.Time)) + 0.5;
            context.DrawLine(ChartDrawing.GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawText(ChartDrawing.Label(marker.Label, 11.5, ChartDrawing.Muted), new Point(x + 5, plot.Top - 18));
        }

        // As linhas aparecem da esquerda para a direita enquanto Reveal vai de 0 a 1.
        // Sem "using": o recorte é desfeito à mão logo depois das linhas, antes da mira e da dica.
        DrawingContext.PushedState reveal = context.PushClip(new Rect(plot.Left - 2, 0, (plot.Width + 4) * Reveal, Bounds.Height));
        foreach (TimeSeries line in series)
        {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(new Point(X(line.Points[0].X), Y(line.Points[0].Y)), false);
                foreach (Point point in line.Points.Skip(1))
                    path.LineTo(new Point(X(point.X), Y(point.Y)));
                path.EndFigure(false);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(line.Color), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        }

        reveal.Dispose();
        if (_pointer is not { } pointer || !plot.Contains(pointer))
            return;

        // Mira: o valor de cada teste no instante sob o cursor.
        double time = duration * (pointer.X - plot.Left) / plot.Width;
        double crosshair = Math.Round(pointer.X) + 0.5;
        context.DrawLine(new Pen(ChartDrawing.Muted, 1), new Point(crosshair, plot.Top), new Point(crosshair, plot.Bottom));

        var ring = new Pen(ChartDrawing.Surface, 2);
        var lines = new List<(Color, string)>();
        foreach (TimeSeries line in series)
        {
            Point nearest = line.Points.MinBy(p => Math.Abs(p.X - time));
            if (Math.Abs(nearest.X - time) > 2)
                continue;

            context.DrawEllipse(new SolidColorBrush(line.Color), ring, new Point(X(nearest.X), Y(nearest.Y)), 5, 5);
            lines.Add((line.Color, $"{line.Name}: {ChartDrawing.Number(nearest.Y, Decimals)} {Unit}"));
        }

        if (lines.Count > 0)
            ChartDrawing.Tooltip(context, new Rect(Bounds.Size), pointer, $"{time:0.0} s", lines);
    }
}
