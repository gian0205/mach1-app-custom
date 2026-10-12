using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mach1DriverCustom.Models;
using Mach1DriverCustom.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Mach1DriverCustom.ViewModels;

// Layout personalizado da tela e o estado do editor.
public partial class MainViewModel
{
    private const int CustomPageMode = 3;

    private bool _editingLayout;
    private bool _loadingLayout;

    /// <summary>Elementos do layout, de trás para a frente.</summary>
    public ObservableCollection<LayoutElementViewModel> LayoutElements { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedElement))]
    private LayoutElementViewModel? _selectedElement;

    public bool HasSelectedElement => SelectedElement is not null;

    public IReadOnlyList<string> SensorNames => LcdSensors.Names;

    private LcdLayout BuildLayout() => new() { Elements = LayoutElements.Select(e => e.ToModel()).ToList() };

    private void LoadLayout(Profile profile)
    {
        _loadingLayout = true;
        LayoutElements.Clear();
        foreach (LcdElement element in profile.Layout.Elements)
            LayoutElements.Add(new LayoutElementViewModel(element, OnLayoutChanged));
        _loadingLayout = false;
        _lcd.Layout = BuildLayout();
    }

    /// <summary>Qualquer mudança em um elemento vai direto para o painel; o disco fica para o fim da edição.</summary>
    private void OnLayoutChanged()
    {
        if (!_loadingLayout)
            _lcd.Layout = BuildLayout();
    }

    /// <summary>Chamado ao abrir o editor: mostra o layout personalizado e acelera o redesenho.</summary>
    public void BeginLayoutEditing()
    {
        if (LayoutElements.Count == 0)
            ApplyLayoutTemplate();

        _editingLayout = true;
        LcdPageMode = CustomPageMode;
        _lcd.Responsive = true;
        UpdateLcdActive();
    }

    /// <summary>Chamado ao fechar o editor: grava o layout.</summary>
    public void EndLayoutEditing()
    {
        _editingLayout = false;
        _lcd.Responsive = false;
        UpdateLcdActive();
        SaveSettings();
    }

    private void AddElement(LcdElement element)
    {
        var item = new LayoutElementViewModel(element, OnLayoutChanged);
        LayoutElements.Add(item);
        SelectedElement = item;
        OnLayoutChanged();
    }

    [RelayCommand]
    private void AddTextElement() =>
        AddElement(new LcdElement { Kind = LcdElementKind.Text, X = 56, Y = 600, Width = 400, Height = 48, Text = "Texto" });

    [RelayCommand]
    private void AddSensorElement() =>
        AddElement(new LcdElement { Kind = LcdElementKind.Sensor, X = 56, Y = 600, Width = 400, Height = 96, Sensor = 0 });

    [RelayCommand]
    private void AddBarElement() =>
        AddElement(new LcdElement { Kind = LcdElementKind.Bar, X = 56, Y = 600, Width = 608, Height = 14, Sensor = 2 });

    /// <summary>Chamado pela janela do editor depois de o usuário escolher o arquivo.</summary>
    public void AddImageElement(string path) =>
        AddElement(new LcdElement { Kind = LcdElementKind.Image, X = 210, Y = 490, Width = 300, Height = 300, Path = path });

    [RelayCommand]
    private void RemoveElement()
    {
        if (SelectedElement is not { } element)
            return;

        int index = LayoutElements.IndexOf(element);
        LayoutElements.Remove(element);
        SelectedElement = LayoutElements.Count > 0 ? LayoutElements[System.Math.Min(index, LayoutElements.Count - 1)] : null;
        OnLayoutChanged();
    }

    [RelayCommand]
    private void DuplicateElement()
    {
        if (SelectedElement is not { } element)
            return;

        LcdElement copy = element.ToModel();
        copy.X += 24;
        copy.Y += 24;
        AddElement(copy);
    }

    /// <summary>Um passo para a frente: o elemento passa a ser desenhado por cima do seguinte.</summary>
    [RelayCommand]
    private void BringElementForward() => MoveSelectedElement(+1);

    [RelayCommand]
    private void SendElementBackward() => MoveSelectedElement(-1);

    private void MoveSelectedElement(int step)
    {
        if (SelectedElement is not { } element)
            return;

        int from = LayoutElements.IndexOf(element), to = from + step;
        if (to < 0 || to >= LayoutElements.Count)
            return;

        LayoutElements.Move(from, to);
        SelectedElement = element;
        OnLayoutChanged();
    }

    /// <summary>Troca o layout por um modelo parecido com a página de temperaturas, para servir de ponto de partida.</summary>
    [RelayCommand]
    private void ApplyLayoutTemplate()
    {
        LcdElement Label(string text, double y) => new() { Kind = LcdElementKind.Text, X = 56, Y = y, Width = 350, Height = 40, Text = text, Color = 0xFF95A0B5 };
        LcdElement Value(int sensor, double y, double height, int align = 0, double x = 56, double width = 608) =>
            new() { Kind = LcdElementKind.Sensor, X = x, Y = y, Width = width, Height = height, Sensor = sensor, Align = align };
        LcdElement Bar(int sensor, double y) => new() { Kind = LcdElementKind.Bar, X = 56, Y = y, Width = 608, Height = 12, Sensor = sensor };

        LcdElement[] template =
        [
            Value(8, 60, 56, align: 2),
            Label("Processador", 150), Value(0, 196, 200), Bar(0, 410),
            Label("Placa de vídeo", 470), Value(1, 516, 200), Bar(1, 730),
            Label("Uso do processador", 820), Value(2, 816, 46, align: 2, x: 420, width: 244), Bar(2, 876),
            Label("Uso da placa de vídeo", 940), Value(3, 936, 46, align: 2, x: 420, width: 244), Bar(3, 996),
            Label("Memória", 1060), Value(4, 1056, 46, align: 2, x: 420, width: 244), Bar(4, 1116),
        ];

        _loadingLayout = true;
        LayoutElements.Clear();
        foreach (LcdElement element in template)
            LayoutElements.Add(new LayoutElementViewModel(element, OnLayoutChanged));
        _loadingLayout = false;

        SelectedElement = null;
        OnLayoutChanged();
    }
}
