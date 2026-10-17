using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ShouTech.App.Reporting;
using ShouTech.App.Reporting.Invoices;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

/// <summary>
/// Shared editor for sales, purchase, return, and point-of-sale documents.
/// </summary>
public sealed class InvoiceWin : BaseWin
{
    private readonly InvDoc _doc;
    private readonly ObservableCollection<LineVm> _lines = new();
    private readonly TextBox _txtDocNo = new() { IsReadOnly = true, Width = 140 };
    private readonly DatePicker _dtDate = new() { SelectedDate = DateTime.Today, Width = 140 };
    private readonly TextBox _txtPartyCode = new() { Width = 120 };
    private readonly TextBox _txtPartyName = new() { Width = 220 };
    private readonly TextBox _txtTaxRate = new() { Width = 70 };
    private readonly TextBox _txtCurrency = new() { Width = 70 };
    private readonly TextBox _txtNotes = new() { Height = 48, AcceptsReturn = true };
    private readonly TextBlock _lblSubtotal = new();
    private readonly TextBlock _lblTax = new();
    private readonly TextBlock _lblTotal = new();
    private readonly TextBlock _lblStatus = new();
    private readonly DataGrid _grid = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnPost = new();
    private readonly Button _btnReverse = new();
    private readonly Button _btnPrint = new();
    private readonly Button _btnPdf = new();
    private readonly Button _btnAddLine = new();
    private readonly Button _btnRemoveLine = new();
    private readonly Button _btnFindMaterial = new();

    public InvoiceWin() : this(InvDocKind.SalesInvoice) { }

    public InvoiceWin(InvDocKind kind)
    {
        if (kind == InvDocKind.PosSale)
            throw new ArgumentException("Point-of-sale documents must use the POS checkout workflow.", nameof(kind));

        _doc = new InvDoc
        {
            Kind = kind,
            DefaultTaxRate = TaxSvc.GetDefaultRate(),
            CurrencyCode = InvRepo.GetCompanyCurrencyCode()
        };
        Width = 1100;
        Height = 680;
        MinWidth = 840;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        BuildUi();
        _doc.DocNo = InvRepo.NextDocNo(kind);
        _txtDocNo.Text = _doc.DocNo;
        _txtCurrency.Text = _doc.CurrencyCode;
        _txtTaxRate.Text = _doc.DefaultTaxRate.ToString("0.##", CultureInfo.InvariantCulture);
        _lines.Add(new LineVm { TaxRate = _doc.DefaultTaxRate });
        _txtPartyCode.LostFocus += (_, _) => ResolveParty();
        RefreshTotals();
    }

