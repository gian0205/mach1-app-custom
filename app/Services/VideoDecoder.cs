using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Decodifica um vídeo em laço, no ritmo dele, com um ffmpeg.exe em segundo plano que entrega
/// quadros crus já cortados para a proporção da tela. Só o quadro mais recente fica guardado.
/// </summary>
public sealed class VideoDecoder : IDisposable
{
    public const int FramesPerSecond = 15;

    // Meia resolução da tela: sob os números e o escurecimento a diferença não aparece, e o
    // custo de decodificar e copiar cai para um quarto.
    private const int Width = LcdDevice.Width / 2, Height = LcdDevice.Height / 2;
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(8);

    private static readonly string[] Extensions = [".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi"];

    private readonly Process _process;
    private readonly Thread _reader;
    private readonly object _gate = new();
    private readonly byte[] _latest = new byte[Width * Height * 4];
    private readonly ManualResetEventSlim _firstFrame = new(false);
    private readonly StringBuilder _errors = new();
    private volatile bool _disposed;

    public static bool IsVideo(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Procura o ffmpeg.exe ao lado do app, no PATH e na instalação do MACH1-LCD.</summary>
    public static string? FindFfmpeg()
    {
        var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder.Trim(), "ffmpeg.exe")))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MACH1-LCD", "FFMPEG", "ffmpeg.exe"));

        return candidates.FirstOrDefault(File.Exists);
    }

    public VideoDecoder(string path)
    {
        string ffmpeg = FindFfmpeg() ?? throw new InvalidOperationException(
            "Para vídeo é preciso o ffmpeg.exe: ele vem com o MACH1-LCD, ou pode ficar na pasta deste app.");

        // -stream_loop -1 repete o arquivo sem parar; -re lê no ritmo do vídeo em vez de o mais
        // rápido possível; o filtro preenche a tela cortando as sobras.
        string filter = $"scale={Width}:{Height}:force_original_aspect_ratio=increase,crop={Width}:{Height},fps={FramesPerSecond}";
        _process = Process.Start(new ProcessStartInfo(ffmpeg)
        {
            Arguments = $"-hide_banner -loglevel error -stream_loop -1 -re -i \"{path}\" -an -vf \"{filter}\" -pix_fmt rgba -f rawvideo -",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg.");

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (_errors)
                    _errors.AppendLine(e.Data);
        };
        _process.BeginErrorReadLine();

        _reader = new Thread(ReadFrames) { IsBackground = true, Name = "Vídeo de fundo" };
        _reader.Start();

        // Espera o primeiro quadro para já devolver erro de arquivo ilegível a quem carregou.
        if (!_firstFrame.Wait(FirstFrameTimeout))
        {
            string errors;
            lock (_errors)
                errors = _errors.ToString().Trim();
            Dispose();
            throw new InvalidOperationException(errors.Length > 0
                ? "O ffmpeg não conseguiu ler o vídeo: " + errors.Split('\n')[^1].Trim()
                : "O vídeo não produziu nenhum quadro.");
        }
    }

    /// <summary>Cópia do quadro mais recente; quem chama é dono dela e precisa descartá-la.</summary>
    public SKImage CopyFrame()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        lock (_gate)
            return SKImage.FromPixelCopy(info, _latest);
    }

    private void ReadFrames()
    {
        byte[] frame = new byte[_latest.Length];
        Stream output = _process.StandardOutput.BaseStream;
        try
        {
            while (!_disposed)
            {
                output.ReadExactly(frame);
                lock (_gate)
                    Buffer.BlockCopy(frame, 0, _latest, 0, frame.Length);
                _firstFrame.Set();
            }
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ObjectDisposedException)
        {
            // O ffmpeg terminou (arquivo ilegível) ou foi encerrado pelo Dispose.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (!_process.HasExited)
                _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Já tinha terminado.
        }
        _reader.Join(1000);
        _process.Dispose();
    }
}
