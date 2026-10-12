using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mach1DriverCustom.ViewModels;
using System;
using System.Collections.Generic;

namespace Mach1DriverCustom.Views;

/// <summary>Editor do layout personalizado da tela do water cooler.</summary>
public partial class LayoutEditorWindow : Window
{
    // A prévia acompanha o arrasto; o painel redesenha dez vezes por segundo enquanto a janela está aberta.
    private readonly DispatcherTimer _preview = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public LayoutEditorWindow()
    {
        InitializeComponent();
        WheelGuard.Attach(this);
        _preview.Tick += (_, _) => (DataContext as MainViewModel)?.RefreshLcdPreview();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        (DataContext as MainViewModel)?.BeginLayoutEditing();
        _preview.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _preview.Stop();
        (DataContext as MainViewModel)?.EndLayoutEditing();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Delete remove o elemento selecionado, menos quando o foco está em uma caixa de texto.
        if (e.Key == Key.Delete && !e.Handled && e.Source is not TextBox && DataContext is MainViewModel viewModel)
        {
            viewModel.RemoveElementCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnAddImage(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Imagem ou GIF",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagens e GIFs") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp"] },
            ],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path && DataContext is MainViewModel viewModel)
            viewModel.AddImageElement(path);
    }
}
