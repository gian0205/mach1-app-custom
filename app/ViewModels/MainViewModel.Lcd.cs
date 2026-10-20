using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// Tela do water cooler: opções, pré-visualização e alerta de temperatura.
public partial class MainViewModel
{
    private const int LcdTab = 1;

    // Leituras dos últimos minutos, para as páginas da tela com gráfico.
    private readonly LcdHistoryBuffer _lcdHistory = new();

    private const int LcdRetrySeconds = 5;

    private byte[]? _previewSource;
    private bool _lcdOpening;
    private string? _lcdFailure;
    private bool _alertActive;

    /// <summary>A tela do water cooler está conectada.</summary>
    [ObservableProperty]
    private bool _lcdAvailable;

    [ObservableProperty]
    private bool _lcdEnabled;

    [ObservableProperty]
    private double _lcdBrightness = 50;

    [ObservableProperty]
    private bool _lcdFlipped = true;

    [ObservableProperty]
    private Color _lcdBackground = Color.FromUInt32(LcdTheme.Default.Background);

    [ObservableProperty]
    private Color _lcdText = Color.FromUInt32(LcdTheme.Default.Text);

    /// <summary>Cor das temperaturas e das barras; perto do limite a temperatura muda de cor sozinha.</summary>
    [ObservableProperty]
    private Color _lcdAccent = Color.FromUInt32(LcdTheme.Default.Accent);

    /// <summary>0 = temperaturas e sensores, 1 = Curve Optimizer, 2 = alterna as duas, 3 = layout personalizado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomLayout))]
    private int _lcdPageMode;

    public bool IsCustomLayout => LcdPageMode == CustomPageMode;

