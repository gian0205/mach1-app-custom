using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mach1CustomApp.Controls;

/// <summary>
/// Movimento que não cabe em estilos: entrada em sequência dos itens de uma lista e um pulso
/// quando um valor muda. Nada roda com <see cref="Enabled"/> desligado, que é como o app fica
/// quando o Windows está com as animações desativadas.
/// </summary>
public static class Motion
{
    private const int MaxStaggered = 20;

    /// <summary>Posição do item na lista; cada um entra um pouco depois do anterior.</summary>
    public static readonly AttachedProperty<int> StaggerIndexProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("StaggerIndex", typeof(Motion), -1);

    /// <summary>Quando o valor ligado aqui muda, o controle dá um pulso para chamar a atenção.</summary>
    public static readonly AttachedProperty<object?> PulseKeyProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("PulseKey", typeof(Motion));

    static Motion()
    {
        StaggerIndexProperty.Changed.AddClassHandler<Control>(OnStaggerIndexChanged);
        PulseKeyProperty.Changed.AddClassHandler<Control>(OnPulseKeyChanged);
    }

    public static bool Enabled { get; set; }

    public static int GetStaggerIndex(Control control) => control.GetValue(StaggerIndexProperty);

    public static void SetStaggerIndex(Control control, int value) => control.SetValue(StaggerIndexProperty, value);

    public static object? GetPulseKey(Control control) => control.GetValue(PulseKeyProperty);

    public static void SetPulseKey(Control control, object? value) => control.SetValue(PulseKeyProperty, value);

    /// <summary>
    /// Faz uma propriedade de 0 a 1 ir de novo do zero ao fim, para um gráfico se "desenhar"
    /// quando recebe dados. Sem animações, vai direto para 1.
    /// </summary>
    public static void Replay(Control control, StyledProperty<double> property, TimeSpan duration)
    {
        if (!Enabled)
        {
            control.SetCurrentValue(property, 1.0);
            return;
        }

        control.Transitions = null;
        control.SetCurrentValue(property, 0.0);
        control.Transitions = [new DoubleTransition { Property = property, Duration = duration, Easing = new CubicEaseOut() }];
        Dispatcher.UIThread.Post(() => control.SetCurrentValue(property, 1.0), DispatcherPriority.Loaded);
    }

    private static void OnStaggerIndexChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (!Enabled || e.NewValue is not int index || index < 0)
            return;

        // Entra pela esquerda, cada item 45 ms depois do anterior.
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(380),
            Delay = TimeSpan.FromMilliseconds(45 * Math.Min(index, MaxStaggered)),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, -26.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } },
            },
        };
        RunWhenAttached(control, animation);
    }

    private static void OnPulseKeyChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // A primeira atribuição é só o valor inicial, não uma mudança.
        if (!Enabled || e.OldValue is null || Equals(e.OldValue, e.NewValue) || !control.IsEffectivelyVisible)
            return;

        control.RenderTransformOrigin = RelativePoint.Center;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(520),
            Easing = new BackEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.7), new Setter(ScaleTransform.ScaleYProperty, 1.7), new Setter(Visual.OpacityProperty, 0.3) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0), new Setter(Visual.OpacityProperty, 1.0) } },
            },
        };
        _ = animation.RunAsync(control);
    }

    private static void RunWhenAttached(Control control, Animation animation)
    {
        if (control.IsAttachedToVisualTree())
        {
            _ = animation.RunAsync(control);
            return;
        }

        void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            control.AttachedToVisualTree -= OnAttached;
            _ = animation.RunAsync(control);
        }
        control.AttachedToVisualTree += OnAttached;
    }
}

/// <summary>
/// Troca de página em que a antiga sai para um lado apagando e a nova entra do outro: para a
/// direita ao avançar nas abas, para a esquerda ao voltar.
/// </summary>
public sealed class SlideFadeTransition : IPageTransition
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(280);

    /// <summary>Distância que cada página percorre, em pixels.</summary>
    public double Offset { get; set; } = 44;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        double direction = forward ? 1 : -1;
        Task leave = Task.CompletedTask, enter = Task.CompletedTask;

        if (from is not null)
        {
            leave = new Animation
            {
                Duration = Duration * 0.55,
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, -direction * Offset) } },
                },
            }.RunAsync(from, cancellationToken);
        }

        if (to is not null)
        {
            to.IsVisible = true;
            enter = new Animation
            {
                Duration = Duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, direction * Offset) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } },
                },
            }.RunAsync(to, cancellationToken);
        }

        await Task.WhenAll(leave, enter);

        if (from is not null && !cancellationToken.IsCancellationRequested)
            from.IsVisible = false;
    }
}
