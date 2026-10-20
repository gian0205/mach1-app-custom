using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1CustomApp.Models;
using Mach1CustomApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Mach1CustomApp.ViewModels;

// Clima pela Open-Meteo, para a página da tela e os sensores do layout personalizado.
public partial class MainViewModel
{
    private const int WeatherPageMode = 9;
    private const int WeatherRefreshMinutes = 15;
    private static readonly TimeSpan LocationMaxAge = TimeSpan.FromHours(6);

    private WeatherSnapshot? _weather;
    private WeatherPlace? _manualPlace;
    private WeatherPlace? _autoPlace;
    private DateTime _autoPlaceAt;
    private DateTime _weatherDue = DateTime.MinValue;
    private bool _weatherBusy;

    [ObservableProperty]
    private bool _weatherEnabled;

    /// <summary>Localização automática (Windows e, se preciso, internet) em vez da cidade escolhida.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WeatherManualLocation))]
    private bool _weatherAutoLocation = true;

    public bool WeatherManualLocation
    {
        get => !WeatherAutoLocation;
        set => WeatherAutoLocation = !value;
    }

    /// <summary>Sem a localização do Windows, usar a aproximada pelo endereço de internet.</summary>
    [ObservableProperty]
    private bool _weatherUseInternet = true;

    [ObservableProperty]
    private string _weatherCitySearch = "";

    public ObservableCollection<WeatherPlace> WeatherCityResults { get; } = [];

    [ObservableProperty]
    private bool _hasWeatherCityResults;

    [ObservableProperty]
    private WeatherPlace? _selectedWeatherCity;

    [ObservableProperty]
    private string _weatherStatus = "Desligado.";

    private void LoadWeatherSettings(Profile profile)
    {
        WeatherEnabled = profile.WeatherEnabled;
        WeatherAutoLocation = profile.WeatherAutoLocation;
        WeatherUseInternet = profile.WeatherUseInternet;
        _manualPlace = profile.WeatherCity is { Length: > 0 } city
            ? new WeatherPlace(city, profile.WeatherCityDetail ?? "", profile.WeatherLatitude, profile.WeatherLongitude)
            : null;
        WeatherStatus = WeatherEnabled ? "Buscando a previsão..." : "Desligado.";
    }

    private void StoreWeatherSettings(Profile profile)
    {
        profile.WeatherEnabled = WeatherEnabled;
        profile.WeatherAutoLocation = WeatherAutoLocation;
        profile.WeatherUseInternet = WeatherUseInternet;
        profile.WeatherCity = _manualPlace?.Name;
        profile.WeatherCityDetail = _manualPlace?.Detail;
        profile.WeatherLatitude = _manualPlace?.Latitude ?? 0;
        profile.WeatherLongitude = _manualPlace?.Longitude ?? 0;
    }

    partial void OnWeatherEnabledChanged(bool value)
    {
        if (_loading)
            return;

        _weatherDue = DateTime.MinValue;
        if (value)
        {
            WeatherStatus = "Buscando a previsão...";
        }
        else
        {
            _weather = null;
            WeatherStatus = "Desligado.";
        }
    }

    partial void OnWeatherAutoLocationChanged(bool value) => _weatherDue = DateTime.MinValue;

    partial void OnWeatherUseInternetChanged(bool value)
    {
        _autoPlace = null;
        _weatherDue = DateTime.MinValue;
    }

    partial void OnSelectedWeatherCityChanged(WeatherPlace? value)
    {
        if (value is null || _loading)
            return;

        _manualPlace = value;
        WeatherAutoLocation = false;
        _weatherDue = DateTime.MinValue;
        SaveSettings();
    }

    [RelayCommand]
    private async Task SearchWeatherCityAsync()
    {
        if (string.IsNullOrWhiteSpace(WeatherCitySearch))
            return;

        WeatherStatus = "Procurando a cidade...";
        try
        {
            IReadOnlyList<WeatherPlace> places = await WeatherClient.SearchAsync(WeatherCitySearch);
            WeatherCityResults.Clear();
            foreach (WeatherPlace place in places)
                WeatherCityResults.Add(place);
            HasWeatherCityResults = places.Count > 0;

            if (places.Count == 0)
                WeatherStatus = "Nenhuma cidade encontrada com esse nome.";
            else if (places.Count == 1)
                SelectedWeatherCity = places[0];
            else
                WeatherStatus = $"{places.Count} cidades encontradas: escolha uma na lista.";
        }
        catch (Exception ex)
        {
            WeatherStatus = "A busca não respondeu: " + ex.Message;
        }
    }

    /// <summary>Busca de novo a localização e a previsão.</summary>
    [RelayCommand]
    private void RefreshWeather()
    {
        _autoPlace = null;
        _weatherDue = DateTime.MinValue;
    }

    /// <summary>Chamado a cada segundo, sem ser esperado: busca a previsão quando ela vence.</summary>
    private async Task UpdateWeatherAsync()
    {
        if (!WeatherEnabled || _weatherBusy || DateTime.Now < _weatherDue)
            return;

        _weatherBusy = true;
        try
        {
            WeatherPlace? place = await ResolveWeatherPlaceAsync();
            if (place is null)
            {
                _weatherDue = DateTime.Now.AddMinutes(5);
                return;
            }

            WeatherSnapshot weather = await WeatherClient.GetForecastAsync(place);
            if (!WeatherEnabled)
                return;

            _weather = weather;
            _weatherDue = DateTime.Now.AddMinutes(WeatherRefreshMinutes);
            WeatherStatus = $"{place}: {weather.Temperature:0.0} °C, {WeatherCodes.Describe(weather.Code).ToLowerInvariant()}. " +
                            $"Atualizado às {DateTime.Now:HH:mm}; a próxima busca é em {WeatherRefreshMinutes} minutos.";
        }
        catch (Exception ex)
        {
            _weatherDue = DateTime.Now.AddMinutes(2);
            WeatherStatus = "A Open-Meteo não respondeu: " + ex.Message + " Nova tentativa em 2 minutos." +
                            (_weather is null ? "" : " A tela continua com a última previsão.");
        }
        finally
        {
            _weatherBusy = false;
        }
    }

    private async Task<WeatherPlace?> ResolveWeatherPlaceAsync()
    {
        if (!WeatherAutoLocation)
        {
            if (_manualPlace is null)
                WeatherStatus = "Digite o nome da cidade, clique em Buscar e escolha uma na lista.";
            return _manualPlace;
        }

        if (_autoPlace is not null && DateTime.Now - _autoPlaceAt < LocationMaxAge)
            return _autoPlace;

        WeatherPlace? place = await WeatherClient.LocateByWindowsAsync();
        if (place is null && WeatherUseInternet)
            place = await WeatherClient.LocateByInternetAsync();

        if (place is null)
        {
            WeatherStatus = WeatherUseInternet
                ? "Não deu para descobrir a localização: a do Windows está desligada e a pela internet não respondeu. Nova tentativa em 5 minutos, ou escolha uma cidade."
                : "A localização do Windows está desligada ou não respondeu. Ligue-a em Configurações > Privacidade > Localização, marque a opção pela internet ou escolha uma cidade.";
            return null;
        }

        _autoPlace = place;
        _autoPlaceAt = DateTime.Now;
        return place;
    }
}
