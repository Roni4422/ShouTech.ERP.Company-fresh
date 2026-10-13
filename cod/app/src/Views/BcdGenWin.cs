using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ShouTech.App.Common;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7B - "Generate barcode" (Inventory > Barcode).
/// Automatically assigns a unique EAN-13 barcode to ALL materials loaded
/// from the live materials repository. Materials that already carry a
/// barcode are skipped by default (regenerate mode can overwrite them),
/// and generated barcodes are written back to the material cards.
/// </summary>
public sealed class BcdGenWin : BaseWin
{
    private readonly TextBox _txtPrefix = new() { Width = 90, Text = "628" };
    private readonly Button _btnGenerateMissing = new();
    private readonly Button _btnGenerateAll = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnPrint = new();
    private readonly TextBox _txtSearch = new();
    private readonly TextBlock _status = new();
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        IsReadOnly = false,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single
    };

    private readonly List<MatRow> _rows = new();

    public BcdGenWin()
    {
        Width = 860;
        Height = 560;
        MinWidth = 660;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        LoadMaterials();
        ApplyFilter();
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        // Top toolbar.
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

        toolbar.Children.Add(MakeLabel(L("BarcodeGen.Prefix", "Prefix:")));
        _txtPrefix.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        toolbar.Children.Add(_txtPrefix);

        StyleButton(_btnGenerateMissing, L("BarcodeGen.GenerateMissing", "Generate for materials without barcode"));
        _btnGenerateMissing.Click += (_, _) => Generate(overwrite: false);
        toolbar.Children.Add(_btnGenerateMissing);

        StyleButton(_btnGenerateAll, L("BarcodeGen.RegenerateAll", "Regenerate all"));
        _btnGenerateAll.Click += (_, _) => Generate(overwrite: true);
        toolbar.Children.Add(_btnGenerateAll);

        StyleButton(_btnSave, T("Common.Save"));
        _btnSave.Click += (_, _) => SaveGenerated();
        toolbar.Children.Add(_btnSave);

        StyleButton(_btnPrint, T("Common.Print"));
        _btnPrint.Click += (_, _) => PrintGenerated();
        toolbar.Children.Add(_btnPrint);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // Search row.
        var searchPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _txtSearch.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        _txtSearch.TextChanged += (_, _) => ApplyFilter();
        var searchLabel = new TextBlock
        {
            Text = T("Buttons.Search") + ":",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        searchLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchPanel.Children.Add(searchLabel);
        searchPanel.Children.Add(_txtSearch);
        DockPanel.SetDock(searchPanel, Dock.Top);
        root.Children.Add(searchPanel);

        // Grid.
        BuildColumns();
        _grid.SetResourceReference(BackgroundProperty, "SurfaceSecondaryBrush");
        _grid.SetResourceReference(ForegroundProperty, "TextPrimary");
        root.Children.Add(_grid);

        // Status.
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.FontWeight = FontWeights.SemiBold;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        Content = root;
    }

    private void BuildColumns()
    {
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = T("Common.SelectAll"),
            Binding = new Binding(nameof(MatRow.Include))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
        _grid.Columns.Add(MakeCol(T("MatCard.Code"), nameof(MatRow.Code), 90));
        _grid.Columns.Add(MakeCol(T("MatCard.Name"), nameof(MatRow.Name), 230));
        _grid.Columns.Add(MakeCol(T("MatCard.Barcode"), nameof(MatRow.Barcode), 150));
        _grid.Columns.Add(MakeCol(T("Reports.Col.Status"), nameof(MatRow.Status), 140));
    }

    private static DataGridTextColumn MakeCol(string header, string prop, int width)
    {
        var col = new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(prop) { Mode = BindingMode.OneWay }
        };
        col.IsReadOnly = true;
        if (width > 0)
            col.Width = new DataGridLength(width);
        return col;
    }

    // ------------------------------------------------------------------
    // Data
    // ------------------------------------------------------------------

    private void LoadMaterials()
    {
        _rows.Clear();

        try
        {
            foreach (var material in MatRepo.LoadAll())
            {
                _rows.Add(new MatRow
                {
                    Id = material.Id,
                    Code = material.Code,
                    Name = DisplayName(material),
                    Barcode = material.Unit1Barcode ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string DisplayName(Services.MatRepo.MatRec material)
    {
        var isArabic = string.Equals(
            App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase);
        return isArabic || string.IsNullOrWhiteSpace(material.NameEn)
            ? material.NameAr
            : material.NameEn;
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

        var missing = source.Count(r => string.IsNullOrWhiteSpace(r.Barcode));
        _status.Text = string.Format(
            CultureInfo.CurrentCulture,
            L("BarcodeGen.Summary", "{0} of {1} material(s) shown. Missing barcode: {2}."),
            source.Count, _rows.Count, missing);
    }

    // ------------------------------------------------------------------
    // Generation
    // ------------------------------------------------------------------

    private void Generate(bool overwrite)
    {
        var prefix = (_txtPrefix.Text ?? string.Empty).Trim();
        if (prefix.Length == 0)
            prefix = "628";

        var used = new HashSet<string>(
            _rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Barcode))
                .Select(r => r.Barcode),
            StringComparer.Ordinal);

        var seq = _rows.Count > 0 ? _rows.Max(r => r.Id) : 0;
        var generated = 0;
        var skipped = 0;

        foreach (var row in _rows)
        {
            if (!string.IsNullOrWhiteSpace(row.Barcode) && !overwrite)
            {
                skipped++;
                continue;
            }

            string candidate;
            do
            {
                seq++;
                candidate = Code128.BuildEan13(prefix, seq);
            }
            while (!used.Add(candidate));

            row.Barcode = candidate;
            row.Status = L("BarcodeGen.Generated", "Generated");
            generated++;
        }

        _status.Text = string.Format(
            CultureInfo.CurrentCulture,
            L("BarcodeGen.Result", "Barcodes generated: {0}. Skipped (already coded): {1}. Total materials: {2}. Use Save to write them to the material cards."),
            generated, skipped, _rows.Count);

        _grid.Items.Refresh();
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    private void SaveGenerated()
    {
        var changed = _rows.Where(r => r.Include && !string.IsNullOrWhiteSpace(r.Barcode)).ToList();
        if (changed.Count == 0)
        {
            MessageBox.Show(this, T("Messages.NoData"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saved = 0;
        var failed = 0;

        foreach (var row in changed)
        {
            try
            {
                // Load the full record (children included) so saving the
                // barcode never wipes prices, units or extra barcodes.
                var full = MatRepo.LoadById(row.Id, includeChildren: true);
                if (full == null)
                {
                    failed++;
                    continue;
                }

                full.Unit1Barcode = row.Barcode;
                MatRepo.Save(full, isNew: false);
                saved++;
            }
            catch
            {
                failed++;
            }
        }

        _status.Text = string.Format(
            CultureInfo.CurrentCulture,
            L("BarcodeGen.SavedSummary", "Saved to material cards: {0}. Failed: {1}."),
            saved, failed);

        MessageBox.Show(
            this,
            string.Format(CultureInfo.CurrentCulture, "{0} ({1})", T("Messages.SaveSuccess"), saved),
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ------------------------------------------------------------------
    // Printing
    // ------------------------------------------------------------------

    private void PrintGenerated()
    {
        var printable = _rows
            .Where(r => r.Include && !string.IsNullOrWhiteSpace(r.Barcode))
            .ToList();

        if (printable.Count == 0)
        {
            MessageBox.Show(this, T("Messages.NoData"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // One page per material, one label per page keeps sizing simple
            // and predictable for bulk label stock.
            foreach (var row in printable)
            {
                var label = BarcodeLabelPrinter.BuildLabel(row.Name, row.Barcode);
                dialog.PrintVisual(label, Title + " — " + row.Code);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ------------------------------------------------------------------
    // Localization helpers
    // ------------------------------------------------------------------

    protected override void ApplyLocalizedText()
    {
        Title = L("Inventory.GenerateBarcode", "Generate barcode");
        _btnGenerateMissing.Content = L("BarcodeGen.GenerateMissing", "Generate for materials without barcode");
        _btnGenerateAll.Content = L("BarcodeGen.RegenerateAll", "Regenerate all");
        _btnSave.Content = T("Common.Save");
        _btnPrint.Content = T("Common.Print");
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

    private static TextBlock MakeLabel(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
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

    /// <summary>Row model. Barcode and status are mutated by the generator.</summary>
    public sealed class MatRow
    {
        public bool Include { get; set; } = true;
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
