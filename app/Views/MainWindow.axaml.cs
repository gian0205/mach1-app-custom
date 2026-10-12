using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Mach1DriverCustom.ViewModels;
using System.Collections.Generic;

namespace Mach1DriverCustom.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WheelGuard.Attach(this);
    }

    private LayoutEditorWindow? _layoutEditor;

    /// <summary>Abre o editor de layout, ou traz para a frente se já estiver aberto.</summary>
    private void OnEditLayout(object? sender, RoutedEventArgs e) => OpenLayoutEditor();

    public void OpenLayoutEditor()
    {
        if (_layoutEditor is not null)
        {
            _layoutEditor.Activate();
            return;
        }

        _layoutEditor = new LayoutEditorWindow { DataContext = DataContext };
        _layoutEditor.Closed += (_, _) => _layoutEditor = null;
        _layoutEditor.Show(this);
    }

    /// <summary>Grava o painel de comparação (legenda, gráficos e tabela) em um PNG.</summary>
    private async void OnExportComparison(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar os gráficos",
            SuggestedFileName = $"benchmark-{System.DateTime.Now:yyyy-MM-dd-HHmm}",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("Imagem PNG") { Patterns = ["*.png"] }],
        });
        if (file is null)
            return;

        await using System.IO.Stream stream = await file.OpenWriteAsync();
        SaveVisual(ComparisonPanel, stream);
    }

    /// <summary>Desenha um controle em um PNG com o dobro da resolução da tela.</summary>
    public static void SaveVisual(Control control, System.IO.Stream stream)
    {
        const double scale = 2;
        var size = new PixelSize((int)(control.Bounds.Width * scale), (int)(control.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(control);
        bitmap.Save(stream);
    }

    // O seletor de arquivos precisa da janela, por isso fica aqui e não no view model.
    private async void OnPickLcdImage(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Fundo da tela",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagens, GIFs e vídeos")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.mp4", "*.m4v", "*.mov", "*.mkv", "*.webm", "*.avi"],
                },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && DataContext is MainViewModel viewModel)
            viewModel.LcdImagePath = path;
    }
}
