using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mach1CustomApp.Services;

/// <summary>
/// Registro, em disco, dos conectores de ventoinha que o app tirou do controle da placa-mãe.
/// Existe por causa de uma limitação: se o app é encerrado à força (ou trava) com um conector
/// em velocidade fixa ou curva, o chip continua nesse modo, parado na última velocidade, e só a
/// BIOS, no próximo boot, devolve o conector ao automático. Na abertura seguinte o app não tem
/// como restaurar o modo original, então precisa saber quais conectores ficaram assim para
/// continuar cuidando deles.
/// </summary>
public static class FanTakeover
{
    private sealed class State
    {
        /// <summary>Momento em que o Windows subiu; outro valor significa que houve reboot.</summary>
        public DateTime Boot { get; set; }

        public List<string> Ids { get; set; } = [];
    }

    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(ProfileStore.Directory, "fans-taken.json");

    private static DateTime CurrentBoot => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    /// <summary>
    /// Conectores que uma execução anterior deixou fora do automático neste mesmo boot. Depois de
    /// um reboot a BIOS já reassumiu todos, e a lista volta vazia.
    /// </summary>
    public static IReadOnlyCollection<string> ReadOrphans()
    {
        lock (Gate)
        {
            State? state = Read();
            if (state is null)
                return [];

            if (Math.Abs((state.Boot - CurrentBoot).TotalMinutes) > 2)
            {
                File.Delete(FilePath);
                return [];
            }
            return state.Ids;
        }
    }

    /// <summary>Anota que o app assumiu este conector.</summary>
    public static void Mark(string id)
    {
        lock (Gate)
        {
            State state = Read() ?? new State();
            if (state.Ids.Contains(id) && Math.Abs((state.Boot - CurrentBoot).TotalMinutes) <= 2)
                return;

            state.Boot = CurrentBoot;
            if (!state.Ids.Contains(id))
                state.Ids.Add(id);
            Write(state);
        }
    }

    /// <summary>
    /// Chamado depois de devolver os conectores ao automático numa saída normal. Os que já
    /// estavam órfãos quando o app abriu continuam anotados: para eles o "automático" que o app
    /// consegue restaurar é a velocidade fixa em que ficaram.
    /// </summary>
    public static void Release(IReadOnlyCollection<string> stillOrphaned)
    {
        lock (Gate)
        {
            if (stillOrphaned.Count == 0)
            {
                File.Delete(FilePath);
                return;
            }
            Write(new State { Boot = CurrentBoot, Ids = stillOrphaned.ToList() });
        }
    }

    private static State? Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void Write(State state)
    {
        try
        {
            Directory.CreateDirectory(ProfileStore.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
        }
        catch (IOException)
        {
            // Sem conseguir gravar, o pior caso é o aviso não aparecer na próxima abertura.
        }
    }
}
