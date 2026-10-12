using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShouTech.App.Views;

/// <summary>
/// </summary>
public sealed class BarWin : BaseWin
{
    private readonly TextBox _txtItemCode = new() { Width = 130, Height = 26, Text = "ITEM-001" };
    private readonly TextBox _txtItemName = new() { Width = 230, Height = 26, Text = "Sample Product" };
    private readonly TextBox _txtBarcode = new() { Width = 180, Height = 26, Text = "6281001234567" };
    private readonly TextBox _txtPrice = new() { Width = 100, Height = 26, Text = "25.00" };
    private readonly ComboBox _cbType = new() { Width = 120, Height = 26 };
    private readonly CheckBox _chkIncludePrice = new() { IsChecked = true, Margin = new Thickness(0, 4, 10, 4) };
    private readonly CheckBox _chkIncludeName = new() { IsChecked = true, Margin = new Thickness(0, 4, 10, 4) };
    private readonly TextBox _txtCopies = new() { Width = 70, Height = 26, Text = "1" };

    private readonly Button _btnGen = new();
    private readonly Button _btnPrint = new();

    private readonly Border _previewCard = new();
    private readonly TextBlock _prevName = new();
    private readonly TextBlock _prevPrice = new();
    private readonly TextBlock _prevCode = new();
    private readonly StackPanel _prevBarcodeBars = new();

    public BarWin()
    {
        Width = 740;
        Height = 490;
        MinWidth = 650;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        UpdatePreview();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        // 1. Toolbar
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        StyleButton(_btnGen, "Common.New");
        StyleButton(_btnPrint, "BarcodeTool.Print");

        _btnGen.Click += (_, _) =>
        {
            var rnd = new Random();
            _txtBarcode.Text = "628" + rnd.Next(100000000, 999999999);
            UpdatePreview();
        };

        _btnPrint.Click += (_, _) => MessageBox.Show(this, T("BarcodeTool.PrintSuccess"), T("BarcodeTool.Print"), MessageBoxButton.OK, MessageBoxImage.Information);

        toolbar.Children.Add(_btnGen);
        toolbar.Children.Add(_btnPrint);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // 2. Main split layout
        var splitGrid = new Grid();
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var formPanel = new StackPanel { Margin = new Thickness(2) };

        var r1 = new WrapPanel();
        r1.Children.Add(Labeled("Invoice.Col.Code", _txtItemCode));
        r1.Children.Add(Labeled("BarcodeTool.Type", _cbType));
        formPanel.Children.Add(r1);

        formPanel.Children.Add(Labeled("Invoice.Col.Description", _txtItemName));

        var r2 = new WrapPanel();
        r2.Children.Add(Labeled("BarcodeTool.Value", _txtBarcode));
        r2.Children.Add(Labeled("Invoice.Col.SalePrice", _txtPrice));
        formPanel.Children.Add(r2);

        var r3 = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        r3.Children.Add(_chkIncludeName);
        r3.Children.Add(_chkIncludePrice);
        formPanel.Children.Add(r3);

        var r4 = new WrapPanel();
        r4.Children.Add(Labeled("BarcodeTool.Copies", _txtCopies));
        formPanel.Children.Add(r4);

        _txtItemName.TextChanged += (_, _) => UpdatePreview();
        _txtBarcode.TextChanged += (_, _) => UpdatePreview();
        _txtPrice.TextChanged += (_, _) => UpdatePreview();
        _chkIncludeName.Checked += (_, _) => UpdatePreview();
        _chkIncludeName.Unchecked += (_, _) => UpdatePreview();
        _chkIncludePrice.Checked += (_, _) => UpdatePreview();
        _chkIncludePrice.Unchecked += (_, _) => UpdatePreview();

        Grid.SetColumn(formPanel, 0);
        splitGrid.Children.Add(formPanel);

        // Right Preview Container
        var previewContainer = new Border
        {
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        previewContainer.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        previewContainer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var previewStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var previewLabel = new TextBlock
        {
            Text = T("BarcodeTool.Preview"),
            Tag = "BarcodeTool.Preview",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        previewStack.Children.Add(previewLabel);

        _previewCard.Width = 240;
        _previewCard.Height = 150;
        _previewCard.Background = Brushes.White;
        _previewCard.BorderBrush = new SolidColorBrush(Color.FromRgb(180, 185, 190));
        _previewCard.BorderThickness = new Thickness(1.5);
        _previewCard.CornerRadius = new CornerRadius(5);
        _previewCard.Padding = new Thickness(10);

        var cardContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        _prevName.FontSize = 13;
        _prevName.FontWeight = FontWeights.Bold;
        _prevName.Foreground = Brushes.Black;
        _prevName.HorizontalAlignment = HorizontalAlignment.Center;
        _prevName.Margin = new Thickness(0, 0, 0, 4);
        cardContent.Children.Add(_prevName);

        _prevPrice.FontSize = 14;
        _prevPrice.FontWeight = FontWeights.Bold;
        _prevPrice.Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210));
        _prevPrice.HorizontalAlignment = HorizontalAlignment.Center;
        _prevPrice.Margin = new Thickness(0, 0, 0, 6);
        cardContent.Children.Add(_prevPrice);

        _prevBarcodeBars.Orientation = Orientation.Horizontal;
        _prevBarcodeBars.HorizontalAlignment = HorizontalAlignment.Center;
        _prevBarcodeBars.Height = 44;
        _prevBarcodeBars.Margin = new Thickness(0, 0, 0, 4);
        cardContent.Children.Add(_prevBarcodeBars);

        _prevCode.FontSize = 11;
        _prevCode.FontFamily = new FontFamily("Consolas");
        _prevCode.Foreground = Brushes.Black;
        _prevCode.HorizontalAlignment = HorizontalAlignment.Center;
        cardContent.Children.Add(_prevCode);

        _previewCard.Child = cardContent;
        previewStack.Children.Add(_previewCard);

        previewContainer.Child = previewStack;
        Grid.SetColumn(previewContainer, 2);
        splitGrid.Children.Add(previewContainer);

        root.Children.Add(splitGrid);
        Content = root;
    }

