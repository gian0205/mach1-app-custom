using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Mach1DriverCustom.Models;
using Mach1DriverCustom.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace Mach1DriverCustom.ViewModels;

/// <summary>Um elemento do layout personalizado, como o editor o mostra e altera.</summary>
public partial class LayoutElementViewModel : ViewModelBase
{
    private readonly Action _changed;

    public LayoutElementViewModel(LcdElement element, Action changed)
    {
        _changed = changed;
        Kind = element.Kind;
        _x = (decimal)element.X;
        _y = (decimal)element.Y;
        _width = (decimal)element.Width;
        _height = (decimal)element.Height;
        _text = element.Text ?? "";
        _sensor = Math.Clamp(element.Sensor, 0, LcdSensors.Names.Length - 1);
        _align = Math.Clamp(element.Align, 0, 2);
        _useCustomColor = element.Color is not null;
        _color = element.Color is uint argb ? Color.FromUInt32(argb) : Color.FromUInt32(LcdTheme.Default.Text);
        _path = element.Path;
    }

    public LcdElementKind Kind { get; }

    public bool IsText => Kind == LcdElementKind.Text;
    public bool IsImage => Kind == LcdElementKind.Image;
    public bool HasSensor => Kind is LcdElementKind.Sensor or LcdElementKind.Bar;
    public bool HasAlign => Kind is LcdElementKind.Text or LcdElementKind.Sensor;
    public bool HasColor => Kind != LcdElementKind.Image;

    // Posição e tamanho em pixels da tela (720x1280).

    [ObservableProperty]
    private decimal? _x;

    [ObservableProperty]
    private decimal? _y;

    [ObservableProperty]
    private decimal? _width;

    [ObservableProperty]
    private decimal? _height;

    [ObservableProperty]
    private string _text;

    private int _sensor;
    private int _align;

    // As listas da janela escrevem -1 por um instante ao trocar de elemento; valores fora da
    // faixa são ignorados em vez de gravados.
    public int Sensor
    {
        get => _sensor;
        set
        {
            if (value >= 0 && value < LcdSensors.Names.Length)
                SetProperty(ref _sensor, value);
        }
    }

    /// <summary>0 = à esquerda, 1 = centralizado, 2 = à direita.</summary>
    public int Align
    {
        get => _align;
        set
        {
            if (value is >= 0 and <= 2)
                SetProperty(ref _align, value);
        }
    }

    /// <summary>Desmarcado, o elemento segue a cor do tema.</summary>
    [ObservableProperty]
    private bool _useCustomColor;

    [ObservableProperty]
    private Color _color;

    [ObservableProperty]
    private string? _path;

    /// <summary>Como o elemento aparece na lista do editor.</summary>
    public string Title => Kind switch
    {
        LcdElementKind.Text => $"Texto: {(Text.Length > 0 ? Text : "(vazio)")}",
        LcdElementKind.Sensor => $"Sensor: {LcdSensors.Names[Sensor]}",
        LcdElementKind.Bar => $"Barra: {LcdSensors.Names[Sensor]}",
        _ => $"Imagem: {(Path is null ? "(sem arquivo)" : System.IO.Path.GetFileName(Path))}",
    };

    public LcdElement ToModel() => new()
    {
        Kind = Kind,
        X = (double)(X ?? 0),
        Y = (double)(Y ?? 0),
        Width = Math.Max(4, (double)(Width ?? 0)),
        Height = Math.Max(4, (double)(Height ?? 0)),
        Text = IsText ? Text : null,
        Sensor = Sensor,
        Align = Align,
        Color = UseCustomColor && HasColor ? Color.ToUInt32() : null,
        Path = IsImage ? Path : null,
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Text) or nameof(Sensor) or nameof(Path))
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Title)));
        if (e.PropertyName != nameof(Title))
            _changed();
    }
}
