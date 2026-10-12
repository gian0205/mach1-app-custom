using System.Collections.Generic;

namespace Mach1DriverCustom.Models;

/// <summary>
/// Tudo o que o app guarda em disco. A curva e os limites só mudam quando o usuário salva o
/// perfil; as demais opções são gravadas assim que mudam.
/// </summary>
public sealed class Profile
{
    // Curva e limites. Valem até o reboot depois de aplicados.

    /// <summary>Margem do Curve Optimizer por núcleo físico, em contagens (negativo = undervolt).</summary>
    public int[] CoreMargins { get; set; } = [];

    public bool ChangeLimits { get; set; }

    /// <summary>PPT em watts.</summary>
    public int Ppt { get; set; }

    /// <summary>TDC em ampères.</summary>
    public int Tdc { get; set; }

    /// <summary>EDC em ampères.</summary>
    public int Edc { get; set; }

    // Inicialização

    /// <summary>Aplicar a curva salva quando o app sobe junto com o Windows.</summary>
    public bool ApplyOnStart { get; set; }

    /// <summary>Fechar a janela mantém o app na bandeja.</summary>
    public bool CloseToTray { get; set; } = true;

    // Tela do water cooler

    public bool LcdEnabled { get; set; }

    /// <summary>Brilho da tela, de 0 a 100.</summary>
    public int LcdBrightness { get; set; } = 50;

    /// <summary>Imagem girada 180°, para tela montada de cabeça para baixo.</summary>
    public bool LcdFlipped { get; set; } = true;

    /// <summary>Cores do painel, em hexadecimal (#AARRGGBB); null usa o padrão.</summary>
    public string? LcdBackground { get; set; }

    public string? LcdText { get; set; }

    public string? LcdAccent { get; set; }

    /// <summary>0 = temperaturas e sensores, 1 = Curve Optimizer, 2 = alterna as duas, 3 = layout personalizado.</summary>
    public int LcdPageMode { get; set; }

    /// <summary>Layout personalizado, montado no editor.</summary>
    public LcdLayout Layout { get; set; } = new();

    public int LcdPageSeconds { get; set; } = 8;

    /// <summary>Imagem ou GIF de fundo; null deixa a cor de fundo lisa.</summary>
    public string? LcdImagePath { get; set; }

    /// <summary>Opacidade da cor de fundo sobre a imagem ou o vídeo, de 0 (só a imagem) a 100 (só a cor).</summary>
    public int LcdImageDim { get; set; } = 55;

    // Alerta de temperatura

    public bool AlertEnabled { get; set; }

    public int AlertCpu { get; set; } = 85;

    public int AlertGpu { get; set; } = 83;

    // Cores da temperatura. Faixas em 0 e cores nulas significam "automático".

    /// <summary>A temperatura muda de cor conforme sobe.</summary>
    public bool TempColorsEnabled { get; set; } = true;

    public int CpuWarmAt { get; set; }

    public int CpuHotAt { get; set; }

    public int GpuWarmAt { get; set; }

    public int GpuHotAt { get; set; }

    public string? TempWarnColor { get; set; }

    public string? TempHotColor { get; set; }

    // Perfis por jogo

    public List<GameProfile> Games { get; set; } = [];
}

/// <summary>Margem aplicada a todos os núcleos enquanto um executável está aberto.</summary>
public sealed class GameProfile
{
    /// <summary>Nome do executável, por exemplo "cs2.exe".</summary>
    public string Executable { get; set; } = "";

    public int Margin { get; set; }

    public string Summary => $"{Executable}: margem {Margin:+0;-0;0}";
}
