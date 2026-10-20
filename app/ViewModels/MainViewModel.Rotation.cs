using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mach1CustomApp.ViewModels;

// Rodízio de telas: as páginas escolhidas se revezam na tela, cada uma pelo seu tempo.
public partial class MainViewModel
{
    private const int RotationPageMode = 2;

    /// <summary>As páginas que podem entrar no rodízio, na ordem inicial.</summary>
    private static readonly (int Mode, string Name)[] RotationPages =
    [
        (0, "Temperaturas e sensores"),
        (1, "Curve Optimizer"),
        (4, "Gráfico de temperatura e uso"),
        (5, "Medidores"),
        (6, "Relógio"),
        (7, "Minimalista"),
        (8, "Blocos com minigráficos"),
        (WeatherPageMode, "Clima"),
        (CustomPageMode, "Layout personalizado"),
    ];

    /// <summary>As páginas do rodízio, na ordem em que passam.</summary>
    public ObservableCollection<RotationPageViewModel> LcdRotation { get; } = [];

    public bool IsRotation => LcdPageMode == RotationPageMode;

    private void LoadRotation(Profile profile)
    {
        LcdRotation.Clear();
        int seconds = Math.Clamp(profile.LcdPageSeconds, 2, 600);

        // Primeiro a ordem gravada; depois as páginas que não estavam nela (vindas de versão nova).
        foreach (RotationItem item in profile.LcdRotation)
        {
            if (RotationPages.Any(p => p.Mode == item.Mode) && LcdRotation.All(r => r.Mode != item.Mode))
                AddRotationPage(item.Mode, item.Enabled, item.Seconds);
        }
        foreach ((int mode, _) in RotationPages)
        {
            // Sem rodízio gravado, começa como o antigo "alternar": temperaturas e Curve Optimizer.
            if (LcdRotation.All(r => r.Mode != mode))
                AddRotationPage(mode, profile.LcdRotation.Count == 0 && mode is 0 or 1, seconds);
        }
    }

    private void AddRotationPage(int mode, bool enabled, int seconds) =>
        LcdRotation.Add(new RotationPageViewModel(mode, RotationPages.First(p => p.Mode == mode).Name, enabled,
            Math.Clamp(seconds, 2, 600), MoveRotationPage, OnRotationChanged));

    private void StoreRotation(Profile profile) =>
        profile.LcdRotation = LcdRotation
            .Select(r => new RotationItem { Mode = r.Mode, Enabled = r.Enabled, Seconds = (int)(r.Seconds ?? 8) })
            .ToList();

    /// <summary>As páginas marcadas, como o laço de desenho as percorre.</summary>
    private LcdRotationStep[] RotationSteps() =>
        LcdRotation.Where(r => r.Enabled).Select(r => new LcdRotationStep(r.Mode, (int)(r.Seconds ?? 8))).ToArray();

    private void OnRotationChanged()
    {
        if (_loading)
            return;

        PushLcdSettings();
        // Clima no rodízio precisa da previsão.
        if (IsRotation && LcdRotation.Any(r => r.Enabled && r.Mode == WeatherPageMode))
            WeatherEnabled = true;
        SaveSettings();
    }

    private void MoveRotationPage(RotationPageViewModel page, int delta)
    {
        int from = LcdRotation.IndexOf(page), to = from + delta;
        if (from < 0 || to < 0 || to >= LcdRotation.Count)
            return;

        LcdRotation.Move(from, to);
        OnRotationChanged();
    }
}

/// <summary>Uma página na lista do rodízio.</summary>
public partial class RotationPageViewModel(int mode, string name, bool enabled, int seconds,
    Action<RotationPageViewModel, int> move, Action changed) : ObservableObject
{
    public int Mode { get; } = mode;

    public string Name { get; } = name;

    [ObservableProperty]
    private bool _enabled = enabled;

    /// <summary>Quanto tempo a página fica na tela, em segundos.</summary>
    [ObservableProperty]
    private decimal? _seconds = seconds;

    partial void OnEnabledChanged(bool value) => changed();

    partial void OnSecondsChanged(decimal? value) => changed();

    [RelayCommand]
    private void MoveUp() => move(this, -1);

    [RelayCommand]
    private void MoveDown() => move(this, 1);
}
