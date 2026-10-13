using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

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
        nameof(LcdBackground), nameof(LcdText), nameof(LcdAccent), nameof(LcdNeon),
        nameof(LcdPageMode), nameof(LcdPageSeconds), nameof(LcdImagePath), nameof(LcdImageDim),
        nameof(AlertEnabled), nameof(AlertCpu), nameof(AlertGpu),
        nameof(TempColorsEnabled), nameof(CpuWarmAt), nameof(CpuHotAt), nameof(GpuWarmAt), nameof(GpuHotAt),
        nameof(TempWarnColor), nameof(TempHotColor),
        nameof(LcdWidthSetting), nameof(LcdHeightSetting), nameof(LcdSidewaysSetting),
        nameof(RgbEnabled), nameof(RgbMode), nameof(RgbColor), nameof(RgbBrightness), nameof(RgbPort),
    ];

    private readonly bool _startedAtLogon;
    private readonly LcdService _lcd;
    private Machine? _machine;
    private DispatcherTimer? _monitor;
    private bool _monitorBusy;
    private bool _loading;
    // Falso até o perfil ser lido do disco: antes disso, gravar apagaria o que o usuário salvou.
    private bool _storedLoaded;
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

        // A resolução da tela precisa estar definida antes de o desenho do painel ser criado.
        try
        {
            Profile? saved = ProfileStore.Load();
            LcdScreen.Configure(saved?.LcdWidth ?? 0, saved?.LcdHeight ?? 0, saved?.LcdSideways ?? false);
        }
        catch (Exception)
        {
            // Perfil ilegível: fica a resolução padrão; o aviso sai quando o perfil for carregado.
        }

        // Os campos da janela nascem antes desta leitura, com o padrão; aqui passam a mostrar o que está em uso.
        LcdWidthSetting = LcdScreen.PanelWidth;
        LcdHeightSetting = LcdScreen.PanelHeight;
        LcdSidewaysSetting = LcdScreen.Sideways;

        _lcd = new LcdService();
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

    /// <summary>Dá para mexer na curva agora: o processador tem Curve Optimizer e nada está em andamento.</summary>
    public bool CanTuneCurve => IsReady && HasCurve;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTuneCurve))]
    private bool _hasCurve;

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
    [NotifyPropertyChangedFor(nameof(CanTune), nameof(CanTuneCurve))]
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
        HasTuner = machine.Tuner is { SupportsLimits: true };
        HasCurve = machine.Tuner is { SupportsCurveOptimizer: true };

        Cores.Clear();
        if (machine.Tuner is { } tuner)
        {
            Details = $"{tuner.CodeName}, {machine.CoreSummary}, firmware SMU {tuner.SmuVersion}";
            if (tuner.SupportsCurveOptimizer)
            {
                for (int i = 0; i < tuner.CoreCount; i++)
                    Cores.Add(new CoreViewModel(i, tuner.MinMargin));
            }
            else
            {
                ErrorMessage = "Este Ryzen é anterior à série 5000 e não tem Curve Optimizer. " +
                               (tuner.SupportsLimits ? "Os limites de PBO estão disponíveis; " : "") +
                               "a tela do water cooler, as ventoinhas, os sensores e o benchmark funcionam normalmente.";
            }

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
            Details = machine.CoreSummary;
            if (machine.DriverInstalled)
            {
                ErrorMessage = machine.Vendor switch
                {
                    CpuVendor.Intel => "Processador Intel: a tela do water cooler, as ventoinhas, os sensores e o benchmark funcionam. " +
                                       "O Curve Optimizer e os limites de PBO são recursos de Ryzen e ficam desabilitados.",
                    CpuVendor.Amd => "Este processador AMD não é reconhecido pela biblioteca de ajuste, então a curva e os limites ficam desabilitados. " +
                                     "A tela do water cooler, as ventoinhas, os sensores e o benchmark funcionam.",
                    _ => "Processador não reconhecido para ajustes. A tela do water cooler, as ventoinhas, os sensores e o benchmark funcionam.",
                };
            }
        }
        OnPropertyChanged(nameof(MinMargin));

        _loading = true;
        LoadStored();
        _storedLoaded = true;
        StartWithWindows = StartupTask.IsEnabled();
        StartupBlocked = StartupGuard.IsBlocked;
        DetectLcdScreen();
        _loading = false;

        ReportInterruptedTest();
        await StartFansAsync();
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

            // Guarda o arquivo ilegível: a próxima gravação o substituiria pelo padrão.
            try
            {
                System.IO.File.Copy(ProfileStore.ProfilePath, ProfileStore.ProfilePath + ".ilegivel", overwrite: true);
            }
            catch (Exception)
            {
            }
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
        LoadRgbSettings(_stored);

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
        if (_loading || !_storedLoaded)
            return;

        _stored.ApplyOnStart = ApplyOnStart;
        _stored.CloseToTray = CloseToTray;
        StoreLcdSettings(_stored);
        _stored.Layout = BuildLayout();
        StoreRgbSettings(_stored);
        _stored.Games = [.. Games];
        if (_fans is not null)
            _stored.Fans = Fans.Select(f => f.ToSetting()).ToList();

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
            await UpdateFansAsync(machine, sensors);
            await UpdateRgbAsync(sensors);

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
        ReleaseFans();
        _rgb.Dispose();
        _machine?.Dispose();
    }
}
