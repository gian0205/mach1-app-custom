using System;
using System.Diagnostics;

namespace Mach1DriverCustom.Services;

/// <summary>
/// Tarefa agendada que abre o app na bandeja a cada logon. Precisa ser tarefa (e não a chave
/// Run do registro) porque o app roda elevado e não pode abrir o prompt do UAC a cada login.
/// </summary>
public static class StartupTask
{
    /// <summary>Argumento com que a tarefa chama o app: sobe sem janela, só na bandeja.</summary>
    public const string TrayArgument = "--tray";

    private const string TaskName = "Mach1DriverCustom";

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Enable()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do executável desconhecido.");
        // Atraso de 15 s: deixa o Windows terminar o logon antes de mexer no processador.
        return Run($"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /DELAY 0000:15 " +
                   $"/TR \"\\\"{exe}\\\" {TrayArgument}\"") == 0;
    }

    public static bool Disable() => Run($"/Delete /F /TN \"{TaskName}\"") == 0;

    private static int Run(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
