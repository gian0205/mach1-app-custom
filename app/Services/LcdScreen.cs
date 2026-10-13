using HidSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mach1CustomApp.Services;

/// <summary>Uma tela encontrada no USB.</summary>
/// <param name="Tested">O app já foi testado com este modelo.</param>
public sealed record LcdScreenInfo(int VendorId, int ProductId, string Name, bool Tested)
{
    public string Id => $"{VendorId:X4}:{ProductId:X4}";
}

/// <summary>
/// Tamanho da tela em uso e a procura por telas no USB. O tamanho é definido uma vez, ao abrir
/// o app, porque o desenho, o vídeo de fundo e o editor de layout são montados em cima dele.
/// </summary>
public static class LcdScreen
{
    /// <summary>A IRV360, único modelo com que o app foi testado: USB 1600:3005, 720x1280.</summary>
    public const int TestedVendorId = 0x1600, TestedProductId = 0x3005;

    public const int DefaultWidth = 720, DefaultHeight = 1280;
    public const int MinSide = 120, MaxSide = 2560;

    // Todas as telas da família recebem relatórios HID de 1024 bytes (mais o byte do ID).
    private const int ReportLength = 1025;

    public static int Width { get; private set; } = DefaultWidth;

    public static int Height { get; private set; } = DefaultHeight;

    /// <summary>Define a resolução; valores fora da faixa (ou zero, de perfil antigo) viram o padrão.</summary>
    public static void Configure(int width, int height)
    {
        bool valid = width is >= MinSide and <= MaxSide && height is >= MinSide and <= MaxSide;
        Width = valid ? width : DefaultWidth;
        Height = valid ? height : DefaultHeight;
    }

    /// <summary>
    /// Procura uma tela da mesma fabricante: o identificador de fornecedor 1600 ou o nome
    /// "HOTSPOTEK", e relatórios do tamanho que o protocolo usa. O modelo já testado tem
    /// preferência quando há mais de uma.
    /// </summary>
    public static (LcdScreenInfo Info, HidDevice Device)? Find()
    {
        var found = new List<(LcdScreenInfo Info, HidDevice Device)>();
        foreach (HidDevice device in DeviceList.Local.GetHidDevices())
        {
            try
            {
                string name = SafeProductName(device);
                bool sameMaker = device.VendorID == TestedVendorId || name.Contains("HOTSPOTEK", StringComparison.OrdinalIgnoreCase);
                if (!sameMaker || device.GetMaxOutputReportLength() != ReportLength)
                    continue;

                bool tested = device.VendorID == TestedVendorId && device.ProductID == TestedProductId;
                found.Add((new LcdScreenInfo(device.VendorID, device.ProductID, name, tested), device));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Dispositivo que não deixa ler os dados; não é o que procuramos.
            }
        }

        return found.Count == 0 ? null : found.OrderByDescending(f => f.Info.Tested).First();
    }

    private static string SafeProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName() ?? "";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
