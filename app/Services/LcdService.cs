using System;
using Mach1CustomApp.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Mach1CustomApp.Services;

/// <summary>Opções do painel que o laço de desenho consulta a cada quadro.</summary>
/// <param name="PageMode">0 = temperaturas, 1 = Curve Optimizer, 2 = alterna as duas, 3 = layout personalizado, 4 = gráfico, 5 = medidores, 6 = relógio, 7 = minimalista, 8 = blocos.</param>
/// <param name="Alert">Alguma temperatura passou do limite de alerta.</param>
/// <param name="Temps">Cores da temperatura; null usa os valores automáticos.</param>
public sealed record LcdSettings(LcdTheme Theme, bool Flipped, int PageMode, int PageSeconds, int ImageDim, bool Alert, LcdTempColors? Temps = null)
{
    public static LcdSettings Default { get; } = new(LcdTheme.Default, true, 0, 8, 55, false);
}

/// <summary>
/// Desenha o painel em uma thread própria e o envia para a tela do water cooler. O ritmo
/// depende do conteúdo: 1 quadro por segundo parado, 2 durante um alerta, 10 com GIF de fundo
/// e 15 com vídeo.
/// </summary>
/// <param name="FramesPerSecond">Quadros enviados à tela por segundo; 0 com a tela fechada.</param>
/// <param name="FrameKilobytes">Tamanho do último quadro.</param>
public sealed record LcdStats(double FramesPerSecond, int FrameKilobytes, int Quality);

