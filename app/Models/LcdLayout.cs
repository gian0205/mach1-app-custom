using System.Collections.Generic;

namespace Mach1DriverCustom.Models;

public enum LcdElementKind
{
    /// <summary>Texto fixo.</summary>
    Text,

    /// <summary>Valor de um sensor, como "41.1 °C".</summary>
    Sensor,

    /// <summary>Barra horizontal preenchida conforme um sensor.</summary>
    Bar,

    /// <summary>Imagem ou GIF.</summary>
    Image,
}

/// <summary>
/// Um elemento do layout personalizado. As coordenadas são em pixels da tela (720x1280), com a
/// origem no canto superior esquerdo.
/// </summary>
public sealed class LcdElement
{
    public LcdElementKind Kind { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    /// <summary>Altura da caixa; em texto e sensor ela define o tamanho da letra.</summary>
    public double Height { get; set; }

    /// <summary>Texto fixo (só em <see cref="LcdElementKind.Text"/>).</summary>
    public string? Text { get; set; }

    /// <summary>Índice do sensor em LcdSensors (sensor e barra).</summary>
    public int Sensor { get; set; }

    /// <summary>0 = à esquerda, 1 = centralizado, 2 = à direita (texto e sensor).</summary>
    public int Align { get; set; }

    /// <summary>Cor em 0xAARRGGBB; null usa a cor do tema (texto, ou destaque na barra).</summary>
    public uint? Color { get; set; }

    /// <summary>Arquivo da imagem ou do GIF (só em <see cref="LcdElementKind.Image"/>).</summary>
    public string? Path { get; set; }
}

/// <summary>Layout personalizado: os elementos são desenhados na ordem da lista, o último por cima.</summary>
public sealed class LcdLayout
{
    public List<LcdElement> Elements { get; set; } = [];
}
