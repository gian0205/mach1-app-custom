using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Mach1DriverCustom.Views;

/// <summary>
/// A roda do mouse sobre um controle deslizante, uma caixa de número ou uma lista fechada
/// mudaria o valor dele; quem está só rolando a página altera opções sem perceber (foi assim
/// que o brilho da tela foi parar em zero). Com isto a roda rola a página.
/// </summary>
public static class WheelGuard
{
    public static void Attach(Window window) =>
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is not Visual source)
            return;

        Control? input = source.FindAncestorOfType<Slider>(includeSelf: true)
            ?? source.FindAncestorOfType<NumericUpDown>(includeSelf: true)
            ?? (Control?)source.FindAncestorOfType<ComboBox>(includeSelf: true);
        if (input is null || input is ComboBox { IsDropDownOpen: true })
            return;

        e.Handled = true;
        if (input.FindAncestorOfType<ScrollViewer>() is { } scroll)
            scroll.Offset = scroll.Offset.WithY(scroll.Offset.Y - e.Delta.Y * 60);
    }
}
