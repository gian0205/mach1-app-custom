using Avalonia;
using Mach1CustomApp.Services;
using System;
using System.IO;
using System.Threading;

namespace Mach1CustomApp;

sealed class Program
{
    /// <summary>Sinal que uma segunda cópia do app dispara para a primeira mostrar a janela.</summary>
    public const string ShowSignalName = "Mach1CustomApp.Show";

    /// <summary>Sinal que pede para a cópia em execução encerrar do jeito normal, devolvendo as ventoinhas.</summary>
    public const string QuitSignalName = "Mach1CustomApp.Quit";

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // --lcd-size <largura> <altura> faz a prévia abaixo em outra resolução de tela.
        int size = Array.IndexOf(args, "--lcd-size");
        if (size >= 0 && size + 2 < args.Length && int.TryParse(args[size + 1], out int w) && int.TryParse(args[size + 2], out int h))
            LcdScreen.Configure(w, h, args.Contains("--lcd-sideways"));

        // Grava em arquivos as duas páginas do painel com valores de exemplo, sem tocar em
        // hardware: --lcd-preview <pasta> [imagem de fundo]
        int preview = Array.IndexOf(args, "--lcd-preview");
        if (preview >= 0 && preview + 1 < args.Length)
            return WritePreview(args[preview + 1], preview + 2 < args.Length && !args[preview + 2].StartsWith("--") ? args[preview + 2] : null);

        // --fans <arquivo> lista os conectores de ventoinha que a placa-mãe expõe, sem alterar nada.
        int fans = Array.IndexOf(args, "--fans");
        if (fans >= 0 && fans + 1 < args.Length)
        {
            var control = new FanControl();
            var lines = new System.Collections.Generic.List<string> { "Chip: " + (control.ChipName.Length > 0 ? control.ChipName : "(nenhum reconhecido)") };
            foreach (FanChannel channel in control.Channels)
                lines.Add($"{channel.Id} | {channel.Name} | {channel.Rpm:0} RPM | {channel.Percent:0.0} %");
            File.WriteAllLines(args[fans + 1], lines);
            // Sai sem o Dispose de propósito: listar não deve mexer no modo de controle de nenhum conector.
            return 0;
        }

        // --fan-test <conector> <porcentagem> <arquivo>: fixa um conector por alguns segundos, anota a
        // rotação antes e depois, e devolve ao automático. Serve para conferir se a placa obedece.
        int fanTest = Array.IndexOf(args, "--fan-test");
        if (fanTest >= 0 && fanTest + 3 < args.Length && int.TryParse(args[fanTest + 1], out int index) && float.TryParse(args[fanTest + 2], out float percent))
        {
            using var control = new FanControl();
            FanChannel channel = control.Channels[index];
            var lines = new System.Collections.Generic.List<string> { $"antes: {channel.Rpm:0} RPM, {channel.Percent:0} %" };
            channel.SetPercent(percent);
            for (int i = 1; i <= 4; i++)
            {
                Thread.Sleep(2000);
                control.Update();
                lines.Add($"fixo em {percent:0} %, {i * 2} s: {channel.Rpm:0} RPM, {channel.Percent:0} %");
            }
            channel.SetAutomatic();
            Thread.Sleep(4000);
            control.Update();
            lines.Add($"de volta ao automático: {channel.Rpm:0} RPM, {channel.Percent:0} %");
            File.WriteAllLines(args[fanTest + 3], lines);
            return 0;
        }

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

        // --quit encerra a cópia que estiver aberta, como se fosse o Sair da bandeja.
        if (Array.IndexOf(args, "--quit") >= 0)
        {
            if (EventWaitHandle.TryOpenExisting(QuitSignalName, out EventWaitHandle? quit))
            {
                quit.Set();
                quit.Dispose();
            }
            return 0;
        }

        // Só uma cópia pode falar com o processador e com a tela. A segunda pede para a primeira
        // abrir a janela e sai.
        using var instance = new Mutex(true, "Mach1CustomApp.Instance", out bool first);
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
            // Antes de tudo, devolve ventoinhas e bomba à placa-mãe: o app está caindo.
            try
            {
                FanControl.Current?.Dispose();
            }
            catch (Exception)
            {
                // Já estamos em queda; segue para registrar o motivo.
            }

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
            LcdContent shown = i is 8 or 9 ? warm : content;
            File.WriteAllBytes(Path.Combine(directory, $"tema-{i + 1:00}-{preset.Name}.jpg"),
                renderer.Render(shown, new LcdStyle(preset.Theme, false, LcdPage.Temperatures, false, 55), null));
        }
        return 0;
    }
}