    public InvoiceWin(long docId, InvDocKind kind = InvDocKind.SalesInvoice) : this(kind)
    {
        var loaded = InvRepo.Load(docId, kind);
        if (loaded is not null) ApplyDoc(loaded);
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(8) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnPost, "Invoice.Post");
        StyleButton(_btnReverse, "Invoice.Reverse");
        _btnReverse.IsEnabled = false;
        _btnReverse.Click += (_, _) => ReverseCurrentDocument();
        StyleButton(_btnPrint, "Invoice.Print");
        StyleButton(_btnPdf, "Invoice.Pdf");
        StyleButton(_btnAddLine, "Invoice.AddLine");
        StyleButton(_btnRemoveLine, "Invoice.RemoveLine");
        StyleButton(_btnFindMaterial, "Invoice.FindMaterial");
        _btnSave.Click += (_, _) => SaveDraft();
        _btnPost.Click += (_, _) => PostDoc();
        _btnPrint.Click += (_, _) => InvPrn.Print(CaptureDoc(), CurrentCompanyDisplay);
        _btnPdf.Click += (_, _) => InvPrn.ExportPdf(CaptureDoc(), CurrentCompanyDisplay);
        _btnAddLine.Click += (_, _) =>
        {
            _lines.Add(new LineVm { TaxRate = ParseDec(_txtTaxRate.Text, TaxSvc.GetDefaultRate()) });
            RefreshTotals();
        };
        _btnRemoveLine.Click += (_, _) =>
        {
            if (_grid.SelectedItem is LineVm line) { _lines.Remove(line); RefreshTotals(); }
        };
        _btnFindMaterial.Click += (_, _) => PickMaterial();
        foreach (var b in new[] { _btnSave, _btnPost, _btnReverse, _btnPrint, _btnPdf, _btnAddLine, _btnRemoveLine, _btnFindMaterial })
            toolbar.Children.Add(b);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        footer.Children.Add(_lblSubtotal);
        footer.Children.Add(Spacer());
        footer.Children.Add(_lblTax);
        footer.Children.Add(Spacer());
        footer.Children.Add(_lblTotal);
        footer.Children.Add(Spacer());
        footer.Children.Add(_lblStatus);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(Labeled("Invoice.DocNo", _txtDocNo));
        header.Children.Add(Labeled("Invoice.Date", _dtDate));
        header.Children.Add(Labeled(PartyCodeKey(), _txtPartyCode));
        header.Children.Add(Labeled(PartyNameKey(), _txtPartyName));
        header.Children.Add(Labeled("Invoice.TaxRate", _txtTaxRate));
        header.Children.Add(Labeled("Invoice.Currency", _txtCurrency));
        Grid.SetRow(header, 0);
        body.Children.Add(header);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.RowHeight = 28;
        _grid.ColumnHeaderHeight = 34;
        _grid.AlternationCount = 2;
        _grid.SetResourceReference(StyleProperty, "VisuDataGridStyle");
        _grid.SetResourceReference(DataGrid.RowStyleProperty, "VisuDataGridRowStyle");
        _grid.SetResourceReference(DataGrid.CellStyleProperty, "VisuDataGridCellStyle");
        _grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        _grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "TableAlternateRowBrush");

        var customHeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
            (Style)FindResource("VisuDataGridHeaderStyle"));
        customHeaderStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource(GetHeaderBrushKey(_doc.Kind))));
        customHeaderStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = customHeaderStyle;

        _grid.ItemsSource = _lines;
        _grid.Columns.Add(TextCol(nameof(LineVm.MaterialCode), 100));
        _grid.Columns.Add(TextCol(nameof(LineVm.Description), 260));
        _grid.Columns.Add(TextCol(nameof(LineVm.Unit), 70));
        _grid.Columns.Add(TextCol(nameof(LineVm.QtyText), 90));
        _grid.Columns.Add(TextCol(nameof(LineVm.PriceText), 100));
        _grid.Columns.Add(TextCol(nameof(LineVm.DiscText), 80));
        _grid.Columns.Add(TextCol(nameof(LineVm.TaxText), 80));
        _grid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(LineVm.LineTotalText)) { Mode = BindingMode.OneWay },
            Width = 110,
            IsReadOnly = true
        });
        _grid.CellEditEnding += OnCellEditEnding;
        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);

        var notes = Labeled("Invoice.Notes", _txtNotes);
        Grid.SetRow(notes, 2);
        body.Children.Add(notes);
        root.Children.Add(body);
        Content = root;
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not LineVm line) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Column is DataGridTextColumn col &&
                col.Binding is Binding b &&
                b.Path.Path == nameof(LineVm.MaterialCode))
            {
                var mat = TaxSvc.ResolveMaterial(line.MaterialCode);
                if (mat is not null)
                {
                    line.MaterialId = mat.Value.Id;
                    if (string.IsNullOrWhiteSpace(line.Description)) line.Description = mat.Value.Name;
                    var material = MatRepo.LoadById(mat.Value.Id ?? 0);
                    if (string.IsNullOrWhiteSpace(line.Unit))
                        line.Unit = _doc.IsSalesSide
                            ? material?.SalesUnit ?? mat.Value.Unit
                            : material?.PurchaseUnit ?? mat.Value.Unit;
                    if (line.UnitPrice == 0)
                        line.UnitPrice = DefaultPrice(material, mat.Value.Price);
                    if (mat.Value.TaxRate > 0) line.TaxRate = mat.Value.TaxRate;
                    else if (line.TaxRate == 0) line.TaxRate = TaxSvc.GetDefaultRate();
                }
            }
            RefreshTotals();
        });
    }

    private void PickMaterial()
    {
        var selected = _grid.SelectedItem as LineVm;
        if (selected == null)
        {
            selected = _lines.FirstOrDefault(line => string.IsNullOrWhiteSpace(line.MaterialCode));
            if (selected == null)
            {
                selected = new LineVm { TaxRate = _doc.DefaultTaxRate };
                _lines.Add(selected);
            }
        }

        var material = SerWin.PickMaterial(this, selected.MaterialCode);
        if (material == null)
            return;

        selected.MaterialId = material.Id;
        selected.MaterialCode = material.Code;
        selected.Description = string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase) ||
                              string.IsNullOrWhiteSpace(material.NameEn)
            ? material.NameAr
            : material.NameEn;
        selected.Unit = _doc.IsSalesSide
            ? FirstNonEmpty(material.SalesUnit, material.DefaultUnit, material.Unit1)
            : FirstNonEmpty(material.PurchaseUnit, material.DefaultUnit, material.Unit1);
        selected.UnitPrice = DefaultPrice(material, 0m);
        selected.TaxRate = material.TaxRate > 0 ? material.TaxRate : _doc.DefaultTaxRate;
        _grid.SelectedItem = selected;
        _grid.ScrollIntoView(selected);
        _grid.Focus();
        RefreshTotals();
    }

    private decimal DefaultPrice(MatRepo.MatRec? material, decimal fallback)
    {
        if (material == null)
            return fallback;
        if (_doc.IsSalesSide)
            return material.PriceRetail > 0 ? material.PriceRetail : material.PriceList;
        if (material.LastPurchaseCost > 0)
            return material.LastPurchaseCost;
        if (material.Cost > 0)
            return material.Cost;
        if (material.StandardCost > 0)
            return material.StandardCost;
        return fallback;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private void ResolveParty()
    {
        var found = TaxSvc.ResolveParty(_txtPartyCode.Text, customer: _doc.IsSalesSide);
        if (found is null) return;
        _doc.PartyId = found.Value.Id;
        _txtPartyName.Text = found.Value.Name;
    }

    private static TextBlock Spacer() => new() { Width = 24 };
    private static DataGridTextColumn TextCol(string path, double width)
        => new() { Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = width };

    private static StackPanel Labeled(string key, UIElement control)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 3) };
        sp.Children.Add(new TextBlock { Text = T(key), Tag = key, Margin = new Thickness(0, 0, 0, 1), Opacity = 0.85 });
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

    protected override void ApplyLocalizedText()
    {
        Title = T(_doc.Kind switch
        {
            InvDocKind.SalesReturn => "Invoice.SalesReturnTitle",
            InvDocKind.PurchaseInvoice => "Invoice.PurchaseTitle",
            InvDocKind.PurchaseReturn => "Invoice.PurchaseReturnTitle",
            InvDocKind.StockIn => "Invoice.StockInTitle",
            InvDocKind.StockOut => "Invoice.StockOutTitle",
            InvDocKind.StockTransfer => "Invoice.StockTransferTitle",
            InvDocKind.OpeningStock => "Invoice.OpeningStockTitle",
            InvDocKind.InventoryCount => "Invoice.InventoryCountTitle",
            InvDocKind.Quotation => "Invoice.QuotationTitle",
            _ => "Invoice.SalesTitle"
        });
        _btnReverse.Content = string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase) ? "عكس الفاتورة" : "Reverse invoice";
        RefreshLocalizedContent(Content as DependencyObject);
        if (_grid.Columns.Count >= 8)
        {
            _grid.Columns[0].Header = T("Invoice.Col.Code");
            _grid.Columns[1].Header = T("Invoice.Col.Description");
            _grid.Columns[2].Header = T("Invoice.Col.Unit");
            _grid.Columns[3].Header = T("Invoice.Col.Qty");
            _grid.Columns[4].Header = T(_doc.IsSalesSide ? "Invoice.Col.SalePrice" : "Invoice.Col.PurchasePrice");
            _grid.Columns[5].Header = T("Invoice.Col.Discount");
            _grid.Columns[6].Header = T("Invoice.Col.TaxRate");
            _grid.Columns[7].Header = T("Invoice.Col.LineTotal");
        }
        RefreshTotals();
    }

    private static string GetHeaderBrushKey(InvDocKind kind) => kind switch
    {
        InvDocKind.SalesInvoice => "TableHeaderSalesBrush",
        InvDocKind.PurchaseInvoice => "TableHeaderPurchaseBrush",
        InvDocKind.SalesReturn => "TableHeaderSalesReturnBrush",
        InvDocKind.PurchaseReturn => "TableHeaderPurchaseReturnBrush",
        InvDocKind.StockIn => "TableHeaderStockInBrush",
        InvDocKind.StockOut => "TableHeaderStockOutBrush",
        InvDocKind.StockTransfer => "TableHeaderTransferBrush",
        InvDocKind.OpeningStock => "TableHeaderOpeningStockBrush",
        InvDocKind.InventoryCount => "TableHeaderStockCountBrush",
        InvDocKind.Quotation => "TableHeaderQuotationBrush",
        _ => "TableHeaderCyanBrush"
    };

    private static void RefreshLocalizedContent(DependencyObject? parent)
    {
        if (parent is Button button && button.Tag is string buttonKey)
            button.Content = T(buttonKey);
        if (parent is TextBlock textBlock && textBlock.Tag is string textKey)
            textBlock.Text = T(textKey);
        if (parent == null)
            return;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            RefreshLocalizedContent(VisualTreeHelper.GetChild(parent, i));
    }

    private string PartyCodeKey()
        => _doc.IsSalesSide ? "Invoice.CustomerCode" : "Invoice.SupplierCode";

    private string PartyNameKey()
        => _doc.IsSalesSide ? "Invoice.CustomerName" : "Invoice.SupplierName";

    private void ApplyDoc(InvDoc doc)
    {
        _doc.DocId = doc.DocId;
        _doc.DocNo = doc.DocNo;
        _doc.DocDate = doc.DocDate;
        _doc.PartyId = doc.PartyId;
        _doc.PartyCode = doc.PartyCode;
        _doc.PartyName = doc.PartyName;
        _doc.CurrencyCode = doc.CurrencyCode;
        _doc.DefaultTaxRate = doc.DefaultTaxRate;
        _doc.Notes = doc.Notes;
        _doc.Status = doc.Status;
        _doc.Kind = doc.Kind;
        _txtDocNo.Text = doc.DocNo;
        _dtDate.SelectedDate = doc.DocDate;
        _txtPartyCode.Text = doc.PartyCode;
        _txtPartyName.Text = doc.PartyName;
        _txtTaxRate.Text = doc.DefaultTaxRate.ToString("0.##", CultureInfo.InvariantCulture);
        _txtCurrency.Text = doc.CurrencyCode;
        _txtNotes.Text = doc.Notes ?? "";
        _lines.Clear();
        foreach (var l in doc.Lines) _lines.Add(LineVm.From(l));
        if (_lines.Count == 0) _lines.Add(new LineVm { TaxRate = doc.DefaultTaxRate });
        var locked = doc.Status == InvDocStatus.Posted;
        _btnSave.IsEnabled = !locked;
        _btnReverse.IsEnabled = locked && doc.Kind is InvDocKind.SalesInvoice or InvDocKind.PurchaseInvoice or InvDocKind.PosSale;
        if (_btnReverse.IsEnabled)
        {
            try { _btnReverse.IsEnabled = !InvRepo.HasCompletedReversal(doc.DocId, doc.Kind); }
            catch { _btnReverse.IsEnabled = false; }
        }
        _btnPost.IsEnabled = !locked;
        _grid.IsReadOnly = locked;
        RefreshTotals();
    }

    private InvDoc CaptureDoc()
    {
        _doc.DocNo = _txtDocNo.Text.Trim();
        _doc.DocDate = _dtDate.SelectedDate ?? DateTime.Today;
        _doc.PartyCode = _txtPartyCode.Text.Trim();
        _doc.PartyName = _txtPartyName.Text.Trim();
        _doc.CurrencyCode = string.IsNullOrWhiteSpace(_txtCurrency.Text) ? InvRepo.GetCompanyCurrencyCode() : _txtCurrency.Text.Trim().ToUpperInvariant();
        _doc.DefaultTaxRate = ParseDec(_txtTaxRate.Text, TaxSvc.GetDefaultRate());
        _doc.Notes = _txtNotes.Text;
        _doc.Lines = _lines.Select(l => l.ToLine()).Where(l => !string.IsNullOrWhiteSpace(l.Description) || l.Qty != 0).ToList();
        _doc.RenumberLines();
        return _doc;
    }

    private void SaveDraft()
    {
        try
        {
            ResolveParty();
            var doc = CaptureDoc();
            if (doc.Lines.Count == 0) { ShowWarn("Invoice.NeedLines"); return; }
            InvRepo.SaveDraft(doc);
            _txtDocNo.Text = doc.DocNo;
            ShowInfo("Invoice.Saved");
            RefreshTotals();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PostDoc()
    {
        if (!AskConfirm("Invoice.ConfirmPost")) return;
        try
        {
            ResolveParty();
            var doc = CaptureDoc();
            if (doc.DocId <= 0) InvRepo.SaveDraft(doc);
            InvRepo.Post(doc.DocId, doc.Kind, AppState.CurrentUser?.Username ?? Environment.UserName);
            doc.Status = InvDocStatus.Posted;
            _btnSave.IsEnabled = false;
            _btnPost.IsEnabled = false;
            _grid.IsReadOnly = true;
            ShowInfo("Invoice.Posted");
            RefreshTotals();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void ReverseCurrentDocument()
    {
        var current = CaptureDoc();
        if (current.DocId <= 0 || current.Status != InvDocStatus.Posted)
        {
            ShowWarn("Only a posted invoice can be reversed.");
            return;
        }

        var reason = PromptReversalReason();
        if (string.IsNullOrWhiteSpace(reason)) return;
        var confirm = MessageBox.Show(
            this,
            $"A reversal document will be created for {current.DocNo}. The original invoice will remain unchanged.\n\nReason: {reason}\n\nContinue?",
            string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase) ? "تأكيد عكس الفاتورة" : "Confirm invoice reversal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var reversalId = InvRepo.ReversePostedInvoice(
                current.DocId,
                current.Kind,
                reason,
                AppState.CurrentUser?.Username ?? Environment.UserName);
            _btnReverse.IsEnabled = false;
            MessageBox.Show(
                this,
                $"Reversal completed. Reversal document ID: {reversalId}.",
                string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase) ? "تم عكس الفاتورة" : "Reversal completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string? PromptReversalReason()
    {
        var ar = string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase);
        var dialog = new Window
        {
            Title = ar ? "سبب عكس الفاتورة" : "Invoice reversal reason",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 460,
            Height = 220,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = ar ? "متابعة" : "Continue", MinWidth = 90, Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = ar ? "إلغاء" : "Cancel", MinWidth = 90, Margin = new Thickness(4), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = ar ? "أدخل سبب العكس (إلزامي):" : "Enter the reason for reversal (required):", Margin = new Thickness(0, 0, 0, 8) });
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 1000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        input.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        stack.Children.Add(input);
        root.Children.Add(stack);
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                MessageBox.Show(dialog, ar ? "سبب العكس إلزامي." : "A reversal reason is required.", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        dialog.Content = root;
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }

    private void RefreshTotals()
    {
        var doc = CaptureDoc();
        _lblSubtotal.Text = $"{T("Invoice.Subtotal")}: {FormatAmount(doc.Subtotal)}";
        _lblTax.Text = $"{T("Invoice.Tax")}: {FormatAmount(doc.TaxAmount)}";
        _lblTotal.Text = $"{T("Invoice.Total")}: {FormatAmount(doc.GrandTotal)} {doc.CurrencyCode}";
        _lblStatus.Text = $"{T("Invoice.Status")}: {(doc.Status == InvDocStatus.Posted ? T("Invoice.StatusPosted") : T("Invoice.StatusDraft"))}";
        _lblTotal.FontWeight = FontWeights.Bold;
        _lblTotal.SetResourceReference(ForegroundProperty, "AccentBrush");
    }

    private static decimal ParseDec(string? text, decimal fallback)
        => decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v
            : decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out v) ? v : fallback;

    private static string FormatAmount(decimal value)
        => value.ToString("N2", CultureInfo.InvariantCulture);

    private sealed class LineVm : INotifyPropertyChanged
    {
        private string _materialCode = "", _description = "", _unit = "";
        private decimal _qty = 1, _price, _disc, _taxRate = 15;
        public long? MaterialId { get; set; }
        public string MaterialCode { get => _materialCode; set { _materialCode = value; OnChanged(); } }
        public string Description { get => _description; set { _description = value; OnChanged(); } }
        public string Unit { get => _unit; set { _unit = value; OnChanged(); } }
        public decimal Qty { get => _qty; set { _qty = value; OnChanged(nameof(QtyText)); } }
        public decimal UnitPrice { get => _price; set { _price = value; OnChanged(nameof(PriceText)); } }
        public decimal DiscountPct { get => _disc; set { _disc = value; OnChanged(nameof(DiscText)); } }
        public decimal TaxRate { get => _taxRate; set { _taxRate = value; OnChanged(nameof(TaxText)); } }
        public string QtyText { get => _qty.ToString("0.###", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) Qty = v; } }
        public string PriceText { get => _price.ToString("0.####", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) UnitPrice = v; } }
        public string DiscText { get => _disc.ToString("0.##", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) DiscountPct = v; } }
        public string TaxText { get => _taxRate.ToString("0.##", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) TaxRate = v; } }
        public string LineTotalText => ToLine().LineTotal.ToString("N2", CultureInfo.InvariantCulture);
        public InvLine ToLine() => new()
        {
            MaterialId = MaterialId,
            MaterialCode = MaterialCode,
            Description = string.IsNullOrWhiteSpace(Description) ? MaterialCode : Description,
            Unit = Unit, Qty = Qty, UnitPrice = UnitPrice, DiscountPct = DiscountPct, TaxRate = TaxRate
        };
        public static LineVm From(InvLine l) => new()
        {
            MaterialId = l.MaterialId, MaterialCode = l.MaterialCode, Description = l.Description,
            Unit = l.Unit, Qty = l.Qty, UnitPrice = l.UnitPrice, DiscountPct = l.DiscountPct, TaxRate = l.TaxRate
        };
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? n = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText)));
        }
    }
}
