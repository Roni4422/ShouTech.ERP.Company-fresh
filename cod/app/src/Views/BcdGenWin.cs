using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7B - "Generate barcode" (Inventory > Barcode).
/// Automatically assigns a unique barcode to ALL materials that do not
/// have one yet. Materials that already carry a barcode are skipped and
/// reported, so re-running is always safe.
/// </summary>
public sealed class BcdGenWin : BaseWin
{
    private readonly TextBox _txtPrefix = new() { Width = 90, Text = "628" };
    private readonly ComboBox _cbDigits = new() { Width = 90 };
    private readonly Button _btnGenerateAll = new();
    private readonly Button _btnGenerateChecked = new();
    private readonly Button _btnPrint = new();
    private readonly TextBox _txtSearch = new();
    private readonly TextBlock _status = new();
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single
    };

    // Demo data consistent with the current phase of the codebase
    // (BarWin also works on sample rows). Swap for the materials
    // repository once the data layer is wired.
    private readonly List<MatRow> _rows = new()
    {
        new MatRow { Code = "M-1001", Name = "Olive oil 1L", Barcode = "" },
        new MatRow { Code = "M-1002", Name = "Sunflower oil 1L", Barcode = "6281000000027" },
        new MatRow { Code = "M-1003", Name = "Sea salt 500g", Barcode = "" },
        new MatRow { Code = "M-1004", Name = "Brown sugar 1kg", Barcode = "" },
        new MatRow { Code = "M-1005", Name = "Green tea 100g", Barcode = "6281000000058" },
        new MatRow { Code = "M-1006", Name = "Cardamom 50g", Barcode = "" },
        new MatRow { Code = "M-1007", Name = "Laundry detergent 2L", Barcode = "" },
        new MatRow { Code = "M-1008", Name = "Dish soap 750ml", Barcode = "" }
    };

    public BcdGenWin()
    {
        Width = 820;
        Height = 540;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyFilter();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        // Top toolbar.
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        toolbar.Children.Add(MakeLabel("Prefix:"));
        _txtPrefix.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        toolbar.Children.Add(_txtPrefix);

        toolbar.Children.Add(MakeLabel("Digits:"));
        _cbDigits.Items.Add("9");
        _cbDigits.Items.Add("11");
        _cbDigits.Items.Add("13");
        _cbDigits.SelectedIndex = 0;
        toolbar.Children.Add(_cbDigits);

        StyleButton(_btnGenerateAll, "Generate for all materials");
        _btnGenerateAll.Click += (_, _) => Generate(checkedOnly: false);
        toolbar.Children.Add(_btnGenerateAll);

        StyleButton(_btnGenerateChecked, "Generate for checked only");
        _btnGenerateChecked.Click += (_, _) => Generate(checkedOnly: true);
        toolbar.Children.Add(_btnGenerateChecked);

        StyleButton(_btnPrint, "Print");
        _btnPrint.Click += (_, _) => MessageBox.Show(
            this,
            "Generated barcodes are ready for printing.",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        toolbar.Children.Add(_btnPrint);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // Search row.
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _txtSearch.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        _txtSearch.TextChanged += (_, _) => ApplyFilter();
        var searchLabel = new TextBlock
        {
            Text = "Search:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        searchLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchPanel.Children.Add(searchLabel);
        searchPanel.Children.Add(_txtSearch);
        DockPanel.SetDock(searchPanel, Dock.Top);
        root.Children.Add(searchPanel);

        // Grid.
        BuildColumns();
        _grid.SetResourceReference(BackgroundProperty, "SurfaceSecondaryBrush");
        _grid.SetResourceReference(ForegroundProperty, "TextBrush");
        root.Children.Add(_grid);

        // Status.
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.FontWeight = FontWeights.SemiBold;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        Content = root;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Include",
            Binding = new Binding(nameof(MatRow.Include))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
        _grid.Columns.Add(MakeCol("Code", nameof(MatRow.Code), 90));
        _grid.Columns.Add(MakeCol("Material", nameof(MatRow.Name), 230));
        _grid.Columns.Add(MakeCol("Barcode", nameof(MatRow.Barcode), 150));
        _grid.Columns.Add(MakeCol("Status", nameof(MatRow.Status), 140));
    }

    private static DataGridTextColumn MakeCol(string header, string prop, int width)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(prop),
            IsReadOnly = true
        };
        if (width > 0)
            col.Width = new DataGridLength(width);
        return col;
    }

    private void Generate(bool checkedOnly)
    {
        var prefix = (_txtPrefix.Text ?? string.Empty).Trim();
        if (prefix.Length == 0)
            prefix = "628";

        var digits = _cbDigits.SelectedItem as string ?? "9";
        _ = int.TryParse(digits, out var digitCount);
        if (digitCount < 6)
            digitCount = 9;

        var seq = 1;
        var generated = 0;
        var skipped = 0;

        foreach (var row in _rows)
        {
            if (checkedOnly && !row.Include)
                continue;

            if (!string.IsNullOrWhiteSpace(row.Barcode))
            {
                skipped++;
                continue;
            }

            row.Barcode = prefix + seq.ToString()
                .PadLeft(Math.Max(1, digitCount - prefix.Length), '0');
            seq++;
            generated++;
            row.Status = "Generated";
        }

        _status.Text = string.Format(
            "Barcodes generated: {0}. Skipped (already coded): {1}. Total materials: {2}.",
            generated, skipped, _rows.Count);

        _grid.Items.Refresh();
    }

    private void ApplyFilter()
    {
        var q = (_txtSearch.Text ?? string.Empty).Trim();
        var source = string.IsNullOrEmpty(q)
            ? _rows.ToList()
            : _rows.Where(r =>
                    (r.Code ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)
                 || (r.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)
                 || (r.Barcode ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();

        _grid.ItemsSource = source;

        _status.Text = string.Format(
            "{0} of {1} material(s) shown.",
            source.Count, _rows.Count);
    }

    protected override void ApplyLocalizedText()
    {
        Title = L("Inventory.GenerateBarcode", "Generate barcode");
    }

    private static TextBlock MakeLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return block;
    }

    private static void StyleButton(Button btn, string text)
    {
        btn.Content = text;
        btn.Margin = new Thickness(10, 0, 0, 0);
        btn.Padding = new Thickness(10, 4, 10, 4);
        btn.Height = 28;
        btn.SetResourceReference(StyleProperty, "VisuButtonStyle");
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

    /// <summary>Row model. Barcode and status are mutated by the generator.</summary>
    public sealed class MatRow
    {
        public bool Include { get; set; } = true;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
