using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1CustomApp.Controls;

/// <summary>
/// Curva de ventoinha: temperatura no eixo horizontal, velocidade no vertical. Os pontos são
/// arrastados com o mouse; cada um fica preso entre os vizinhos, para a curva nunca voltar.
/// Também mostra a temperatura de agora e a velocidade que a curva dá para ela.
/// </summary>
public class FanCurveEditor : Control
{
    public const double MinTemperature = 20, MaxTemperature = 100;

    private const double Left = 44, Right = 14, Top = 12, Bottom = 26, PointRadius = 6;

    /// <summary>Pontos da curva: X é a temperatura em °C e Y a velocidade em %, em ordem de temperatura.</summary>
    public static readonly StyledProperty<IReadOnlyList<Point>?> PointsProperty =
        AvaloniaProperty.Register<FanCurveEditor, IReadOnlyList<Point>?>(nameof(Points), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Velocidade mínima do conector; a área abaixo dela aparece hachurada e a curva não entra nela.</summary>
    public static readonly StyledProperty<double> FloorProperty =
        AvaloniaProperty.Register<FanCurveEditor, double>(nameof(Floor));

    /// <summary>Temperatura de agora; NaN quando não há leitura.</summary>
    public static readonly StyledProperty<double> CurrentTemperatureProperty =
        AvaloniaProperty.Register<FanCurveEditor, double>(nameof(CurrentTemperature), double.NaN);

    private int _dragging = -1;

    static FanCurveEditor()
    {
        AffectsRender<FanCurveEditor>(PointsProperty, FloorProperty, CurrentTemperatureProperty);
    }

    public IReadOnlyList<Point>? Points { get => GetValue(PointsProperty); set => SetValue(PointsProperty, value); }

    public double Floor { get => GetValue(FloorProperty); set => SetValue(FloorProperty, value); }

    public double CurrentTemperature { get => GetValue(CurrentTemperatureProperty); set => SetValue(CurrentTemperatureProperty, value); }

    /// <summary>Velocidade que a curva dá para uma temperatura: reta entre os pontos, constante fora deles.</summary>
    public static double Evaluate(IReadOnlyList<Point> points, double temperature)
    {
        if (points.Count == 0)
            return 100;
        if (temperature <= points[0].X)
            return points[0].Y;

        for (int i = 1; i < points.Count; i++)
        {
            if (temperature > points[i].X)
                continue;

            Point a = points[i - 1], b = points[i];
            return b.X <= a.X ? b.Y : a.Y + (b.Y - a.Y) * (temperature - a.X) / (b.X - a.X);
        }
        return points[^1].Y;
    }

    private Rect Plot => new(Left, Top, Math.Max(1, Bounds.Width - Left - Right), Math.Max(1, Bounds.Height - Top - Bottom));

    private Point ToScreen(Point point)
    {
        Rect plot = Plot;
        return new Point(
            plot.Left + plot.Width * (point.X - MinTemperature) / (MaxTemperature - MinTemperature),
            plot.Bottom - plot.Height * point.Y / 100);
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        Rect plot = Plot;
        var accent = new SolidColorBrush(ChartDrawing.SeriesColors[0]);

        for (int percent = 0; percent <= 100; percent += 25)
        {
            double y = Math.Round(plot.Bottom - plot.Height * percent / 100) + 0.5;
            context.DrawLine(ChartDrawing.GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            FormattedText label = ChartDrawing.Label($"{percent} %", 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(plot.Left - 8 - label.Width, y - label.Height / 2));
        }
        for (double temperature = MinTemperature; temperature <= MaxTemperature; temperature += 20)
        {
            double x = ToScreen(new Point(temperature, 0)).X;
            FormattedText label = ChartDrawing.Label($"{temperature:0} °C", 11.5, ChartDrawing.Muted);
            context.DrawText(label, new Point(x - label.Width / 2, plot.Bottom + 6));
        }

        // Faixa abaixo do mínimo do conector.
        if (Floor > 0)
        {
            double floorY = plot.Bottom - plot.Height * Floor / 100;
            context.DrawRectangle(new SolidColorBrush(Colors.White, 0.05), null, new Rect(plot.Left, floorY, plot.Width, plot.Bottom - floorY));
        }

        if (Points is not { Count: > 0 } points)
            return;

        // A curva segue constante até a primeira temperatura e depois da última.
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(plot.Left, ToScreen(points[0]).Y), false);
            foreach (Point point in points)
                path.LineTo(ToScreen(point));
            path.LineTo(new Point(plot.Right, ToScreen(points[^1]).Y));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(accent, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);

        var ring = new Pen(ChartDrawing.Surface, 2);
        for (int i = 0; i < points.Count; i++)
            context.DrawEllipse(i == _dragging ? ChartDrawing.Text : accent, ring, ToScreen(points[i]), PointRadius, PointRadius);

        // Onde a temperatura de agora cai na curva.
        if (!double.IsNaN(CurrentTemperature))
        {
            double temperature = Math.Clamp(CurrentTemperature, MinTemperature, MaxTemperature);
            Point now = ToScreen(new Point(temperature, Evaluate(points, temperature)));
            double x = Math.Round(now.X) + 0.5;
            context.DrawLine(new Pen(ChartDrawing.Muted, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
            context.DrawEllipse(ChartDrawing.Text, ring, now, 4, 4);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (Points is not { } points)
            return;

        Point pointer = e.GetPosition(this);
        _dragging = -1;
        double best = 16;
        for (int i = 0; i < points.Count; i++)
        {
            Point screen = ToScreen(points[i]);
            double distance = Math.Sqrt(Math.Pow(screen.X - pointer.X, 2) + Math.Pow(screen.Y - pointer.Y, 2));
            if (distance < best)
            {
                best = distance;
                _dragging = i;
            }
        }

        if (_dragging >= 0)
            e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_dragging < 0 || Points is not { } points)
            return;

        Rect plot = Plot;
        Point pointer = e.GetPosition(this);
        double temperature = MinTemperature + (MaxTemperature - MinTemperature) * (pointer.X - plot.Left) / plot.Width;
        double percent = 100 * (plot.Bottom - pointer.Y) / plot.Height;

        // Entre os vizinhos na temperatura, e nunca abaixo do mínimo do conector.
        double low = _dragging > 0 ? points[_dragging - 1].X + 1 : MinTemperature;
        double high = _dragging < points.Count - 1 ? points[_dragging + 1].X - 1 : MaxTemperature;
        var moved = new Point(Math.Round(Math.Clamp(temperature, low, high)), Math.Round(Math.Clamp(percent, Floor, 100)));

        Point[] updated = points.ToArray();
        updated[_dragging] = moved;
        Points = updated;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _dragging = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }
}
