using Avalonia;
using Mach1DriverCustom.Services;
using System;
using System.IO;
using System.Threading;

namespace Mach1DriverCustom;

sealed class Program
{
    /// <summary>Sinal que uma segunda cópia do app dispara para a primeira mostrar a janela.</summary>
    public const string ShowSignalName = "Mach1DriverCustom.Show";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // Grava em arquivos as duas páginas do painel com valores de exemplo, sem tocar em
        // hardware: --lcd-preview <pasta> [imagem de fundo]
        int preview = Array.IndexOf(args, "--lcd-preview");
        if (preview >= 0 && preview + 1 < args.Length)
            return WritePreview(args[preview + 1], preview + 2 < args.Length ? args[preview + 2] : null);

        // Roda o benchmark sem janela e grava o resultado em JSON: --bench <segundos> <arquivo>
        int bench = Array.IndexOf(args, "--bench");
        if (bench >= 0 && bench + 2 < args.Length && int.TryParse(args[bench + 1], out int seconds))
        {
            using var machine = new Machine();
            var options = new BenchmarkOptions("Linha de comando", seconds, PerCore: true, Memory: true);
            Models.BenchmarkRun run = BenchmarkRunner.Run(options, machine, null, CancellationToken.None);
            File.WriteAllText(args[bench + 2], System.Text.Json.JsonSerializer.Serialize(run));
            return 0;
        }

        // Só uma cópia pode falar com o processador e com a tela. A segunda pede para a primeira
        // abrir a janela e sai.
        using var instance = new Mutex(true, "Mach1DriverCustom.Instance", out bool first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(ShowSignalName, out EventWaitHandle? signal))
            {
                signal.Set();
                signal.Dispose();
            }
            return 0;
        }

        // Se o app cair, o motivo fica gravado ao lado do perfil.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                Directory.CreateDirectory(ProfileStore.Directory);
                File.AppendAllText(Path.Combine(ProfileStore.Directory, "crash.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Sem disco para gravar, não há mais o que fazer aqui.
            }
        };

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static int WritePreview(string directory, string? imagePath)
    {
        using var renderer = new LcdRenderer();
        using LcdBackground? background = imagePath is null ? null : LcdBackground.Load(imagePath);

        var sensors = new SensorSnapshot(41.1f, 37f, 11.6f, 31.1f, new GpuReading(38f, 12f, 3.2f, 16f, 86f));
        var content = new LcdContent("AMD Ryzen 7 9800X3D 8-Core Processor", sensors,
            [-15, -20, -25, -10, -30, 5, -15, null], [5225, 5225, 4810, 5225, 3600, 5225, 5225, 5225], DateTime.Now);

        Directory.CreateDirectory(directory);
        void Write(string name, LcdPage page, bool alert) => File.WriteAllBytes(Path.Combine(directory, name),
            renderer.Render(content, new LcdStyle(LcdTheme.Default, false, page, alert, 55), background?.GetFrame(0, out _)));

        Write("temperaturas.jpg", LcdPage.Temperatures, false);
        Write("curva.jpg", LcdPage.Curve, false);
        Write("alerta.jpg", LcdPage.Temperatures, true);

        // Faixas e cores de temperatura personalizadas: 41,1 °C já na cor de atenção (verde), 38 °C normal.
        var custom = new LcdTempColors(true, 40, 60, 50, 70, 0xFF6FD3A0, 0xFFB79CF5);
        File.WriteAllBytes(Path.Combine(directory, "cores-personalizadas.jpg"),
            renderer.Render(content, new LcdStyle(LcdTheme.Default, false, LcdPage.Temperatures, false, 55, custom), null));
        File.WriteAllBytes(Path.Combine(directory, "cores-desligadas.jpg"),
            renderer.Render(content with { Sensors = sensors with { CpuTemperature = 92f } },
                new LcdStyle(LcdTheme.Default, false, LcdPage.Temperatures, false, 55, custom with { Enabled = false }), null));

        // Um arquivo por tema pronto.
        var warm = content with { Sensors = sensors with { CpuTemperature = 78.4f } };
        for (int i = 0; i < LcdThemePreset.All.Count; i++)
        {
            LcdThemePreset preset = LcdThemePreset.All[i];
            // Nos temas claros a temperatura vai em faixa de aviso, para conferir o laranja.
            LcdContent shown = i >= 8 ? warm : content;
            File.WriteAllBytes(Path.Combine(directory, $"tema-{i + 1:00}-{preset.Name}.jpg"),
                renderer.Render(shown, new LcdStyle(preset.Theme, false, LcdPage.Temperatures, false, 55), null));
        }
        return 0;
    }
}
