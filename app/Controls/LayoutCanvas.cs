using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Mach1CustomApp.Services;
using Mach1CustomApp.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1CustomApp.Controls;

/// <summary>
/// A tela do water cooler em escala reduzida, para o editor de layout: mostra o quadro desenhado
/// de verdade e, por cima, a caixa de cada elemento. Clicar seleciona, arrastar move e a alça
/// do canto inferior direito redimensiona.
/// </summary>
public class LayoutCanvas : Control
{
    private const double HandleSize = 12, MinimumSize = 8;

    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<LayoutCanvas, IImage?>(nameof(Source));

    public static readonly StyledProperty<IEnumerable<LayoutElementViewModel>?> ElementsProperty =
        AvaloniaProperty.Register<LayoutCanvas, IEnumerable<LayoutElementViewModel>?>(nameof(Elements));

    public static readonly StyledProperty<LayoutElementViewModel?> SelectedProperty =
        AvaloniaProperty.Register<LayoutCanvas, LayoutElementViewModel?>(nameof(Selected), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> FlippedProperty =
        AvaloniaProperty.Register<LayoutCanvas, bool>(nameof(Flipped));

    private enum Drag { None, Move, Resize }

    private Drag _drag;
    private Point _dragStart;
    private Rect _dragOrigin;

    static LayoutCanvas()
    {
        AffectsRender<LayoutCanvas>(SourceProperty, ElementsProperty, SelectedProperty, FlippedProperty);
        FocusableProperty.OverrideDefaultValue<LayoutCanvas>(true);
    }

    /// <summary>Último quadro desenhado pelo painel.</summary>
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    public IEnumerable<LayoutElementViewModel>? Elements { get => GetValue(ElementsProperty); set => SetValue(ElementsProperty, value); }

    public LayoutElementViewModel? Selected { get => GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }

    /// <summary>O quadro chega girado 180° (tela montada invertida); aqui ele é mostrado em pé.</summary>
    public bool Flipped { get => GetValue(FlippedProperty); set => SetValue(FlippedProperty, value); }

    // Pixels da tela por pixel do controle.
    private double Scale => LcdScreen.Width / Math.Max(1, Bounds.Width);

    private Rect BoxOf(LayoutElementViewModel element) => new(
        (double)(element.X ?? 0) / Scale, (double)(element.Y ?? 0) / Scale,
        (double)(element.Width ?? 0) / Scale, (double)(element.Height ?? 0) / Scale);

    private static Rect HandleOf(Rect box) =>
        new(box.Right - HandleSize / 2, box.Bottom - HandleSize / 2, HandleSize, HandleSize);

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(Brushes.Black, null, area);

        if (Source is { } source)
        {
            if (Flipped)
            {
                using (context.PushTransform(Matrix.CreateRotation(Math.PI) * Matrix.CreateTranslation(area.Width, area.Height)))
                    context.DrawImage(source, area);
            }
            else
            {
                context.DrawImage(source, area);
            }
        }

        var outline = new Pen(new SolidColorBrush(Colors.White, 0.28), 1);
        var selection = new Pen(ChartDrawing.Text, 1.5);
        foreach (LayoutElementViewModel element in Elements ?? [])
        {
            Rect box = BoxOf(element);
            if (!ReferenceEquals(element, Selected))
            {
                context.DrawRectangle(null, outline, box);
                continue;
            }

            // Contorno escuro sob o claro, para aparecer sobre qualquer fundo.
            context.DrawRectangle(null, new Pen(Brushes.Black, 3.5), box);
            context.DrawRectangle(null, selection, box);
            context.DrawRectangle(ChartDrawing.Text, new Pen(Brushes.Black, 1), HandleOf(box));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        Point point = e.GetPosition(this);

        if (Selected is { } current && HandleOf(BoxOf(current)).Inflate(3).Contains(point))
        {
            _drag = Drag.Resize;
        }
        else
        {
            // O de cima primeiro: os elementos são desenhados na ordem da lista.
            Selected = (Elements ?? []).Reverse().FirstOrDefault(element => BoxOf(element).Inflate(3).Contains(point));
            _drag = Selected is null ? Drag.None : Drag.Move;
        }

        if (_drag != Drag.None && Selected is { } element)
        {
            _dragStart = point;
            _dragOrigin = new Rect((double)(element.X ?? 0), (double)(element.Y ?? 0),
                (double)(element.Width ?? 0), (double)(element.Height ?? 0));
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag == Drag.None || Selected is not { } element)
            return;

        Vector delta = (e.GetPosition(this) - _dragStart) * Scale;
        if (_drag == Drag.Move)
        {
            // Pode sair um pouco da tela, mas não a ponto de sumir.
            element.X = (decimal)Math.Round(Math.Clamp(_dragOrigin.X + delta.X, 16 - _dragOrigin.Width, LcdScreen.Width - 16));
            element.Y = (decimal)Math.Round(Math.Clamp(_dragOrigin.Y + delta.Y, 16 - _dragOrigin.Height, LcdScreen.Height - 16));
        }
        else
        {
            element.Width = (decimal)Math.Round(Math.Max(MinimumSize, _dragOrigin.Width + delta.X));
            element.Height = (decimal)Math.Round(Math.Max(MinimumSize, _dragOrigin.Height + delta.Y));
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _drag = Drag.None;
        e.Pointer.Capture(null);
    }

    /// <summary>Setas movem o elemento selecionado 1 pixel; com Shift, 10.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Selected is not { } element)
            return;

        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left: element.X -= step; break;
            case Key.Right: element.X += step; break;
            case Key.Up: element.Y -= step; break;
            case Key.Down: element.Y += step; break;
            default: return;
        }
        e.Handled = true;
        InvalidateVisual();
    }
}
