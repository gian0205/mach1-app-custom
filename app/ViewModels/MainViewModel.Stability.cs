using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// Teste de estabilidade da curva em vigor, um núcleo por vez.
public partial class MainViewModel
{
    private CancellationTokenSource? _testCancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotTesting))]
    private bool _isTesting;

    [ObservableProperty]
    private decimal? _testSeconds = 60;

    [ObservableProperty]
    private double _testProgress;

    [ObservableProperty]
    private string _testStatus = "";

    public bool IsNotTesting => !IsTesting;

    [RelayCommand]
    private async Task RunStabilityTestAsync()
    {
        if (_machine is not { Tuner: { SupportsCurveOptimizer: true } tuner } machine || IsTesting || !IsReady)
            return;

        var duration = TimeSpan.FromSeconds((double)(TestSeconds ?? 60));
        int count = Cores.Count;
        var failed = new List<int>();

        using var cancellation = new CancellationTokenSource();
        _testCancellation = cancellation;
        IsTesting = true;
        IsReady = false;
        AddLog($"Teste de estabilidade: {count} núcleos, {duration.TotalSeconds:0} s cada.");

        try
        {
            for (int core = 0; core < count && !cancellation.IsCancellationRequested; core++)
            {
                int current = core;
                TestStatus = $"Testando o núcleo {current} de {count - 1}…";
                var progress = new Progress<double>(fraction => TestProgress = 100 * (current + fraction) / count);

                bool consistent = await Task.Run(() =>
                {
                    StabilityTester.MarkTesting(current);
                    return StabilityTester.TestCore(machine.CoreProcessors[Math.Min(current, machine.CoreCount - 1)], duration,
                        ((IProgress<double>)progress).Report, cancellation.Token);
                });

                if (consistent)
                    continue;

                // Erro de cálculo: tira o undervolt deste núcleo na hora e sugere uma margem mais folgada.
                failed.Add(current);
                int margin = Cores[current].CurrentMargin ?? 0;
                int suggestion = Math.Min(0, margin + 5);
                await Task.Run(() => tuner.SetCoreMargin(current, 0));
                Cores[current].Margin = suggestion;
                AddLog($"Núcleo {current}: erro de cálculo com margem {margin:+0;-0;0}. Margem zerada no processador; " +
                       $"deixei {suggestion:+0;-0;0} no campo \"A aplicar\" como sugestão.");
            }
        }
        catch (Exception ex)
        {
            AddLog("Teste interrompido por erro: " + ex.Message);
        }
        finally
        {
            StabilityTester.ClearMark();
            bool cancelled = cancellation.IsCancellationRequested;
            _testCancellation = null;
            IsTesting = false;
            TestProgress = 0;

            TestStatus = cancelled ? "Teste cancelado."
                : failed.Count == 0 ? $"Nenhum erro em {count} núcleos."
                : $"Erro nos núcleos {string.Join(", ", failed)}.";
            AddLog("Teste de estabilidade: " + TestStatus);

            await RefreshAsync();
            IsReady = true;
        }
    }

    [RelayCommand]
    private void CancelStabilityTest() => _testCancellation?.Cancel();

    /// <summary>Se o PC caiu durante um teste, avisa qual núcleo estava em teste.</summary>
    private void ReportInterruptedTest()
    {
        if (StabilityTester.TakeInterruptedCore() is int core)
            AddLog($"O último teste de estabilidade foi interrompido no núcleo {core} (queda ou reinício). " +
                   "Use uma margem menos agressiva nesse núcleo.");
    }
}
