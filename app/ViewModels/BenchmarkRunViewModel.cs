using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mach1DriverCustom.Controls;
using Mach1DriverCustom.Models;
using System;

namespace Mach1DriverCustom.ViewModels;

/// <summary>Um benchmark salvo, na lista do histórico.</summary>
public partial class BenchmarkRunViewModel(BenchmarkRun run, Action<BenchmarkRunViewModel> selectionChanged) : ViewModelBase
{
    public BenchmarkRun Run { get; } = run;

    public string Title => Run.Name;

    public string Subtitle =>
        $"{Run.Time:dd/MM HH:mm}, {ChartDrawing.Number(Run.SingleScore)} e {ChartDrawing.Number(Run.MultiScore)} pontos";

    /// <summary>Marcado para aparecer na comparação.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Posição na comparação, que define a cor; -1 quando não está marcado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Swatch))]
    private int _slot = -1;

    public Color Color => Slot >= 0 ? ChartDrawing.SeriesColors[Slot] : Colors.Transparent;

    public IBrush Swatch => new SolidColorBrush(Color);

    partial void OnIsSelectedChanged(bool value) => selectionChanged(this);
}

/// <summary>Uma linha da tabela de comparação, já formatada.</summary>
public sealed record BenchmarkRow(IBrush Swatch, string Name, string Single, string Multi, string Memory,
    string Temperature, string SingleClock, string MultiClock, string Watts);
