using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// Bandeja e inicialização junto com o Windows.
public partial class MainViewModel
{
    private static readonly TimeSpan StartupStabilityWindow = TimeSpan.FromMinutes(3);

    /// <summary>Existe a tarefa agendada que abre o app na bandeja a cada logon.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    /// <summary>Aplicar a curva salva quando o app sobe junto com o Windows.</summary>
    [ObservableProperty]
    private bool _applyOnStart;

    /// <summary>Fechar a janela mantém o app na bandeja.</summary>
    [ObservableProperty]
    private bool _closeToTray = true;

    /// <summary>A última aplicação automática derrubou o PC; ela fica suspensa até salvar um perfil novo.</summary>
    [ObservableProperty]
    private bool _startupBlocked;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
            return;

        try
        {
            if (value)
            {
                AddLog(StartupTask.Enable()
                    ? "O app vai abrir na bandeja a cada logon."
                    : "Não foi possível criar a tarefa agendada.");
            }
            else
            {
                StartupTask.Disable();
                AddLog("O app não abre mais junto com o Windows.");
            }
        }
        catch (Exception ex)
        {
            AddLog("Falha na tarefa agendada: " + ex.Message);
        }
    }

    /// <summary>
    /// Quando o app subiu pelo logon e a opção está marcada, aplica a curva salva. Se o PC cair
    /// nos minutos seguintes, <see cref="StartupGuard"/> suspende a aplicação no próximo logon.
    /// </summary>
    private async Task ApplyStoredAtLogonAsync()
    {
        if (Tuner is null || !_startedAtLogon || !ApplyOnStart || _stored.CoreMargins.Length == 0)
            return;

        if (!StartupGuard.Begin())
        {
            StartupBlocked = true;
            AddLog("A curva salva não foi aplicada: o PC caiu logo depois da última aplicação automática.");
            return;
        }

        var curve = new Profile
        {
            CoreMargins = _stored.CoreMargins,
            ChangeLimits = _stored.ChangeLimits,
            Ppt = _stored.Ppt,
            Tdc = _stored.Tdc,
            Edc = _stored.Edc,
        };
        AddLog("Aplicando a curva salva (início com o Windows).");
        await ApplyCurveAsync(curve, verbose: false);

        var confirm = new DispatcherTimer { Interval = StartupStabilityWindow };
        confirm.Tick += (_, _) =>
        {
            confirm.Stop();
            StartupGuard.Confirm();
        };
        confirm.Start();
    }
}
