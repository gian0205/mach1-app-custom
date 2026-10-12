using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1DriverCustom.Models;
using Mach1DriverCustom.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1DriverCustom.ViewModels;

/// <summary>
/// Estado da janela. Este arquivo cuida do processador (curva, limites, monitor); as outras
/// partes ficam em MainViewModel.Lcd.cs, .Stability.cs, .Games.cs e .Startup.cs.
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string PawnIoUrl = "https://pawnio.eu/";
    private const int MaxLogLines = 300;

    // Opções gravadas em disco assim que mudam (a curva e os limites só no "Salvar perfil").
    private static readonly HashSet<string> AutoSavedSettings =
    [
        nameof(ApplyOnStart), nameof(CloseToTray),
        nameof(LcdEnabled), nameof(LcdBrightness), nameof(LcdFlipped),
        nameof(LcdBackground), nameof(LcdText), nameof(LcdAccent),
        nameof(LcdPageMode), nameof(LcdPageSeconds), nameof(LcdImagePath), nameof(LcdImageDim),
        nameof(AlertEnabled), nameof(AlertCpu), nameof(AlertGpu),
        nameof(TempColorsEnabled), nameof(CpuWarmAt), nameof(CpuHotAt), nameof(GpuWarmAt), nameof(GpuHotAt),
        nameof(TempWarnColor), nameof(TempHotColor),
    ];

    private readonly bool _startedAtLogon;
    private readonly LcdService _lcd = new();
    private Machine? _machine;
    private DispatcherTimer? _monitor;
    private bool _monitorBusy;
    private bool _loading;
    private int _tick;
    // O que está gravado em disco.
    private Profile _stored = new();

    public MainViewModel() : this(startedAtLogon: false)
    {
    }

    /// <param name="startedAtLogon">O app subiu pela tarefa agendada, junto com o Windows.</param>
    public MainViewModel(bool startedAtLogon)
    {
        _startedAtLogon = startedAtLogon;
        _lcd.DeviceFailed += OnLcdDeviceFailed;
    }

    public ObservableCollection<CoreViewModel> Cores { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    /// <summary>Ajuste da curva; null quando o processador não é um Ryzen suportado.</summary>
    private CpuTuner? Tuner => _machine?.Tuner;

    public int MinMargin => Tuner?.MinMargin ?? CpuTuner.DefaultMinMargin;
    public int MaxMargin => CpuTuner.MaxMargin;

    /// <summary>Dá para ajustar o processador agora: é um Ryzen suportado e nada está em andamento.</summary>
    public bool CanTune => IsReady && HasTuner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTune))]
    private bool _hasTuner;

    [ObservableProperty]
    // O app abre na aba da tela do water cooler.
    private int _selectedTab = LcdTab;

    [ObservableProperty]
    private string _cpuName = "Detectando o computador…";

    [ObservableProperty]
    private string _details = "";

    [ObservableProperty]
    private string _temperature = "—";

    [ObservableProperty]
    private string _gpuTemperature = "";

    [ObservableProperty]
    private bool _isCool;

    [ObservableProperty]
    private bool _isWarm;

    [ObservableProperty]
    private bool _isHot;

    /// <summary>O app terminou de iniciar e nada está sendo aplicado ou testado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTune))]
    private bool _isReady;

    [ObservableProperty]
    private bool _driverMissing;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private decimal? _allCoreMargin = -15;

    [ObservableProperty]
    private bool _changeLimits;

    // Valores de partida; em Ryzen suportado são trocados pelos que o processador informa.
    [ObservableProperty]
    private decimal? _ppt = 162;

    [ObservableProperty]
    private decimal? _tdc = 120;

    [ObservableProperty]
    private decimal? _edc = 180;

    [ObservableProperty]
    private string _stockLimits = "";

    [ObservableProperty]
    private bool _confirmingReboot;

    /// <summary>Inicializa o acesso ao hardware fora da thread de UI.</summary>
    public void Start() => _ = InitializeAsync();

    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsReady = false;
        ErrorMessage = null;

        Machine? previous = _machine;
        _machine = null;
        previous?.Dispose();

        // MACH1_GENERIC=1 finge que o processador não é um Ryzen suportado e que não há placa
        // NVIDIA, para testar o caminho das outras máquinas.
        bool forceGeneric = Environment.GetEnvironmentVariable("MACH1_GENERIC") is not null;
        Machine machine = await Task.Run(() => new Machine(forceGeneric));
        _machine = machine;

        CpuName = machine.CpuName;
        DriverMissing = !machine.DriverInstalled;
        HasTuner = machine.Tuner is not null;

        Cores.Clear();
        if (machine.Tuner is { } tuner)
        {
            Details = $"{tuner.CodeName}, {tuner.CoreCount} núcleos, firmware SMU {tuner.SmuVersion}";
            for (int i = 0; i < tuner.CoreCount; i++)
                Cores.Add(new CoreViewModel(i, tuner.MinMargin));

            var stock = await Task.Run(tuner.GetStockLimits);
            if (stock is var (ppt, tdc))
            {
                // Ponto de partida dos limites: os do próprio processador. O SMU não informa o
                // EDC; 1,5 x TDC é a proporção usual da AMD.
                Ppt = ppt;
                Tdc = tdc;
                Edc = Math.Round(tdc * 1.5m / 5) * 5;
                StockLimits = $"Segundo o processador: PPT {ppt} W, TDC {tdc} A. O EDC inicial é uma estimativa (1,5 x TDC).";
            }
        }
        else
        {
            Details = $"{machine.CoreCount} núcleos, {machine.LogicalCount} threads";
            if (machine.DriverInstalled)
                ErrorMessage = "O ajuste da curva e dos limites só existe em Ryzen da série 5000 em diante, e este processador não é um deles. " +
                               "A tela do water cooler, os sensores e o benchmark funcionam normalmente.";
        }
        OnPropertyChanged(nameof(MinMargin));

        _loading = true;
        LoadStored();
        StartWithWindows = StartupTask.IsEnabled();
        StartupBlocked = StartupGuard.IsBlocked;
        LcdAvailable = LcdDevice.IsPresent;
        _loading = false;

        ReportInterruptedTest();
        LoadBenchmarks();

        await RefreshAsync();
        await ApplyStoredAtLogonAsync();
        await StartLcdFromSettingsAsync();
        IsReady = true;

        if (_monitor is null)
        {
            _monitor = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _monitor.Tick += async (_, _) => await UpdateMonitorAsync();
            _monitor.Start();
        }
    }

    /// <summary>Lê o perfil do disco e o espelha na janela.</summary>
    private void LoadStored()
    {
        try
        {
            _stored = ProfileStore.Load() ?? new Profile();
        }
        catch (Exception ex)
        {
            AddLog("Perfil salvo ilegível, usando o padrão: " + ex.Message);
            _stored = new Profile();
        }

        for (int i = 0; i < Math.Min(Cores.Count, _stored.CoreMargins.Length); i++)
            Cores[i].Margin = _stored.CoreMargins[i];

        ChangeLimits = _stored.ChangeLimits;
        if (_stored.Ppt > 0) Ppt = _stored.Ppt;
        if (_stored.Tdc > 0) Tdc = _stored.Tdc;
        if (_stored.Edc > 0) Edc = _stored.Edc;

        ApplyOnStart = _stored.ApplyOnStart;
        CloseToTray = _stored.CloseToTray;
        LoadLcdSettings(_stored);
        LoadLayout(_stored);

        Games.Clear();
        foreach (GameProfile game in _stored.Games)
            Games.Add(game);
    }

    /// <summary>Curva e limites como estão na janela.</summary>
    private Profile BuildCurve() => new()
    {
        CoreMargins = Cores.Select(c => (int)(c.Margin ?? 0)).ToArray(),
        ChangeLimits = ChangeLimits,
        Ppt = (int)(Ppt ?? 0),
        Tdc = (int)(Tdc ?? 0),
        Edc = (int)(Edc ?? 0),
    };

    /// <summary>Grava as opções; a curva em disco fica como estava.</summary>
    private void SaveSettings()
    {
        if (_loading)
            return;

        _stored.ApplyOnStart = ApplyOnStart;
        _stored.CloseToTray = CloseToTray;
        StoreLcdSettings(_stored);
        _stored.Layout = BuildLayout();
        _stored.Games = [.. Games];

        try
        {
            ProfileStore.Save(_stored);
        }
        catch (Exception ex)
        {
            AddLog("Falha ao gravar as opções: " + ex.Message);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && AutoSavedSettings.Contains(e.PropertyName))
            SaveSettings();
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Profile curve = BuildCurve();
        _stored.CoreMargins = curve.CoreMargins;
        _stored.ChangeLimits = curve.ChangeLimits;
        _stored.Ppt = curve.Ppt;
        _stored.Tdc = curve.Tdc;
        _stored.Edc = curve.Edc;
        SaveSettings();

        StartupGuard.ClearBlock();
        StartupBlocked = false;
        AddLog("Perfil salvo em " + ProfileStore.ProfilePath);
    }

    /// <summary>Lê do SMU a margem em vigor em cada núcleo.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (Tuner is not { } tuner)
            return;

        int count = Cores.Count;
        int?[] margins = await Task.Run(() =>
            Enumerable.Range(0, count).Select(tuner.GetCoreMargin).ToArray());

        for (int i = 0; i < count; i++)
            Cores[i].CurrentMargin = margins[i];
    }

    [RelayCommand]
    private void FillAllCores()
    {
        foreach (CoreViewModel core in Cores)
            core.Margin = AllCoreMargin ?? 0;
    }

    [RelayCommand]
    private Task ApplyAsync() => ApplyCurveAsync(BuildCurve(), verbose: true);

    /// <summary>Envia uma curva ao processador e relê o que ficou em vigor.</summary>
    private async Task ApplyCurveAsync(Profile curve, bool verbose)
    {
        if (Tuner is not { } tuner)
            return;

        IsReady = false;
        try
        {
            IReadOnlyList<string> lines = await Task.Run(() => tuner.Apply(curve));
            foreach (string line in lines)
            {
                if (verbose || !line.EndsWith("OK", StringComparison.Ordinal))
                    AddLog(line);
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            AddLog("Falha ao aplicar: " + ex.Message);
        }
        finally
        {
            IsReady = true;
        }
    }

    /// <summary>Volta a curva a zero. Os limites de PBO só voltam ao valor da BIOS no reboot.</summary>
    [RelayCommand]
    private async Task ResetCurveAsync()
    {
        foreach (CoreViewModel core in Cores)
            core.Margin = 0;

        Profile curve = BuildCurve();
        curve.ChangeLimits = false;
        await ApplyCurveAsync(curve, verbose: true);
    }

    [RelayCommand]
    private void OpenDriverPage() =>
        Process.Start(new ProcessStartInfo(PawnIoUrl) { UseShellExecute = true });

    [RelayCommand]
    private void AskReboot() => ConfirmingReboot = true;

    [RelayCommand]
    private void CancelReboot() => ConfirmingReboot = false;

    [RelayCommand]
    private void RebootToUefi()
    {
        ConfirmingReboot = false;
        try
        {
            // /fw: o próximo boot entra direto na tela de configuração da UEFI.
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 5") { CreateNoWindow = true });
            AddLog("Reiniciando para a BIOS em 5 segundos…");
        }
        catch (Exception ex)
        {
            AddLog("Falha ao reiniciar: " + ex.Message);
        }
    }

    private async Task UpdateMonitorAsync()
    {
        if (_monitorBusy || _machine is not { } machine)
            return;

        _monitorBusy = true;
        try
        {
            var (sensors, clocks) = await Task.Run(() => (machine.ReadSensors(), machine.ReadCoreClocks()));

            float? temp = sensors.CpuTemperature;
            Temperature = temp is float t ? string.Create(CultureInfo.InvariantCulture, $"{t:0.0} °C") : "—";
            GpuTemperature = sensors.Gpu.Temperature is float g ? $"Placa de vídeo: {g:0} °C" : "";
            // A cor da leitura segue as mesmas faixas escolhidas para a tela.
            float warmAt = (float)(CpuWarmAt ?? 0), hotAt = (float)(CpuHotAt ?? 0);
            IsCool = temp < 50;
            IsHot = TempColorsEnabled && temp >= hotAt;
            IsWarm = TempColorsEnabled && !IsHot && temp >= warmAt;
            for (int i = 0; i < Math.Min(Cores.Count, clocks.Length); i++)
                Cores[i].Clock = clocks[i] > 0 ? $"{clocks[i]:0} MHz" : "—";

            UpdateLcd(machine, sensors, clocks);

            if (++_tick % 3 == 0)
                await CheckGamesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _monitorBusy = false;
        }
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(Log.Count - 1);
    }

    public void Dispose()
    {
        _monitor?.Stop();
        _testCancellation?.Cancel();
        _benchCancellation?.Cancel();
        // Fechar normalmente não é queda: libera a aplicação automática do próximo logon.
        StartupGuard.Confirm();
        _lcd.Dispose();
        _machine?.Dispose();
    }
}
