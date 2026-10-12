using Mach1DriverCustom.Models;
using System;
using System.IO;
using System.Text.Json;

namespace Mach1DriverCustom.Services;

public static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mach1DriverCustom");

    public static string ProfilePath => Path.Combine(Directory, "profile.json");

    public static Profile? Load()
    {
        if (!File.Exists(ProfilePath))
            return null;

        return JsonSerializer.Deserialize<Profile>(File.ReadAllText(ProfilePath), JsonOptions);
    }

    public static void Save(Profile profile)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
    }
}
