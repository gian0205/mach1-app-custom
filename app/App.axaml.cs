using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Mach1DriverCustom.Services;
using Mach1DriverCustom.ViewModels;
using Mach1DriverCustom.Views;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mach1DriverCustom;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private EventWaitHandle? _showSignal;
    private bool _exiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Transições e animações só entram se o Windows estiver com as animações ligadas.
        // MACH1_NO_MOTION=1 desliga as animações mesmo com o Windows permitindo.
        Controls.Motion.Enabled = AnimationsEnabled() && Environment.GetEnvironmentVariable("MACH1_NO_MOTION") is null;
        if (Controls.Motion.Enabled)
            Styles.Add(new StyleInclude(new Uri("avares://Mach1DriverCustom/")) { Source = new Uri("/Styles/Motion.axaml", UriKind.Relative) });
    }

    /// <summary>A opção "Efeitos de animação" do Windows (Acessibilidade > Efeitos visuais).</summary>
    private static bool AnimationsEnabled()
    {
        const uint GetClientAreaAnimation = 0x1042;
        return !SystemParametersInfo(GetClientAreaAnimation, 0, out bool enabled, 0) || enabled;
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint flags);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            // O app continua vivo na bandeja com a janela fechada.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            string[] args = desktop.Args ?? [];
            bool startInTray = args.Contains(StartupTask.TrayArgument);

            _viewModel = new MainViewModel(startedAtLogon: startInTray);
            desktop.Exit += (_, _) => _viewModel.Dispose();

            // --tab N abre direto em uma aba.
            int tab = Array.IndexOf(args, "--tab");
            if (tab >= 0 && tab + 1 < args.Length && int.TryParse(args[tab + 1], out int index))
                _viewModel.SelectedTab = index;

            _viewModel.Start();
            if (!startInTray)
                ShowWindow();

            // --edit-layout abre direto o editor de layout da tela.
            if (!startInTray && args.Contains("--edit-layout"))
                _window?.OpenLayoutEditor();

            ListenForShowSignal();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { DataContext = _viewModel };
            _window.Closing += (_, e) =>
            {
                if (_exiting || _viewModel?.CloseToTray != true)
                    return;

                e.Cancel = true;
                _window.Hide();
            };
            _window.Closed += (_, _) =>
            {
                _window = null;
                if (!_exiting)
                    Exit();
            };
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _exiting = true;
        _desktop?.Shutdown();
    }

    /// <summary>Uma segunda cópia do app, ao ser aberta, pede para esta mostrar a janela.</summary>
    private void ListenForShowSignal()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSignalName);
        var thread = new Thread(() =>
        {
            while (_showSignal.WaitOne())
                Dispatcher.UIThread.Post(ShowWindow);
        })
        { IsBackground = true, Name = "Sinal de abrir janela" };
        thread.Start();
    }

    private void OnTrayClicked(object? sender, EventArgs e) => ShowWindow();

    private void OnTrayOpen(object? sender, EventArgs e) => ShowWindow();

    private void OnTrayExit(object? sender, EventArgs e) => Exit();
}
