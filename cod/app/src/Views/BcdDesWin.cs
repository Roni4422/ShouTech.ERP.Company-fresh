using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7B - "Design barcode" (Inventory > Barcode).
/// A mini label-design page: a style toolbar at the top controls label size,
/// fonts, colors, and visible elements, with a live preview of the barcode
/// label. The design persists to a local config file and can be reloaded.
/// </summary>
public sealed class BcdDesWin : BaseWin
{
    private readonly TextBox _txtWidth = new() { Width = 60, Text = "240" };
    private readonly TextBox _txtHeight = new() { Width = 60, Text = "150" };
    private readonly ComboBox _cbFont = new() { Width = 120 };
    private readonly ComboBox _cbFontSize = new() { Width = 55 };
    private readonly CheckBox _chkBold = new() { Content = "B", FontWeight = FontWeights.Bold, IsChecked = true };
    private readonly ComboBox _cbTextColor = new() { Width = 100 };
    private readonly ComboBox _cbBarColor = new() { Width = 100 };
    private readonly ComboBox _cbBackground = new() { Width = 100 };
    private readonly CheckBox _chkShowName = new() { Content = "Name", IsChecked = true };
    private readonly CheckBox _chkShowPrice = new() { Content = "Price", IsChecked = true };
    private readonly CheckBox _chkShowCode = new() { Content = "Code", IsChecked = true };
    private readonly Button _btnSave = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnReset = new();
    private readonly Button _btnPrint = new();

    private readonly TextBox _txtName = new() { Width = 150, Text = "Sample Product" };
    private readonly TextBox _txtPrice = new() { Width = 70, Text = "25.00" };
    private readonly TextBox _txtCode = new() { Width = 150, Text = "6281001234567" };

    private readonly Border _label = new();
    private readonly TextBlock _prevName = new();
    private readonly TextBlock _prevPrice = new();
    private readonly StackPanel _prevBars = new();
    private readonly TextBlock _prevCode = new();

    private static string DesignFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ShouTech");

    private static string DesignFile =>
        Path.Combine(DesignFolder, "barcode-design.cfg");

    public BcdDesWin()
    {
        Width = 860;
        Height = 600;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        LoadDesign();
        UpdatePreview();
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Inventory.DesignBarcode", "Design and print barcode");
    }

