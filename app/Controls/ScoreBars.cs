using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1DriverCustom.Controls;

/// <summary>
/// Uma medida, uma barra horizontal por teste, todas a partir do zero. Como a diferença entre
/// dois ajustes costuma ser de poucos por cento, cada barra traz o valor e, da segunda em
/// diante, a diferença para a primeira.
/// </summary>
public class ScoreBars : Control
{
    private const double RowHeight = 28, BarHeight = 14, NameWidth = 170, ValueWidth = 170;

    public static readonly StyledProperty<IReadOnlyList<ChartSeries>?> SeriesProperty =
        AvaloniaProperty.Register<ScoreBars, IReadOnlyList<ChartSeries>?>(nameof(Series));

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<ScoreBars, string>(nameof(Unit), "");

    public static readonly StyledProperty<int> DecimalsProperty =
        AvaloniaProperty.Register<ScoreBars, int>(nameof(Decimals));

    /// <summary>De 0 a 1: quanto das barras já cresceu. Recomeça a cada troca de dados.</summary>
    public static readonly StyledProperty<double> RevealProperty =
        AvaloniaProperty.Register<ScoreBars, double>(nameof(Reveal), 1);

    static ScoreBars()
    {
        AffectsRender<ScoreBars>(SeriesProperty, UnitProperty, DecimalsProperty, RevealProperty);
        SeriesProperty.Changed.AddClassHandler<ScoreBars>((chart, _) => Motion.Replay(chart, RevealProperty, System.TimeSpan.FromMilliseconds(650)));
        AffectsMeasure<ScoreBars>(SeriesProperty);
    }

    /// <summary>Uma série por teste; só o primeiro valor de cada uma é usado.</summary>
    public IReadOnlyList<ChartSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    public double Reveal { get => GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, RowHeight * (Series?.Count ?? 0));

    public override void Render(DrawingContext context)
    {
        if (Series is not { Count: > 0 } series)
            return;

        double[] values = series.Select(s => s.Values.Count > 0 ? s.Values[0] : 0).ToArray();
        double max = values.Max();
        double barSpace = Math.Max(40, Bounds.Width - NameWidth - ValueWidth);
        double baseline = values[0];

        for (int i = 0; i < series.Count; i++)
        {
            double center = RowHeight * i + RowHeight / 2;

            FormattedText name = ChartDrawing.Label(series[i].Name, 12.5, ChartDrawing.Text);
            name.MaxTextWidth = NameWidth - 12;
            name.MaxLineCount = 1;
            name.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(name, new Point(0, center - name.Height / 2));

            if (values[i] <= 0)
            {
                context.DrawText(ChartDrawing.Label("não medido", 12.5, ChartDrawing.Muted), new Point(NameWidth, center - 8));
                continue;
            }

            // Ponta de dados arredondada, base reta no zero.
            double width = Math.Max(2, barSpace * values[i] / max * Reveal);
            var bar = new Rect(NameWidth, center - BarHeight / 2, width, BarHeight);
            context.DrawRectangle(new SolidColorBrush(series[i].Color), null, new RoundedRect(bar, 0, 4, 4, 0));

            string value = ChartDrawing.Number(values[i], Decimals) + (Unit.Length > 0 ? " " + Unit : "");
            FormattedText valueText = ChartDrawing.Label(value, 12.5, ChartDrawing.Text, FontWeight.SemiBold);
            context.DrawText(valueText, new Point(bar.Right + 8, center - valueText.Height / 2));

            if (i > 0 && baseline > 0)
            {
                double delta = 100 * (values[i] - baseline) / baseline;
                FormattedText deltaText = ChartDrawing.Label($"{delta:+0.0;-0.0;0.0} %", 12.5, ChartDrawing.Muted);
                context.DrawText(deltaText, new Point(bar.Right + 8 + valueText.Width + 10, center - deltaText.Height / 2));
            }
        }
    }
}
