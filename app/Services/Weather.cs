using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace Mach1CustomApp.Services;

/// <summary>Uma hora da previsão, no horário do lugar.</summary>
/// <param name="RainChance">Chance de chuva, em %, ou null quando a previsão não informa.</param>
public sealed record WeatherHour(DateTime Time, float Temperature, int Code, bool IsDay, int? RainChance);

/// <summary>Um dia da previsão.</summary>
public sealed record WeatherDay(DateTime Date, int Code, float Max, float Min, int? RainChance);

/// <summary>O tempo agora e a previsão, do jeito que o painel desenha.</summary>
/// <param name="Place">Nome do lugar, quando se sabe; a localização do Windows só dá coordenadas.</param>
/// <param name="Code">Código WMO do tempo (ver <see cref="WeatherCodes"/>).</param>
/// <param name="Hours">As próximas horas, começando pela atual.</param>
/// <param name="Days">Os próximos dias, começando por hoje.</param>
public sealed record WeatherSnapshot(string? Place, DateTime UpdatedAt, float Temperature, float? FeelsLike, int? Humidity,
    float? WindKmh, float? WindFrom, int Code, bool IsDay, IReadOnlyList<WeatherHour> Hours, IReadOnlyList<WeatherDay> Days);

/// <summary>Um lugar para a previsão.</summary>
/// <param name="Name">Cidade; vazio quando só há coordenadas.</param>
/// <param name="Detail">Estado e país, ou de onde veio a posição.</param>
public sealed record WeatherPlace(string Name, string Detail, double Latitude, double Longitude)
{
    public override string ToString() => (Name, Detail) switch
    {
        ("", _) => string.Create(CultureInfo.InvariantCulture, $"{Latitude:0.00}, {Longitude:0.00} ({Detail})"),
        (_, "") => Name,
        _ => $"{Name}, {Detail}",
    };
}

/// <summary>Os desenhos de tempo que o painel sabe fazer.</summary>
public enum WeatherIcon { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Storm }

/// <summary>Os códigos de tempo da OMM (WMO) que a Open-Meteo usa.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Céu limpo",
        1 => "Poucas nuvens",
        2 => "Parcialmente nublado",
        3 => "Nublado",
        45 or 48 => "Neblina",
        51 or 53 => "Garoa",
        55 => "Garoa forte",
        56 or 57 => "Garoa congelante",
        61 => "Chuva fraca",
        63 => "Chuva",
        65 => "Chuva forte",
        66 or 67 => "Chuva congelante",
        71 or 73 or 75 or 77 => "Neve",
        80 => "Pancadas de chuva",
        81 => "Pancadas de chuva",
        82 => "Pancadas fortes",
        85 or 86 => "Pancadas de neve",
        95 => "Trovoadas",
        96 or 99 => "Trovoadas com granizo",
        _ => "—",
    };

    public static WeatherIcon Icon(int code) => code switch
    {
        0 => WeatherIcon.Clear,
        1 or 2 => WeatherIcon.PartlyCloudy,
        45 or 48 => WeatherIcon.Fog,
        >= 51 and <= 57 => WeatherIcon.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => WeatherIcon.Rain,
        (>= 71 and <= 77) or 85 or 86 => WeatherIcon.Snow,
        >= 95 => WeatherIcon.Storm,
        _ => WeatherIcon.Cloudy,
    };

    /// <summary>De onde o vento vem, em pontos cardeais, em português (L de leste, O de oeste).</summary>
    public static string Compass(float degrees) =>
        new[] { "N", "NE", "L", "SE", "S", "SO", "O", "NO" }[(int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8];
}

