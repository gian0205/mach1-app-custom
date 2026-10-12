using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1DriverCustom.Controls;
using Mach1DriverCustom.Models;
using Mach1DriverCustom.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mach1DriverCustom.ViewModels;

// Benchmark: rodar, guardar o histórico e montar a comparação entre até três testes.
public partial class MainViewModel
{
    private static readonly int MaxCompared = ChartDrawing.SeriesColors.Length;

    private CancellationTokenSource? _benchCancellation;

    public ObservableCollection<BenchmarkRunViewModel> Benchmarks { get; } = [];

    [ObservableProperty]
    private string _benchName = "";

    [ObservableProperty]
    private decimal? _benchSeconds = 15;

    [ObservableProperty]
    private bool _benchPerCore = true;

    [ObservableProperty]
    private bool _benchMemory = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBenchmarking))]
    private bool _isBenchmarking;

    [ObservableProperty]
    private double _benchProgress;

    [ObservableProperty]
    private string _benchStatus = "";

    // Comparação entre os testes marcados, na ordem das cores.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoComparison))]
    private bool _hasComparison;

    [ObservableProperty]
    private IReadOnlyList<BenchmarkRunViewModel> _compared = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _singleBars = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _multiBars = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _memoryBars = [];

    [ObservableProperty]
    private IReadOnlyList<ChartSeries> _coreDots = [];

    [ObservableProperty]
    private bool _hasCoreDots;

    [ObservableProperty]
    private IReadOnlyList<TimeSeries> _temperatureLines = [];

    [ObservableProperty]
    private IReadOnlyList<TimeSeries> _clockLines = [];

    [ObservableProperty]
    private IReadOnlyList<TimeMarker> _phaseMarkers = [];

    [ObservableProperty]
    private IReadOnlyList<BenchmarkRow> _benchmarkRows = [];

    [ObservableProperty]
    private string _comparisonNote = "";

    public bool IsNotBenchmarking => !IsBenchmarking;

    public bool HasNoComparison => !HasComparison;

    private void LoadBenchmarks()
    {
        Benchmarks.Clear();
        try
        {
            foreach (BenchmarkRun run in BenchmarkStore.Load().OrderByDescending(r => r.Time))
                Benchmarks.Add(new BenchmarkRunViewModel(run, OnBenchmarkSelectionChanged));
        }
        catch (Exception ex)
        {
            AddLog("Histórico de benchmarks ilegível: " + ex.Message);
        }

        // Abre já comparando os dois testes mais recentes; o mais antigo primeiro, como base.
        foreach (BenchmarkRunViewModel item in Benchmarks.Take(2).Reverse())
            item.IsSelected = true;
    }

    private void SaveBenchmarks()
    {
        try
        {
            BenchmarkStore.Save(Benchmarks.Select(b => b.Run));
        }
        catch (Exception ex)
        {
            AddLog("Falha ao gravar o histórico de benchmarks: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        if (_machine is not { } machine || IsBenchmarking || !IsReady)
            return;

        string name = BenchName.Trim();
        if (name.Length == 0)
            name = DefaultBenchmarkName();
        var options = new BenchmarkOptions(name, (int)(BenchSeconds ?? 15), BenchPerCore, BenchMemory);

        using var cancellation = new CancellationTokenSource();
        _benchCancellation = cancellation;
        IsBenchmarking = true;
        IsReady = false;
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            BenchProgress = 100 * p.Fraction;
            BenchStatus = p.Status;
        });

        try
        {
            BenchmarkRun run = await Task.Run(() => BenchmarkRunner.Run(options, machine, progress, cancellation.Token));

            var item = new BenchmarkRunViewModel(run, OnBenchmarkSelectionChanged);
            Benchmarks.Insert(0, item);
            SaveBenchmarks();
            item.IsSelected = true;

            BenchStatus = $"Concluído: {ChartDrawing.Number(run.SingleScore)} pontos em 1 núcleo, {ChartDrawing.Number(run.MultiScore)} em todos.";
            AddLog($"Benchmark \"{run.Name}\": {BenchStatus}");
        }
        catch (OperationCanceledException)
        {
            BenchStatus = "Benchmark cancelado.";
        }
        catch (Exception ex)
        {
            BenchStatus = "O benchmark falhou: " + ex.Message;
            AddLog(BenchStatus);
        }
        finally
        {
            _benchCancellation = null;
            IsBenchmarking = false;
            BenchProgress = 0;
            IsReady = true;
        }
    }

    [RelayCommand]
    private void CancelBenchmark() => _benchCancellation?.Cancel();

    [RelayCommand]
    private void RemoveBenchmark(BenchmarkRunViewModel item)
    {
        item.IsSelected = false;
        Benchmarks.Remove(item);
        SaveBenchmarks();
    }

    /// <summary>Nome sugerido a partir da curva em vigor, por exemplo "Curva -15".</summary>
    private string DefaultBenchmarkName()
    {
        // Sem ajuste de curva não há margem para dar nome ao teste.
        if (Cores.Count == 0)
            return $"Teste das {DateTime.Now:HH:mm}";

        int?[] margins = Cores.Select(c => c.CurrentMargin).ToArray();
        return margins.Length > 0 && margins.All(m => m is not null && m == margins[0])
            ? $"Curva {margins[0]:+0;-0;0}"
            : "Curva por núcleo";
    }

    private void OnBenchmarkSelectionChanged(BenchmarkRunViewModel changed)
    {
        if (changed.IsSelected)
        {
            // A cor é a primeira posição livre e não muda enquanto o teste estiver marcado.
            int[] used = Benchmarks.Where(b => b.Slot >= 0).Select(b => b.Slot).ToArray();
            int free = Enumerable.Range(0, MaxCompared).FirstOrDefault(s => !used.Contains(s), -1);
            if (free < 0)
            {
                changed.IsSelected = false;
                BenchStatus = $"Dá para comparar até {MaxCompared} testes; desmarque um antes.";
                return;
            }
            changed.Slot = free;
        }
        else
        {
            changed.Slot = -1;
        }

        RebuildComparison();
    }

    private void RebuildComparison()
    {
        BenchmarkRunViewModel[] selected = Benchmarks.Where(b => b.Slot >= 0).OrderBy(b => b.Slot).ToArray();
        Compared = selected;
        HasComparison = selected.Length > 0;

        ChartSeries[] Bars(Func<BenchmarkRun, double> value) =>
            selected.Select(b => new ChartSeries(b.Title, b.Color, [value(b.Run)])).ToArray();

        SingleBars = Bars(r => r.SingleScore);
        MultiBars = Bars(r => r.MultiScore);
        MemoryBars = Bars(r => r.MemoryGbps);

        CoreDots = selected.Select(b => new ChartSeries(b.Title, b.Color, b.Run.CoreScores)).ToArray();
        HasCoreDots = selected.Any(b => b.Run.CoreScores.Length > 0);

        TimeSeries[] Lines(Func<BenchmarkSample, double?> value) => selected.Select(b => new TimeSeries(b.Title, b.Color,
            b.Run.Samples.Where(s => value(s) is > 0).Select(s => new Point(s.Time, value(s)!.Value)).ToArray())).ToArray();

        TemperatureLines = Lines(s => s.Temperature);
        ClockLines = Lines(s => s.ClockMax);

        // Marcas de etapa do primeiro teste: onde cada etapa começa.
        var markers = new List<TimeMarker>();
        string previous = BenchmarkPhase.Pause;
        foreach (BenchmarkSample sample in selected.FirstOrDefault()?.Run.Samples ?? [])
        {
            if (sample.Phase != previous && sample.Phase != BenchmarkPhase.Pause)
                markers.Add(new TimeMarker(sample.Time, BenchmarkPhase.Label(sample.Phase)));
            previous = sample.Phase;
        }
        PhaseMarkers = markers;

        BenchmarkRows = selected.Select(b => new BenchmarkRow(
            b.Swatch,
            b.Title,
            ChartDrawing.Number(b.Run.SingleScore),
            ChartDrawing.Number(b.Run.MultiScore),
            b.Run.MemoryGbps > 0 ? ChartDrawing.Number(b.Run.MemoryGbps, 1) : "—",
            b.Run.TemperatureMax is float t ? t.ToString("0.0", CultureInfo.InvariantCulture) : "—",
            b.Run.SingleClock > 0 ? ChartDrawing.Number(b.Run.SingleClock) : "—",
            b.Run.MultiClock > 0 ? ChartDrawing.Number(b.Run.MultiClock) : "—",
            b.Run.MultiWatts is float w ? ChartDrawing.Number(w) : "—")).ToArray();

        ComparisonNote = selected.Select(b => (b.Run.Seconds, b.Run.CoreScores.Length > 0, b.Run.MemoryGbps > 0)).Distinct().Count() > 1
            ? "Os testes marcados têm durações ou etapas diferentes: as marcas de etapa nos gráficos de tempo são as do primeiro da legenda."
            : "";
    }
}
