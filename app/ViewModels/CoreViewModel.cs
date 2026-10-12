using CommunityToolkit.Mvvm.ComponentModel;

namespace Mach1DriverCustom.ViewModels;

public partial class CoreViewModel(int index, int minMargin) : ViewModelBase
{
    public int Index { get; } = index;

    /// <summary>Menor margem que este processador aceita.</summary>
    public int MinMargin { get; } = minMargin;

    public int MaxMargin => Services.CpuTuner.MaxMargin;

    public string Label => $"Núcleo {Index}";

    /// <summary>Margem a aplicar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarginValue))]
    private decimal? _margin = 0;

    public double MarginValue => (double)(Margin ?? 0);

    /// <summary>Margem em vigor no processador, lida do SMU; null quando a leitura falha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(AppliedValue))]
    private int? _currentMargin;

    public string Current => CurrentMargin is int m ? m.ToString("+0;−0;0") : "—";

    public double AppliedValue => CurrentMargin ?? double.NaN;

    [ObservableProperty]
    private string _clock = "—";
}