    [ObservableProperty]
    private decimal? _lcdPageSeconds = 8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LcdImageName), nameof(HasLcdImage))]
    private string? _lcdImagePath;

    /// <summary>Opacidade da cor de fundo sobre a imagem ou o vídeo, de 0 (só a imagem) a 100 (só a cor).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LcdImageDimText))]
    private double _lcdImageDim = 55;

    /// <summary>Ritmo real de envio para a tela, em texto.</summary>
    [ObservableProperty]
    private string _lcdStatsText = "";

    [ObservableProperty]
    private bool _alertEnabled;

    [ObservableProperty]
    private decimal? _alertCpu = 85;

    [ObservableProperty]
    private decimal? _alertGpu = 83;

    // Modelo e resolução da tela.

    /// <summary>Qual tela foi encontrada no USB, em texto.</summary>
    [ObservableProperty]
    private string _lcdScreenText = "";

    /// <summary>Resolução gravada no perfil; passa a valer na próxima abertura do app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LcdResolutionNote))]
    private decimal? _lcdWidthSetting = LcdScreen.PanelWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LcdResolutionNote))]
    private decimal? _lcdHeightSetting = LcdScreen.PanelHeight;

    /// <summary>Tela deitada, como gravado no perfil; passa a valer na próxima abertura do app.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LcdResolutionNote))]
    private bool _lcdSidewaysSetting = LcdScreen.Sideways;

    public string LcdResolutionNote
    {
        get
        {
            string inUse = $"Em uso: {LcdScreen.PanelWidth} x {LcdScreen.PanelHeight}, {(LcdScreen.Sideways ? "deitada" : "em pé")}.";
            bool same = (int)(LcdWidthSetting ?? 0) == LcdScreen.PanelWidth && (int)(LcdHeightSetting ?? 0) == LcdScreen.PanelHeight &&
                        LcdSidewaysSetting == LcdScreen.Sideways;
            return same ? inUse : inUse + " Feche e abra o app para a mudança valer.";
        }
    }

    // Tamanho da prévia na aba da tela e da área do editor, na proporção da tela.
    public double PreviewWidth => FitScreen(288, 512).Width;
    public double PreviewHeight => FitScreen(288, 512).Height;
    public double EditorWidth => FitScreen(480, 640).Width;
    public double EditorHeight => FitScreen(480, 640).Height;

    private static (double Width, double Height) FitScreen(double maxWidth, double maxHeight)
    {
        double scale = Math.Min(maxWidth / LcdScreen.Width, maxHeight / LcdScreen.Height);
        return (Math.Round(LcdScreen.Width * scale), Math.Round(LcdScreen.Height * scale));
    }

    /// <summary>Procura a tela no USB e descreve o que achou.</summary>
    private void DetectLcdScreen()
    {
        LcdScreenInfo? screen = LcdScreen.Find()?.Info;
        LcdAvailable = screen is not null;
        LcdScreenText = screen switch
        {
            null => "Nenhuma tela encontrada no USB.",
            { Tested: true } => $"Tela encontrada: IRV360 (USB {screen.Id}).",
            _ => $"Tela encontrada: {(screen.Name.Length > 0 ? screen.Name : "modelo sem nome")} (USB {screen.Id}). " +
                 "Este modelo ainda não foi testado com o app: informe a resolução dele abaixo.",
        };
    }

    [RelayCommand]
    private void ResetLcdResolution()
    {
        LcdWidthSetting = LcdScreen.DefaultWidth;
        LcdHeightSetting = LcdScreen.DefaultHeight;
    }

    // Cores da temperatura conforme ela sobe.

    [ObservableProperty]
    private bool _tempColorsEnabled = true;

    /// <summary>Temperatura do processador, em °C, a partir da qual vale a cor de atenção.</summary>
    [ObservableProperty]
    private decimal? _cpuWarmAt = 75;

    [ObservableProperty]
    private decimal? _cpuHotAt = 88;

    [ObservableProperty]
    private decimal? _gpuWarmAt = 75;

    [ObservableProperty]
    private decimal? _gpuHotAt = 85;

    [ObservableProperty]
    private Color _tempWarnColor = Color.FromUInt32(LcdTempColors.DefaultWarn);

    [ObservableProperty]
    private Color _tempHotColor = Color.FromUInt32(LcdTempColors.DefaultHot);

    /// <summary>Último quadro desenhado, mostrado na aba da tela.</summary>
    [ObservableProperty]
    private Bitmap? _lcdPreview;

    public string LcdImageName => LcdImagePath is null ? "Nenhum arquivo: fundo liso." : Path.GetFileName(LcdImagePath);

    public bool HasLcdImage => LcdImagePath is not null;

    public string LcdImageDimText => $"{LcdImageDim:0} %";

    private void LoadLcdSettings(Profile profile)
    {
        LcdEnabled = profile.LcdEnabled;
        LcdBrightness = profile.LcdBrightness;
        LcdFlipped = profile.LcdFlipped;
        if (Color.TryParse(profile.LcdBackground, out Color background)) LcdBackground = background;
        if (Color.TryParse(profile.LcdText, out Color text)) LcdText = text;
        if (Color.TryParse(profile.LcdAccent, out Color accent)) LcdAccent = accent;
        LcdNeon = profile.LcdNeon;
        LcdPageMode = Math.Clamp(profile.LcdPageMode, 0, LastPageMode);
        LcdPageSeconds = profile.LcdPageSeconds;
        LcdImagePath = profile.LcdImagePath;
        LcdImageDim = profile.LcdImageDim;
        AlertEnabled = profile.AlertEnabled;
        AlertCpu = profile.AlertCpu;
        AlertGpu = profile.AlertGpu;

        // Faixas em 0 no perfil significam "automático": as do processador desta máquina.
        LcdTempColors automatic = LcdTempColors.Automatic(_machine?.CpuTemperatureLimit ?? 95);
        TempColorsEnabled = profile.TempColorsEnabled;
        CpuWarmAt = profile.CpuWarmAt > 0 ? profile.CpuWarmAt : (decimal)automatic.CpuWarm;
        CpuHotAt = profile.CpuHotAt > 0 ? profile.CpuHotAt : (decimal)automatic.CpuHot;
        GpuWarmAt = profile.GpuWarmAt > 0 ? profile.GpuWarmAt : (decimal)automatic.GpuWarm;
        GpuHotAt = profile.GpuHotAt > 0 ? profile.GpuHotAt : (decimal)automatic.GpuHot;
        TempWarnColor = Color.TryParse(profile.TempWarnColor, out Color warn) ? warn : Color.FromUInt32(automatic.Warn);
        TempHotColor = Color.TryParse(profile.TempHotColor, out Color hot) ? hot : Color.FromUInt32(automatic.Hot);
    }

    private void StoreLcdSettings(Profile profile)
    {
        profile.LcdEnabled = LcdEnabled;
        profile.LcdBrightness = (int)LcdBrightness;
        profile.LcdFlipped = LcdFlipped;
        profile.LcdBackground = LcdBackground.ToString();
        profile.LcdText = LcdText.ToString();
        profile.LcdAccent = LcdAccent.ToString();
        profile.LcdNeon = LcdNeon;
        profile.LcdPageMode = LcdPageMode;
        profile.LcdPageSeconds = (int)(LcdPageSeconds ?? 8);
        profile.LcdImagePath = LcdImagePath;
        profile.LcdImageDim = (int)LcdImageDim;
        profile.AlertEnabled = AlertEnabled;
        profile.AlertCpu = (int)(AlertCpu ?? 85);
        profile.AlertGpu = (int)(AlertGpu ?? 83);
        profile.LcdWidth = (int)(LcdWidthSetting ?? 0);
        profile.LcdHeight = (int)(LcdHeightSetting ?? 0);
        profile.LcdSideways = LcdSidewaysSetting;
        profile.TempColorsEnabled = TempColorsEnabled;
        profile.CpuWarmAt = (int)(CpuWarmAt ?? 0);
        profile.CpuHotAt = (int)(CpuHotAt ?? 0);
        profile.GpuWarmAt = (int)(GpuWarmAt ?? 0);
        profile.GpuHotAt = (int)(GpuHotAt ?? 0);
        profile.TempWarnColor = TempWarnColor.ToString();
        profile.TempHotColor = TempHotColor.ToString();
    }

    /// <summary>Depois de carregar o perfil: repassa as opções, a imagem de fundo e liga a tela se for o caso.</summary>
    private async Task StartLcdFromSettingsAsync()
    {
        PushLcdSettings();
        await LoadLcdImageAsync();
        UpdateLcdActive();
        if (LcdEnabled)
            await StartLcdAsync();
    }

    private void PushLcdSettings() => _lcd.Settings = new LcdSettings(
        new LcdTheme(LcdBackground.ToUInt32(), LcdText.ToUInt32(), LcdAccent.ToUInt32(), LcdNeon),
        LcdFlipped,
        LcdPageMode,
        (int)(LcdPageSeconds ?? 8),
        (int)LcdImageDim,
        _alertActive,
        new LcdTempColors(TempColorsEnabled,
            (float)(CpuWarmAt ?? 0), (float)(CpuHotAt ?? 0), (float)(GpuWarmAt ?? 0), (float)(GpuHotAt ?? 0),
            TempWarnColor.ToUInt32(), TempHotColor.ToUInt32()));

    /// <summary>Só desenha quadros com a tela ligada, com a aba da tela aberta ou com o editor de layout aberto.</summary>
    private void UpdateLcdActive() => _lcd.Active = LcdEnabled || SelectedTab == LcdTab || _editingLayout;

    partial void OnSelectedTabChanged(int value) => UpdateLcdActive();

    partial void OnLcdFlippedChanged(bool value) => PushLcdSettings();

    partial void OnLcdBackgroundChanged(Color value) => PushLcdSettings();

    partial void OnLcdTextChanged(Color value) => PushLcdSettings();

    partial void OnLcdAccentChanged(Color value) => PushLcdSettings();

    /// <summary>Barras e temperaturas como tubos de neon acesos.</summary>
    [ObservableProperty]
    private bool _lcdNeon;

    partial void OnLcdNeonChanged(bool value) => PushLcdSettings();

    partial void OnLcdPageModeChanged(int value)
    {
        PushLcdSettings();
        // Escolher a página do clima já liga a busca da previsão.
        if (value == WeatherPageMode && !_loading)
            WeatherEnabled = true;
    }

    partial void OnLcdPageSecondsChanged(decimal? value) => PushLcdSettings();

    partial void OnLcdImageDimChanged(double value) => PushLcdSettings();

    partial void OnTempColorsEnabledChanged(bool value) => PushLcdSettings();

    partial void OnCpuWarmAtChanged(decimal? value) => PushLcdSettings();

    partial void OnCpuHotAtChanged(decimal? value) => PushLcdSettings();

    partial void OnGpuWarmAtChanged(decimal? value) => PushLcdSettings();

    partial void OnGpuHotAtChanged(decimal? value) => PushLcdSettings();

    partial void OnTempWarnColorChanged(Color value) => PushLcdSettings();

    partial void OnTempHotColorChanged(Color value) => PushLcdSettings();

    /// <summary>Volta as faixas e as cores da temperatura para os valores automáticos deste processador.</summary>
    [RelayCommand]
    private void ResetTempColors()
    {
        LcdTempColors automatic = LcdTempColors.Automatic(_machine?.CpuTemperatureLimit ?? 95);
        TempColorsEnabled = true;
        CpuWarmAt = (decimal)automatic.CpuWarm;
        CpuHotAt = (decimal)automatic.CpuHot;
        GpuWarmAt = (decimal)automatic.GpuWarm;
        GpuHotAt = (decimal)automatic.GpuHot;
        TempWarnColor = Color.FromUInt32(automatic.Warn);
        TempHotColor = Color.FromUInt32(automatic.Hot);
    }

    partial void OnLcdImagePathChanged(string? value)
    {
        if (!_loading)
            _ = LoadLcdImageAsync();
    }

    partial void OnLcdEnabledChanged(bool value)
    {
        if (_loading)
            return;

        UpdateLcdActive();
        if (value)
            _ = StartLcdAsync();
        else
            _lcd.CloseDevice();
    }

    partial void OnLcdBrightnessChanged(double value)
    {
        int percent = (int)value;
        Task.Run(() =>
        {
            try { _lcd.SetBrightness(percent); }
            catch (Exception ex) { Debug.WriteLine(ex); }
        });
    }

    [RelayCommand]
    private void ClearLcdImage() => LcdImagePath = null;

    /// <summary>Temas de cor prontos, mostrados como amostras clicáveis.</summary>
    public IReadOnlyList<LcdThemeSwatch> LcdThemes { get; } = LcdThemePreset.All.Select(p => new LcdThemeSwatch(p)).ToArray();

    /// <summary>Troca as três cores do painel pelas do tema; o fundo com imagem ou vídeo não muda.</summary>
    [RelayCommand]
    private void ApplyLcdTheme(LcdThemeSwatch swatch)
    {
        LcdBackground = Color.FromUInt32(swatch.Preset.Theme.Background);
        LcdText = Color.FromUInt32(swatch.Preset.Theme.Text);
        LcdAccent = Color.FromUInt32(swatch.Preset.Theme.Accent);
        LcdNeon = swatch.Preset.Theme.Neon;
    }

    /// <summary>Volta as cores originais do painel e tira a imagem ou o vídeo de fundo.</summary>
    [RelayCommand]
    private void RestoreLcdAppearance()
    {
        LcdBackground = Color.FromUInt32(LcdTheme.Default.Background);
        LcdText = Color.FromUInt32(LcdTheme.Default.Text);
        LcdAccent = Color.FromUInt32(LcdTheme.Default.Accent);
        LcdNeon = false;
        LcdImagePath = null;
        LcdImageDim = 55;
    }

    private async Task LoadLcdImageAsync()
    {
        string? path = LcdImagePath;
        if (path is null)
        {
            _lcd.SetBackground(null);
            return;
        }

        try
        {
            // "Services." porque a propriedade LcdBackground (a cor) tem o mesmo nome da classe.
            Services.LcdBackground background = await Task.Run(() => Services.LcdBackground.Load(path));
            // O usuário pode ter trocado de imagem enquanto esta carregava.
            if (path == LcdImagePath)
                _lcd.SetBackground(background);
            else
                background.Dispose();
        }
        catch (Exception ex)
        {
            AddLog($"Não foi possível abrir o fundo ({Path.GetFileName(path)}): {ex.Message}");
            if (path == LcdImagePath)
                LcdImagePath = null;
        }
    }

    /// <summary>
    /// Tenta abrir a tela. Se não der (tela ocupada pelo MACH1-LCD, cabo solto, falha momentânea),
    /// a opção continua marcada e <see cref="RetryLcd"/> tenta de novo a cada poucos segundos.
    /// </summary>
    private async Task StartLcdAsync()
    {
        if (_lcdOpening || _lcd.DeviceOpen)
            return;

        _lcdOpening = true;
        try
        {
            // O programa do fabricante e este app não conseguem escrever na tela ao mesmo tempo.
            if (Process.GetProcessesByName("MACH1-LCD").Length > 0)
                throw new InvalidOperationException("o MACH1-LCD está aberto e os dois não podem usar a tela ao mesmo tempo; feche-o.");

            int brightness = (int)LcdBrightness;
            await Task.Run(() => _lcd.OpenDevice(brightness));
            DetectLcdScreen();
            _lcdFailure = null;
            AddLog("Painel ligado na tela do water cooler.");
        }
        catch (Exception ex)
        {
            // Só registra quando o motivo muda, para não repetir a mesma linha a cada tentativa.
            if (_lcdFailure != ex.Message)
                AddLog($"Não foi possível ligar a tela ({ex.Message}). Vou tentar de novo a cada {LcdRetrySeconds} segundos.");
            _lcdFailure = ex.Message;
        }
        finally
        {
            _lcdOpening = false;
        }
    }

    /// <summary>Chamado a cada leitura dos sensores: reabre a tela se ela deveria estar ligada e não está.</summary>
    private void RetryLcd()
    {
        if (LcdEnabled && !_lcd.DeviceOpen && !_lcdOpening && _tick % LcdRetrySeconds == 0)
            _ = StartLcdAsync();
    }

    // Chamado pela thread da tela.
    private void OnLcdDeviceFailed(Exception exception) => Dispatcher.UIThread.Post(() =>
    {
        _lcdFailure = exception.Message;
        AddLog($"A tela parou de responder ({exception.Message}). Vou tentar de novo a cada {LcdRetrySeconds} segundos.");
    });

    /// <summary>A cada leitura dos sensores: entrega os dados ao painel, avalia o alerta e atualiza a prévia.</summary>
    private void UpdateLcd(Machine machine, SensorSnapshot sensors, double[] clocks)
    {
        bool alert = AlertEnabled &&
            (sensors.CpuTemperature >= (float)(AlertCpu ?? 85) || sensors.Gpu.Temperature >= (float)(AlertGpu ?? 83));
        if (alert != _alertActive)
        {
            _alertActive = alert;
            PushLcdSettings();
            AddLog(alert
                ? $"Alerta de temperatura: processador {sensors.CpuTemperature:0.0} °C, placa de vídeo {sensors.Gpu.Temperature:0} °C."
                : "Temperaturas de volta abaixo do limite de alerta.");
        }

        RetryLcd();

        LcdStats stats = _lcd.Stats;
        LcdStatsText = stats.FramesPerSecond > 0
            ? $"Enviando {stats.FramesPerSecond:0} por segundo; cada quadro tem {stats.FrameKilobytes} KB (qualidade {stats.Quality})."
            : "";

        _lcd.Content = new LcdContent(CpuName, sensors, Cores.Select(c => c.CurrentMargin).ToArray(), clocks, DateTime.Now,
            machine.CpuTemperatureLimit, MinMargin, _lcdHistory.Add(sensors), _weather);

        if (SelectedTab == LcdTab)
            RefreshLcdPreview();
    }

    /// <summary>Atualiza a prévia com o último quadro desenhado, se ele mudou.</summary>
    public void RefreshLcdPreview()
    {
        if (_lcd.LastFrame is not { } jpeg || ReferenceEquals(jpeg, _previewSource))
            return;

        _previewSource = jpeg;
        Bitmap? old = LcdPreview;
        LcdPreview = new Bitmap(new MemoryStream(jpeg));
        // Libera o quadro anterior só depois que a janela trocou para o novo.
        Dispatcher.UIThread.Post(() => old?.Dispose(), DispatcherPriority.Background);
    }
}

/// <summary>Um tema pronto com os pincéis para desenhar a amostra dele na janela.</summary>
public sealed class LcdThemeSwatch(LcdThemePreset preset)
{
    public LcdThemePreset Preset { get; } = preset;

    public string Name => Preset.Name;

    public IBrush Background { get; } = new SolidColorBrush(Color.FromUInt32(preset.Theme.Background));

    public IBrush Text { get; } = new SolidColorBrush(Color.FromUInt32(preset.Theme.Text));

    public IBrush Accent { get; } = new SolidColorBrush(Color.FromUInt32(preset.Theme.Accent));
}
