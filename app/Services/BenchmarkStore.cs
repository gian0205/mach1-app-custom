using Mach1CustomApp.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Mach1CustomApp.Services;

/// <summary>Histórico de benchmarks, em %AppData%\Mach1CustomApp\benchmarks.json.</summary>
public static class BenchmarkStore
{
    private static string FilePath => Path.Combine(ProfileStore.Directory, "benchmarks.json");

    public static List<BenchmarkRun> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        return JsonSerializer.Deserialize<List<BenchmarkRun>>(File.ReadAllText(FilePath)) ?? [];
    }

    public static void Save(IEnumerable<BenchmarkRun> runs)
    {
        Directory.CreateDirectory(ProfileStore.Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(runs));
    }
}
