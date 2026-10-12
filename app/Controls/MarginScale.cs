using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mach1DriverCustom.Controls;

/// <summary>
/// Rótulos da escala das barras de margem (mínimo, zero e máximo), alinhados com as
/// <see cref="MarginBar"/> da mesma coluna. A faixa muda conforme o processador.
/// </summary>
public class MarginScale : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<MarginScale, double>(nameof(Minimum), -40);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<MarginScale, double>(nameof(Maximum), 10);

    static MarginScale()
    {
        AffectsRender<MarginScale>(MinimumProperty, MaximumProperty);
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }

    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public override void Render(DrawingContext context)
    {
        double range = Maximum - Minimum;
        if (range <= 0)
            return;

        FormattedText Label(double value) => ChartDrawing.Label(value.ToString("+0;−0;0"), 12.5, ChartDrawing.Muted);

        FormattedText min = Label(Minimum), zero = Label(0), max = Label(Maximum);
        double zeroX = Bounds.Width * -Minimum / range;
        context.DrawText(min, new Point(0, 0));
        context.DrawText(zero, new Point(zeroX - zero.Width / 2, 0));
        context.DrawText(max, new Point(Bounds.Width - max.Width, 0));
    }
}