public sealed class LcdService : IDisposable
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(8);
    private const int StillQuality = 90, MinQuality = 40, MaxAnimatedQuality = 85;

    private readonly LcdRenderer _renderer = new();
    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _deviceGate = new();
    private readonly object _renderGate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private LcdDevice? _device;
    private LcdBackground? _background;
    private TimeSpan _lastKeepAlive;
    private volatile LcdContent? _content;
    private volatile LcdSettings _settings = LcdSettings.Default;
    private volatile byte[]? _lastFrame;
    private volatile LcdLayout _layout = new();
    private volatile bool _responsive;
    // Imagens e GIFs dos elementos do layout, pelo caminho; null marca arquivo que não abriu.
    // Só a thread da tela mexe aqui.
    private readonly Dictionary<string, LcdBackground?> _media = [];
    private volatile bool _active;
    private volatile bool _disposed;
    private volatile LcdStats _stats = new(0, 0, StillQuality);
    // Qualidade do JPEG com fundo animado; ajustada sozinha para os quadros caberem no ritmo.
    private int _animatedQuality = 80;

    public LcdService()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "LCD" };
        _thread.Start();
    }

    /// <summary>A tela parou de responder; o dispositivo já foi fechado.</summary>
    public event Action<Exception>? DeviceFailed;

    public LcdContent? Content
    {
        get => _content;
        set
        {
            // Sem conteúdo a thread dorme sem prazo; o primeiro que chega precisa acordá-la.
            bool first = _content is null;
            _content = value;
            if (first)
                _wake.Set();
        }
    }

    public LcdSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            _wake.Set();
        }
    }

    /// <summary>Layout personalizado em uso. Atribua um objeto novo a cada mudança; ele não é alterado depois.</summary>
    public LcdLayout Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            _wake.Set();
        }
    }

    /// <summary>Redesenhar dez vezes por segundo, para o editor de layout responder ao arrasto.</summary>
    public bool Responsive
    {
        get => _responsive;
        set
        {
            _responsive = value;
            _wake.Set();
        }
    }

    /// <summary>Desenhar quadros. Desligado, a thread fica parada.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _wake.Set();
        }
    }

    /// <summary>A tela está aberta e recebendo quadros.</summary>
    public bool DeviceOpen
    {
        get
        {
            lock (_deviceGate)
                return _device is not null;
        }
    }

    /// <summary>Ritmo real de envio no último segundo.</summary>
    public LcdStats Stats => _stats;

    /// <summary>JPEG do último quadro desenhado, para a pré-visualização.</summary>
    public byte[]? LastFrame => _lastFrame;

    public void OpenDevice(int brightness)
    {
        var device = new LcdDevice();
        try
        {
            device.KeepAlive();
            device.Wake();
            device.SetBrightness(brightness);
        }
        catch
        {
            device.Dispose();
            throw;
        }

        lock (_deviceGate)
        {
            _device?.Dispose();
            _device = device;
            _lastKeepAlive = _clock.Elapsed;
        }
        _wake.Set();
    }

    public void CloseDevice()
    {
        lock (_deviceGate)
        {
            _device?.Dispose();
            _device = null;
        }
    }

    public void SetBrightness(int percent)
    {
        lock (_deviceGate)
            _device?.SetBrightness(percent);
    }

    /// <summary>Troca a imagem de fundo; null volta para a cor lisa.</summary>
    public void SetBackground(LcdBackground? background)
    {
        LcdBackground? old;
        lock (_renderGate)
        {
            old = _background;
            _background = background;
        }
        old?.Dispose();
        _wake.Set();
    }

    private void Loop()
    {
        var work = new Stopwatch();
        long secondStart = 0;
        int framesThisSecond = 0;

        while (!_disposed)
        {
            int wait = Timeout.Infinite;
            work.Restart();
            LcdContent? content = _content;
            LcdSettings settings = _settings;

            if (_active && content is not null)
            {
                try
                {
                    int interval = 0, quality = StillQuality;
                    bool animatedMedia = false;
                    byte[] jpeg;
                    lock (_renderGate)
                    {
                        if (_background?.IsAnimated == true)
                        {
                            interval = _background.FrameIntervalMs;
                            quality = _animatedQuality;
                        }
                        LcdPage page = settings.PageMode switch
                        {
                            0 => LcdPage.Temperatures,
                            1 => LcdPage.Curve,
                            3 => LcdPage.Custom,
                            4 => LcdPage.Chart,
                            5 => LcdPage.Gauges,
                            6 => LcdPage.Clock,
                            7 => LcdPage.Minimal,
                            8 => LcdPage.Tiles,
                            _ => (long)(_clock.Elapsed.TotalSeconds / Math.Max(2, settings.PageSeconds)) % 2 == 0
                                ? LcdPage.Temperatures
                                : LcdPage.Curve,
                        };
                        bool flash = settings.Alert && _clock.ElapsedMilliseconds / 500 % 2 == 0;
                        var style = new LcdStyle(settings.Theme, settings.Flipped, page, flash, settings.ImageDim, settings.Temps);
                        LcdLayout? layout = page == LcdPage.Custom ? _layout : null;
                        SKImage?[]? media = layout is null ? null : ResolveMedia(layout, _clock.ElapsedMilliseconds, out animatedMedia);
                        if (animatedMedia && interval == 0)
                        {
                            interval = 100;
                            quality = _animatedQuality;
                        }

                        bool owned = false;
                        SKImage? image = _background?.GetFrame(_clock.ElapsedMilliseconds, out owned);
                        try
                        {
                            jpeg = _renderer.Render(content, style, image, quality, layout, media);
                        }
                        finally
                        {
                            if (owned)
                                image?.Dispose();
                        }
                    }

                    _lastFrame = jpeg;
                    bool sent = Send(jpeg);

                    // A tela recebe por USB a uma taxa limitada: se desenhar e enviar um quadro leva
                    // mais que o intervalo, o vídeo fica lento. Quadros mais comprimidos resolvem.
                    long spent = work.ElapsedMilliseconds;
                    if (interval > 0 && sent)
                    {
                        if (spent > interval)
                            _animatedQuality = Math.Max(MinQuality, _animatedQuality - 5);
                        else if (spent < interval * 0.6)
                            _animatedQuality = Math.Min(MaxAnimatedQuality, _animatedQuality + 1);
                    }

                    framesThisSecond++;
                    if (_clock.ElapsedMilliseconds - secondStart >= 1000)
                    {
                        double seconds = (_clock.ElapsedMilliseconds - secondStart) / 1000.0;
                        _stats = new LcdStats(sent ? framesThisSecond / seconds : 0, jpeg.Length / 1024, quality);
                        secondStart = _clock.ElapsedMilliseconds;
                        framesThisSecond = 0;
                    }

                    // O intervalo conta do início do quadro, não do fim.
                    wait = interval > 0 ? (int)Math.Max(1, interval - spent) : settings.Alert ? 500 : 1000;
                    if (_responsive)
                        wait = Math.Min(wait, 100);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    wait = 1000;
                }
            }

            _wake.WaitOne(wait);
        }
    }

    /// <summary>
    /// O quadro atual da imagem de cada elemento do layout, carregando os arquivos novos e
    /// descartando os que saíram do layout. Chamar com o _renderGate tomado.
    /// </summary>
    private SKImage?[] ResolveMedia(LcdLayout layout, long elapsedMs, out bool animated)
    {
        animated = false;
        var frames = new SKImage?[layout.Elements.Count];
        var used = new HashSet<string>();

        for (int i = 0; i < frames.Length; i++)
        {
            LcdElement element = layout.Elements[i];
            if (element.Kind != LcdElementKind.Image || string.IsNullOrEmpty(element.Path))
                continue;

            used.Add(element.Path);
            if (!_media.TryGetValue(element.Path, out LcdBackground? media))
            {
                try
                {
                    // Vídeo só como fundo da tela; num elemento ele ocuparia um ffmpeg cada.
                    media = VideoDecoder.IsVideo(element.Path) ? null : LcdBackground.Load(element.Path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    media = null;
                }
                _media[element.Path] = media;
            }

            if (media is null)
                continue;

            frames[i] = media.GetFrame(elapsedMs, out _);
            animated |= media.IsAnimated;
        }

        foreach (string path in _media.Keys.Where(path => !used.Contains(path)).ToArray())
        {
            _media[path]?.Dispose();
            _media.Remove(path);
        }

        return frames;
    }

    /// <summary>Envia o quadro; false quando a tela não está aberta ou falhou.</summary>
    private bool Send(byte[] jpeg)
    {
        Exception? failure = null;
        lock (_deviceGate)
        {
            if (_device is null)
                return false;

            try
            {
                if (_clock.Elapsed - _lastKeepAlive > KeepAliveInterval)
                {
                    _device.KeepAlive();
                    _lastKeepAlive = _clock.Elapsed;
                }
                _device.SendFrame(jpeg);
            }
            catch (Exception ex)
            {
                _device.Dispose();
                _device = null;
                failure = ex;
            }
        }

        if (failure is not null)
            DeviceFailed?.Invoke(failure);
        return failure is null;
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _thread.Join(2000);
        CloseDevice();
        lock (_renderGate)
        {
            _background?.Dispose();
            _background = null;
            foreach (LcdBackground? media in _media.Values)
                media?.Dispose();
            _media.Clear();
        }
        _renderer.Dispose();
    }
}
