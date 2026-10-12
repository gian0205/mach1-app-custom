using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;

namespace Mach1DriverCustom.Controls;

/// <summary>
/// Barra divergente da margem de um núcleo: parte do zero para a esquerda (negativa) ou para a
/// direita (positiva). Um traço marca a margem que está em vigor no processador, para comparar
/// com a que será aplicada.
/// </summary>
public class MarginBar : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<MarginBar, double>(nameof(Minimum), -40);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<MarginBar, double>(nameof(Maximum), 10);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MarginBar, double>(nameof(Value));

    /// <summary>Margem em vigor; NaN quando desconhecida.</summary>
    public static readonly StyledProperty<double> AppliedProperty =
        AvaloniaProperty.Register<MarginBar, double>(nameof(Applied), double.NaN);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<MarginBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<MarginBar, IBrush?>(nameof(AxisBrush));

    public static readonly StyledProperty<IBrush?> NegativeBrushProperty =
        AvaloniaProperty.Register<MarginBar, IBrush?>(nameof(NegativeBrush));

    public static readonly StyledProperty<IBrush?> PositiveBrushProperty =
        AvaloniaProperty.Register<MarginBar, IBrush?>(nameof(PositiveBrush));

    public static readonly StyledProperty<IBrush?> AppliedBrushProperty =
        AvaloniaProperty.Register<MarginBar, IBrush?>(nameof(AppliedBrush));

    static MarginBar()
    {
        AffectsRender<MarginBar>(MinimumProperty, MaximumProperty, ValueProperty, AppliedProperty,
            TrackBrushProperty, AxisBrushProperty, NegativeBrushProperty, PositiveBrushProperty, AppliedBrushProperty);
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Applied { get => GetValue(AppliedProperty); set => SetValue(AppliedProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }
    public IBrush? NegativeBrush { get => GetValue(NegativeBrushProperty); set => SetValue(NegativeBrushProperty, value); }
    public IBrush? PositiveBrush { get => GetValue(PositiveBrushProperty); set => SetValue(PositiveBrushProperty, value); }
    public IBrush? AppliedBrush { get => GetValue(AppliedBrushProperty); set => SetValue(AppliedBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        const double barHeight = 6;
        double width = Bounds.Width;
        double height = Bounds.Height;
        double range = Maximum - Minimum;
        if (width <= 0 || range <= 0)
            return;

        double X(double v) => Math.Round((Math.Clamp(v, Minimum, Maximum) - Minimum) / range * width);

        double top = Math.Round((height - barHeight) / 2);
        double zero = X(0);

        context.DrawRectangle(TrackBrush, null, new Rect(0, top, width, barHeight), 3, 3);

        double value = X(Value);
        if (Math.Abs(value - zero) >= 1)
        {
            var rect = new Rect(Math.Min(zero, value), top, Math.Abs(value - zero), barHeight);
            context.DrawRectangle(Value < 0 ? NegativeBrush : PositiveBrush, null, rect, 3, 3);
        }

        // Eixo do zero
        context.DrawRectangle(AxisBrush, null, new Rect(zero, top - 4, 1, barHeight + 8));

        if (!double.IsNaN(Applied))
        {
            double applied = Math.Clamp(X(Applied) - 1, 0, width - 3);
            context.DrawRectangle(AppliedBrush, null, new Rect(applied, top - 5, 3, barHeight + 10), 1.5, 1.5);
        }
    }
}
