using SkiaSharp;
using System;
using System.Collections.Generic;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Fundo do painel: imagem estática, GIF ou vídeo. Imagens e GIFs ficam decodificados na
/// memória (GIFs em meia resolução e com no máximo <see cref="MaxFrames"/> quadros); vídeos são
/// decodificados aos poucos por <see cref="VideoDecoder"/>.
/// </summary>
public sealed class LcdBackground : IDisposable
{
    private const int MaxFrames = 100;

    private readonly SKImage[] _frames;
    // Instante, em ms, em que cada quadro termina dentro do ciclo.
    private readonly long[] _ends;
    private readonly VideoDecoder? _video;

    private LcdBackground(SKImage[] frames, long[] ends)
    {
        _frames = frames;
        _ends = ends;
    }

    private LcdBackground(VideoDecoder video) : this([], [])
    {
        _video = video;
    }

    public bool IsAnimated => _frames.Length > 1 || _video is not null;

    /// <summary>Intervalo entre quadros que vale a pena enviar para a tela, em ms.</summary>
    public int FrameIntervalMs => _video is not null ? 1000 / VideoDecoder.FramesPerSecond : 100;

    public static LcdBackground Load(string path)
    {
        if (VideoDecoder.IsVideo(path))
            return new LcdBackground(new VideoDecoder(path));

        using SKCodec codec = SKCodec.Create(path)
            ?? throw new InvalidOperationException("Formato de imagem não reconhecido.");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        int count = Math.Max(1, codec.FrameCount);

        if (count == 1)
        {
            Decode(codec, info, decoded, 0);
            return new LcdBackground([Scale(decoded, LcdDevice.Width, LcdDevice.Height)], [1]);
        }

        // GIF longo: guarda um quadro a cada "step" e soma as durações dos que ficaram de fora.
        int step = (count + MaxFrames - 1) / MaxFrames;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        var frames = new List<SKImage>();
        var ends = new List<long>();
        long time = 0;

        for (int i = 0; i < count; i++)
        {
            if (i % step == 0)
            {
                Decode(codec, info, decoded, i);
                frames.Add(Scale(decoded, LcdDevice.Width / 2, LcdDevice.Height / 2));
                ends.Add(0);
            }

            int duration = i < frameInfo.Length ? frameInfo[i].Duration : 0;
            time += duration > 0 ? duration : 100;
            ends[^1] = time;
        }

        return new LcdBackground(frames.ToArray(), ends.ToArray());
    }

    /// <param name="owned">O quadro é uma cópia e quem chamou precisa descartá-lo depois de usar.</param>
    public SKImage GetFrame(long elapsedMs, out bool owned)
    {
        owned = _video is not null;
        if (_video is not null)
            return _video.CopyFrame();

        if (_frames.Length == 1)
            return _frames[0];

        long position = elapsedMs % _ends[^1];
        int index = Array.BinarySearch(_ends, position + 1);
        if (index < 0)
            index = ~index;
        return _frames[Math.Min(index, _frames.Length - 1)];
    }

    private static void Decode(SKCodec codec, SKImageInfo info, SKBitmap target, int frame)
    {
        SKCodecResult result = codec.GetPixels(info, target.GetPixels(), new SKCodecOptions(frame));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidOperationException($"Não foi possível decodificar a imagem ({result}).");
    }

    /// <summary>Reduz a imagem até cobrir exatamente a área pedida; nunca amplia.</summary>
    private static SKImage Scale(SKBitmap source, int width, int height)
    {
        float scale = Math.Max(width / (float)source.Width, height / (float)source.Height);
        if (scale >= 1)
            return SKImage.FromBitmap(source);

        var info = new SKImageInfo((int)Math.Ceiling(source.Width * scale), (int)Math.Ceiling(source.Height * scale),
            SKColorType.Rgba8888, SKAlphaType.Premul);
        using var scaled = new SKBitmap(info);
        using (var canvas = new SKCanvas(scaled))
        using (SKImage image = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(image, SKRect.Create(info.Width, info.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        return SKImage.FromBitmap(scaled);
    }

    public void Dispose()
    {
        _video?.Dispose();
        foreach (SKImage frame in _frames)
            frame.Dispose();
    }
}