    private void UpdatePreview()
    {
        _prevName.Text = _chkIncludeName.IsChecked == true ? _txtItemName.Text : string.Empty;
        _prevPrice.Text = _chkIncludePrice.IsChecked == true ? $"{_txtPrice.Text} SAR" : string.Empty;
        _prevCode.Text = _txtBarcode.Text;

        _prevBarcodeBars.Children.Clear();
        var code = _txtBarcode.Text;
        if (string.IsNullOrWhiteSpace(code)) code = "12345678";

        for (var i = 0; i < code.Length * 3; i++)
        {
            var isBar = (i * 7 + (code[i % code.Length])) % 2 == 0;
            var width = (i % 5 == 0) ? 2.5 : 1.2;
            var bar = new Rectangle
            {
                Width = width,
                Height = 44,
                Fill = isBar ? Brushes.Black : Brushes.White,
                Margin = new Thickness(0.5, 0, 0.5, 0)
            };
            _prevBarcodeBars.Children.Add(bar);
        }
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("BarcodeTool.Title");
        _chkIncludeName.Content = T("BarcodeTool.IncludeName");
        _chkIncludePrice.Content = T("BarcodeTool.IncludePrice");

        _cbType.Items.Clear();
        _cbType.Items.Add("Code 128");
        _cbType.Items.Add("EAN-13");
        _cbType.Items.Add("QR Code");
        _cbType.SelectedIndex = 0;

        RefreshLocalizedContent(Content as DependencyObject);
        UpdatePreview();
    }

    private static void RefreshLocalizedContent(DependencyObject? parent)
    {
        if (parent == null) return;
        if (parent is Button button && button.Tag is string bKey)
            button.Content = T(bKey);
        if (parent is TextBlock tb && tb.Tag is string tKey)
            tb.Text = T(tKey);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            RefreshLocalizedContent(VisualTreeHelper.GetChild(parent, i));
    }

    private static StackPanel Labeled(string key, UIElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 6) };
        sp.Children.Add(new TextBlock { Text = T(key), Tag = key, Margin = new Thickness(0, 0, 0, 2), Opacity = 0.85 });
        if (control is TextBox textBox)
            textBox.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        sp.Children.Add(control);
        return sp;
    }

    private static void StyleButton(Button btn, string key)
    {
        btn.Content = T(key);
        btn.Margin = new Thickness(0, 0, 5, 0);
        btn.Padding = new Thickness(9, 3, 9, 3);
        btn.MinWidth = 76;
        btn.Height = 28;
        btn.Tag = key;
        btn.SetResourceReference(StyleProperty, "VisuButtonStyle");
    }
}
