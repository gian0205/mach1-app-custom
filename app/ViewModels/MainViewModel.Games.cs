using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1DriverCustom.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1DriverCustom.ViewModels;

// Perfis por jogo: enquanto um executável da lista está aberto, a margem dele vale para todos
// os núcleos; quando fecha, volta a curva que está na janela.
public partial class MainViewModel
{
    private GameProfile? _activeGame;

    public ObservableCollection<GameProfile> Games { get; } = [];

    [ObservableProperty]
    private string _newGameExecutable = "";

    [ObservableProperty]
    private decimal? _newGameMargin = -10;

    /// <summary>Texto com o jogo cuja margem está em uso, ou vazio.</summary>
    [ObservableProperty]
    private string _activeGameText = "";

    [RelayCommand]
    private void AddGame()
    {
        string executable = Path.GetFileName(NewGameExecutable.Trim().Trim('"'));
        if (executable.Length == 0)
            return;

        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            executable += ".exe";

        if (Games.Any(g => g.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase)))
        {
            AddLog($"{executable} já está na lista.");
            return;
        }

        Games.Add(new GameProfile { Executable = executable, Margin = (int)(NewGameMargin ?? 0) });
        NewGameExecutable = "";
        SaveSettings();
    }

    [RelayCommand]
    private void RemoveGame(GameProfile game)
    {
        Games.Remove(game);
        SaveSettings();
    }

    /// <summary>Vê se algum jogo da lista abriu ou fechou e troca a curva de acordo.</summary>
    private async Task CheckGamesAsync()
    {
        if (!CanTune || (Games.Count == 0 && _activeGame is null))
            return;

        HashSet<string> running = await Task.Run(() =>
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                names.Add(process.ProcessName);
                process.Dispose();
            }
            return names;
        });

        GameProfile? match = Games.FirstOrDefault(g => running.Contains(Path.GetFileNameWithoutExtension(g.Executable)));
        if (ReferenceEquals(match, _activeGame))
            return;

        _activeGame = match;
        Profile curve;
        if (match is null)
        {
            ActiveGameText = "";
            curve = BuildCurve();
            curve.ChangeLimits = false;
            AddLog("Jogo fechado: de volta à curva da janela.");
        }
        else
        {
            ActiveGameText = $"Em uso agora: {match.Summary}";
            curve = new Profile { CoreMargins = Enumerable.Repeat(match.Margin, Cores.Count).ToArray() };
            AddLog($"{match.Executable} aberto: margem {match.Margin:+0;-0;0} em todos os núcleos.");
        }

        await ApplyCurveAsync(curve, verbose: false);
    }
}