    private static string L(string key, string fallback)
    {
        try
        {
            var s = T(key);
            return string.IsNullOrWhiteSpace(s) || string.Equals(s, key, StringComparison.Ordinal)
                ? fallback
                : s;
        }
        catch
        {
            return fallback;
        }
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        root.Children.Add(BuildToolbar());

        // Main split: settings (left) + preview (right).
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var settingsCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1)
        };
        settingsCard.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        settingsCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        settingsCard.Child = BuildSettings();
        Grid.SetColumn(settingsCard, 0);
        split.Children.Add(settingsCard);

        var previewCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1)
        };
        previewCard.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        previewCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var previewHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        previewHost.Content = BuildPreview();
        previewCard.Child = previewHost;
        Grid.SetColumn(previewCard, 2);
        split.Children.Add(previewCard);

        root.Children.Add(split);
        Content = root;
    }

    private UIElement BuildToolbar()
    {
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        StyleButton(_btnSave, "Save design");
        _btnSave.Click += (_, _) => SaveDesign();
        toolbar.Children.Add(_btnSave);

        StyleButton(_btnLoad, "Load design");
        _btnLoad.Click += (_, _) => { LoadDesign(); UpdatePreview(); };
        toolbar.Children.Add(_btnLoad);

        StyleButton(_btnReset, "Reset");
        _btnReset.Click += (_, _) => { ResetDesign(); UpdatePreview(); };
        toolbar.Children.Add(_btnReset);

        StyleButton(_btnPrint, "Print");
        _btnPrint.Click += (_, _) => MessageBox.Show(
            this,
            "The current design is ready for printing.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        toolbar.Children.Add(_btnPrint);

        var dock = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        dock.Children.Add(toolbar);
        return toolbar;
    }

    private UIElement BuildSettings()
    {
        var panel = new StackPanel();

        // Label size.
        var sizeRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        sizeRow.Children.Add(MakeLabel("Label size:"));
        _txtWidth.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        sizeRow.Children.Add(_txtWidth);
        sizeRow.Children.Add(MakeLabel("x"));
        _txtHeight.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        sizeRow.Children.Add(_txtHeight);
        panel.Children.Add(sizeRow);

        // Font family + size + bold.
        var fontRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        fontRow.Children.Add(MakeLabel("Font:"));
        foreach (var family in new[] { "Segoe UI", "Tahoma", "Arial", "Consolas", "Times New Roman" })
            _cbFont.Items.Add(family);
        _cbFont.SelectedIndex = 0;
        fontRow.Children.Add(_cbFont);

        foreach (var size in new[] { "10", "12", "13", "14", "16", "18", "22" })
            _cbFontSize.Items.Add(size);
        _cbFontSize.SelectedItem = "13";
        fontRow.Children.Add(MakeLabel("Size:"));
        fontRow.Children.Add(_cbFontSize);
        _chkBold.Margin = new Thickness(8, 0, 0, 0);
        fontRow.Children.Add(_chkBold);
        panel.Children.Add(fontRow);

        // Colors.
        var colorRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        colorRow.Children.Add(MakeLabel("Text:"));
        foreach (var name in new[] { "Black", "Dark Blue", "Dark Red", "Forest Green", "Dim Gray" })
            _cbTextColor.Items.Add(name);
        _cbTextColor.SelectedIndex = 0;
        colorRow.Children.Add(_cbTextColor);
        colorRow.Children.Add(MakeLabel("Bars:"));
        foreach (var name in new[] { "Black", "Dark Blue", "Dark Red", "Brown" })
            _cbBarColor.Items.Add(name);
        _cbBarColor.SelectedIndex = 0;
        colorRow.Children.Add(_cbBarColor);
        panel.Children.Add(colorRow);

        var bgRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bgRow.Children.Add(MakeLabel("Background:"));
        foreach (var name in new[] { "White", "Ivory", "Light Yellow", "White Smoke" })
            _cbBackground.Items.Add(name);
        _cbBackground.SelectedIndex = 0;
        bgRow.Children.Add(_cbBackground);
        panel.Children.Add(bgRow);

        // Elements.
        var elementRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        elementRow.Children.Add(MakeLabel("Elements:"));
        elementRow.Children.Add(_chkShowName);
        _chkShowPrice.Margin = new Thickness(10, 0, 0, 0);
        elementRow.Children.Add(_chkShowPrice);
        _chkShowCode.Margin = new Thickness(10, 0, 0, 0);
        elementRow.Children.Add(_chkShowCode);
        panel.Children.Add(elementRow);

        // Sample content.
        var sep = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 8, 0, 8)
        };
        sep.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        panel.Children.Add(sep);

        panel.Children.Add(MakeLabel("Sample content:"));
        var nameRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        _txtName.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        _txtName.TextChanged += (_, _) => UpdatePreview();
        nameRow.Children.Add(MakeLabel("Name:"));
        nameRow.Children.Add(_txtName);
        panel.Children.Add(nameRow);

        var priceRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        _txtPrice.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        _txtPrice.TextChanged += (_, _) => UpdatePreview();
        priceRow.Children.Add(MakeLabel("Price:"));
        priceRow.Children.Add(_txtPrice);
        panel.Children.Add(priceRow);

        var codeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        _txtCode.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        _txtCode.TextChanged += (_, _) => UpdatePreview();
        codeRow.Children.Add(MakeLabel("Code:"));
        codeRow.Children.Add(_txtCode);
        panel.Children.Add(codeRow);

        // Live updates from all styling controls.
        _txtWidth.TextChanged += (_, _) => UpdatePreview();
        _txtHeight.TextChanged += (_, _) => UpdatePreview();
        _cbFont.SelectionChanged += (_, _) => UpdatePreview();
        _cbFontSize.SelectionChanged += (_, _) => UpdatePreview();
        _chkBold.Checked += (_, _) => UpdatePreview();
        _chkBold.Unchecked += (_, _) => UpdatePreview();
        _cbTextColor.SelectionChanged += (_, _) => UpdatePreview();
        _cbBarColor.SelectionChanged += (_, _) => UpdatePreview();
        _cbBackground.SelectionChanged += (_, _) => UpdatePreview();
        _chkShowName.Checked += (_, _) => UpdatePreview();
        _chkShowName.Unchecked += (_, _) => UpdatePreview();
        _chkShowPrice.Checked += (_, _) => UpdatePreview();
        _chkShowPrice.Unchecked += (_, _) => UpdatePreview();
        _chkShowCode.Checked += (_, _) => UpdatePreview();
        _chkShowCode.Unchecked += (_, _) => UpdatePreview();

        return panel;
    }

    private UIElement BuildPreview()
    {
        var host = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var caption = new TextBlock
        {
            Text = "Live preview",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        host.Children.Add(caption);

        _label.BorderThickness = new Thickness(1.5);
        _label.CornerRadius = new CornerRadius(5);
        _label.Padding = new Thickness(10);

        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _prevName.HorizontalAlignment = HorizontalAlignment.Center;
        _prevName.Margin = new Thickness(0, 0, 0, 4);
        content.Children.Add(_prevName);

        _prevPrice.HorizontalAlignment = HorizontalAlignment.Center;
        _prevPrice.Margin = new Thickness(0, 0, 0, 6);
        content.Children.Add(_prevPrice);

        _prevBars.Orientation = Orientation.Horizontal;
        _prevBars.HorizontalAlignment = HorizontalAlignment.Center;
        _prevBars.Height = 44;
        _prevBars.Margin = new Thickness(0, 0, 0, 4);
        content.Children.Add(_prevBars);

        _prevCode.FontFamily = new FontFamily("Consolas");
        _prevCode.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(_prevCode);

        _label.Child = content;
        host.Children.Add(_label);
        return host;
    }

    private void UpdatePreview()
    {
        // Label geometry.
        _ = double.TryParse(_txtWidth.Text, out var w);
        _ = double.TryParse(_txtHeight.Text, out var h);
        _label.Width = w > 40 ? w : 240;
        _label.Height = h > 30 ? h : 150;
        _label.Background = ResolveBrush(_cbBackground.SelectedItem as string, Brushes.White);
        _label.BorderBrush = new SolidColorBrush(Color.FromRgb(180, 185, 190));

        // Font styling.
        var family = new FontFamily((_cbFont.SelectedItem as string) ?? "Segoe UI");
        _ = double.TryParse(_cbFontSize.SelectedItem as string, out var size);
        if (size <= 0) size = 13;

        var textColor = ResolveBrush(_cbTextColor.SelectedItem as string, Brushes.Black);
        var barColor = ResolveBrush(_cbBarColor.SelectedItem as string, Brushes.Black);

        _prevName.Text = _chkShowName.IsChecked == true ? _txtName.Text : string.Empty;
        _prevName.FontFamily = family;
        _prevName.FontSize = size;
        _prevName.FontWeight = _chkBold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        _prevName.Foreground = textColor;

        _prevPrice.Text = _chkShowPrice.IsChecked == true ? _txtPrice.Text : string.Empty;
        _prevPrice.FontFamily = family;
        _prevPrice.FontSize = size + 1;
        _prevPrice.FontWeight = FontWeights.Bold;
        _prevPrice.Foreground = textColor;

        _prevCode.Text = _chkShowCode.IsChecked == true ? _txtCode.Text : string.Empty;
        _prevCode.FontSize = Math.Max(9, size - 2);
        _prevCode.Foreground = textColor;

        // Barcode bars (deterministic pseudo-render, same approach as BarWin).
        _prevBars.Children.Clear();
        var code = _txtCode.Text;
        if (string.IsNullOrWhiteSpace(code)) code = "12345678";

        for (var i = 0; i < code.Length * 3; i++)
        {
            var isBar = (i * 7 + (code[i % code.Length])) % 2 == 0;
            var width = (i % 5 == 0) ? 2.5 : 1.2;
            _prevBars.Children.Add(new Rectangle
            {
                Width = width,
                Height = 44,
                Fill = isBar ? barColor : Brushes.White,
                Margin = new Thickness(0.5, 0, 0.5, 0)
            });
        }
    }

    private static Brush ResolveBrush(string? name, Brush fallback) => name switch
    {
        "Black" => Brushes.Black,
        "Dark Blue" => Brushes.DarkBlue,
        "Dark Red" => Brushes.DarkRed,
        "Forest Green" => Brushes.ForestGreen,
        "Dim Gray" => Brushes.DimGray,
        "Brown" => Brushes.Brown,
        "White" => Brushes.White,
        "Ivory" => Brushes.Ivory,
        "Light Yellow" => Brushes.LightYellow,
        "White Smoke" => Brushes.WhiteSmoke,
        _ => fallback
    };

    private void SaveDesign()
    {
        try
        {
            Directory.CreateDirectory(DesignFolder);
            File.WriteAllLines(DesignFile, new[]
            {
                "width=" + _txtWidth.Text,
                "height=" + _txtHeight.Text,
                "font=" + (_cbFont.SelectedItem ?? string.Empty),
                "fontSize=" + (_cbFontSize.SelectedItem ?? string.Empty),
                "bold=" + (_chkBold.IsChecked == true),
                "textColor=" + (_cbTextColor.SelectedItem ?? string.Empty),
                "barColor=" + (_cbBarColor.SelectedItem ?? string.Empty),
                "background=" + (_cbBackground.SelectedItem ?? string.Empty),
                "showName=" + (_chkShowName.IsChecked == true),
                "showPrice=" + (_chkShowPrice.IsChecked == true),
                "showCode=" + (_chkShowCode.IsChecked == true),
                "sampleName=" + _txtName.Text,
                "samplePrice=" + _txtPrice.Text,
                "sampleCode=" + _txtCode.Text
            });

            MessageBox.Show(
                this,
                "Design saved.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadDesign()
    {
        try
        {
            if (!File.Exists(DesignFile))
                return;

            foreach (var line in File.ReadAllLines(DesignFile))
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0];
                var value = parts[1];

                switch (key)
                {
                    case "width": _txtWidth.Text = value; break;
                    case "height": _txtHeight.Text = value; break;
                    case "font": SelectItem(_cbFont, value); break;
                    case "fontSize": SelectItem(_cbFontSize, value); break;
                    case "bold": _chkBold.IsChecked = value == "True"; break;
                    case "textColor": SelectItem(_cbTextColor, value); break;
                    case "barColor": SelectItem(_cbBarColor, value); break;
                    case "background": SelectItem(_cbBackground, value); break;
                    case "showName": _chkShowName.IsChecked = value == "True"; break;
                    case "showPrice": _chkShowPrice.IsChecked = value == "True"; break;
                    case "showCode": _chkShowCode.IsChecked = value == "True"; break;
                    case "sampleName": _txtName.Text = value; break;
                    case "samplePrice": _txtPrice.Text = value; break;
                    case "sampleCode": _txtCode.Text = value; break;
                }
            }
        }
        catch
        {
            // A corrupt design file must never crash the window.
        }
    }

    private static void SelectItem(ComboBox combo, string value)
    {
        var index = combo.Items.IndexOf(value);
        if (index >= 0)
            combo.SelectedIndex = index;
    }

    private void ResetDesign()
    {
        _txtWidth.Text = "240";
        _txtHeight.Text = "150";
        _cbFont.SelectedIndex = 0;
        _cbFontSize.SelectedItem = "13";
        _chkBold.IsChecked = true;
        _cbTextColor.SelectedIndex = 0;
        _cbBarColor.SelectedIndex = 0;
        _cbBackground.SelectedIndex = 0;
        _chkShowName.IsChecked = true;
        _chkShowPrice.IsChecked = true;
        _chkShowCode.IsChecked = true;
        _txtName.Text = "Sample Product";
        _txtPrice.Text = "25.00";
        _txtCode.Text = "6281001234567";
    }

    private static TextBlock MakeLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return block;
    }

    private static void StyleButton(Button btn, string text)
    {
        btn.Content = text;
        btn.Margin = new Thickness(0, 0, 6, 0);
        btn.Padding = new Thickness(10, 4, 10, 4);
        btn.Height = 28;
        btn.SetResourceReference(StyleProperty, "VisuButtonStyle");
    }
}