/// <summary>
/// Previsão pela Open-Meteo (open-meteo.com: sem chave, para uso não comercial, dados em CC BY 4.0) e
/// a localização para ela: a do Windows ou, se ela estiver desligada, a aproximada pelo endereço de
/// internet (ipinfo.io).
/// </summary>
public static class WeatherClient
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MACH1-custom-app (+https://github.com/gian0205/mach1-app-custom)");
        return client;
    }

    public static async Task<WeatherSnapshot> GetForecastAsync(WeatherPlace place)
    {
        string url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude:0.####}&longitude={place.Longitude:0.####}") +
            "&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m,wind_direction_10m" +
            "&hourly=temperature_2m,precipitation_probability,weather_code,is_day" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
            "&timezone=auto&forecast_days=7&wind_speed_unit=kmh";

        using JsonDocument document = JsonDocument.Parse(await Http.GetStringAsync(url));
        JsonElement root = document.RootElement;

        JsonElement current = root.GetProperty("current");
        DateTime now = ParseTime(current.GetProperty("time"));

        // As horas vêm no fuso do lugar, a partir da meia-noite de hoje: pula as que já passaram.
        JsonElement hourly = root.GetProperty("hourly");
        JsonElement[] hourTimes = Items(hourly, "time"), hourTemps = Items(hourly, "temperature_2m"),
            hourRain = Items(hourly, "precipitation_probability"), hourCodes = Items(hourly, "weather_code"), hourDay = Items(hourly, "is_day");
        DateTime thisHour = now.Date.AddHours(now.Hour);
        var hours = new List<WeatherHour>();
        for (int i = 0; i < hourTimes.Length && hours.Count < 25; i++)
        {
            DateTime time = ParseTime(hourTimes[i]);
            if (time < thisHour || Number(hourTemps[i]) is not float temperature)
                continue;
            hours.Add(new WeatherHour(time, temperature, (int)(Number(hourCodes[i]) ?? 3), Number(hourDay[i]) != 0, Whole(hourRain[i])));
        }

        JsonElement daily = root.GetProperty("daily");
        JsonElement[] dayTimes = Items(daily, "time"), dayCodes = Items(daily, "weather_code"),
            dayMax = Items(daily, "temperature_2m_max"), dayMin = Items(daily, "temperature_2m_min"), dayRain = Items(daily, "precipitation_probability_max");
        var days = new List<WeatherDay>();
        for (int i = 0; i < dayTimes.Length; i++)
        {
            if (Number(dayMax[i]) is not float max || Number(dayMin[i]) is not float min)
                continue;
            days.Add(new WeatherDay(ParseTime(dayTimes[i]), (int)(Number(dayCodes[i]) ?? 3), max, min, Whole(dayRain[i])));
        }

        return new WeatherSnapshot(
            place.Name.Length > 0 ? place.Name : null,
            DateTime.Now,
            Number(current.GetProperty("temperature_2m")) ?? float.NaN,
            Number(current.GetProperty("apparent_temperature")),
            Whole(current.GetProperty("relative_humidity_2m")),
            Number(current.GetProperty("wind_speed_10m")),
            Number(current.GetProperty("wind_direction_10m")),
            (int)(Number(current.GetProperty("weather_code")) ?? 3),
            Number(current.GetProperty("is_day")) != 0,
            hours, days);
    }

    /// <summary>Procura cidades pelo nome, na busca da própria Open-Meteo.</summary>
    public static async Task<IReadOnlyList<WeatherPlace>> SearchAsync(string name)
    {
        string url = "https://geocoding-api.open-meteo.com/v1/search?count=8&language=pt&format=json&name=" + Uri.EscapeDataString(name.Trim());
        using JsonDocument document = JsonDocument.Parse(await Http.GetStringAsync(url));
        if (!document.RootElement.TryGetProperty("results", out JsonElement results))
            return [];

        return results.EnumerateArray()
            .Select(r => new WeatherPlace(
                Text(r, "name") ?? "?",
                string.Join(", ", new[] { Text(r, "admin1"), Text(r, "country") }.Where(s => !string.IsNullOrEmpty(s))),
                r.GetProperty("latitude").GetDouble(),
                r.GetProperty("longitude").GetDouble()))
            .ToList();
    }

    /// <summary>A posição pelo serviço de localização do Windows; null se ele estiver desligado ou não responder.</summary>
    public static async Task<WeatherPlace?> LocateByWindowsAsync()
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
                return null;

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            Geoposition position = await locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(15));
            BasicGeoposition point = position.Coordinate.Point.Position;
            return new WeatherPlace("", "localização do Windows", point.Latitude, point.Longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A posição aproximada pelo endereço de internet, pelo ipinfo.io. Costuma acertar a cidade ou
    /// cair na do provedor; null se o serviço não responder.
    /// </summary>
    public static async Task<WeatherPlace?> LocateByInternetAsync()
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(await Http.GetStringAsync("https://ipinfo.io/json"));
            JsonElement root = document.RootElement;
            string[] parts = (Text(root, "loc") ?? "").Split(',');
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
                return null;

            string detail = string.Join(", ", new[] { Text(root, "region"), "aproximada pela internet" }.Where(s => !string.IsNullOrEmpty(s)));
            return new WeatherPlace(Text(root, "city") ?? "", detail, latitude, longitude);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static JsonElement[] Items(JsonElement parent, string name) => parent.GetProperty(name).EnumerateArray().ToArray();

    private static float? Number(JsonElement element) => element.ValueKind == JsonValueKind.Number ? element.GetSingle() : null;

    private static int? Whole(JsonElement element) => Number(element) is float value ? (int)Math.Round(value) : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTime ParseTime(JsonElement element) =>
        DateTime.Parse(element.GetString() ?? "", CultureInfo.InvariantCulture, DateTimeStyles.None);
}
