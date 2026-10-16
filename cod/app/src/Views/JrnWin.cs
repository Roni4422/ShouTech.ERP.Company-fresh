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

namespace ShouTech.App.Views;

/// <summary>
/// Centralized styling, live balancing badge, and two-tone alternating row DataGrid (#FFFFFF / #E4F4F8).
/// </summary>
public sealed class JrnWin : BaseWin
{
    /// <summary>Logs unavailable operations instead of reporting a false success.</summary>
    private void ShowOperationUnavailable(string operation)
    {
        const string errorCode = "APP-2011";
        var context = $"Operation={operation} is not implemented in window {GetType().Name}.";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var exception = new NotSupportedException(context);

        ShouTech.App.Services.AppHost.Logging?.LogError(
            "WindowOperation",
            $"[{definition.Code}] {definition.Name} | Window={GetType().Name} | {context}",
            exception);
        ShouTech.App.Services.AppHost.Logging?.LogOperation(
            "WindowOperation",
            "OperationUnavailable",
            $"Code={definition.Code} | Window={GetType().Name} | {context}");

        MessageBox.Show(
            this,
            $"[{definition.Code}] {definition.Message}\n{context}",
            "العملية غير متاحة",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>Prevents false success until journal persistence is implemented.</summary>
    private void ShowPersistenceUnavailable()
    {
        const string errorCode = "APP-2010";
        const string context = "This window has no database persistence implementation.";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var exception = new NotSupportedException(context);

        ShouTech.App.Services.AppHost.Logging?.LogError(
            "WindowPersistence",
            $"[{definition.Code}] {definition.Name} | Window={GetType().Name} | {context}",
            exception);
        ShouTech.App.Services.AppHost.Logging?.LogOperation(
            "WindowPersistence",
            "PersistenceUnavailable",
            $"Code={definition.Code} | Window={GetType().Name}");

        MessageBox.Show(
            this,
            $"[{definition.Code}] {definition.Message}\n{context}\nلم يتم حفظ أي بيانات.",
            "العملية غير متاحة",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private readonly TextBox _txtDocNo = new() { Width = 110, Height = 26 };
    private readonly DatePicker _dtDate = new() { Width = 120, Height = 26, SelectedDate = DateTime.Today };
    private readonly TextBox _txtRefNo = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtCur = new() { Width = 70, Height = 26, Text = "SAR" };
    private readonly TextBox _txtRate = new() { Width = 70, Height = 26, Text = "1.00" };
    private readonly TextBox _txtCc = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtMemo = new() { Width = 400, Height = 26 };

    private readonly Button _btnNew = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnPost = new();
    private readonly Button _btnPrint = new();
    private readonly Button _btnAddLine = new();
    private readonly Button _btnRemoveLine = new();
    private readonly Button _btnAutoBal = new();

    private readonly TextBlock _lblTotDr = new();
    private readonly TextBlock _lblTotCr = new();
    private readonly Border _badgeDiff = new();
    private readonly TextBlock _lblDiff = new();

    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<JrnLineVm> _lines = new();

    public JrnWin()
    {
        Width = 920;
        Height = 580;
        MinWidth = 800;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        ResetVoucher();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(10) };

        // 1. Toolbar
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        StyleButton(_btnNew, "Common.New");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnPost, "Vouchers.Post");
        StyleButton(_btnPrint, "Invoice.Print");
        StyleButton(_btnAddLine, "Invoice.AddLine");
        StyleButton(_btnRemoveLine, "Invoice.RemoveLine");
        StyleButton(_btnAutoBal, "Vouchers.AutoBalance");

        _btnNew.Click += (_, _) => ResetVoucher();
        _btnSave.Click += (_, _) => SaveVoucher();
        _btnPost.Click += (_, _) => PostVoucher();
        _btnPrint.Click += (_, _) => ShowOperationUnavailable("Print");
        _btnAddLine.Click += (_, _) =>
        {
            var line = new JrnLineVm { LineNo = _lines.Count + 1 };
            _lines.Add(line);
            _grid.SelectedItem = line;
            _grid.ScrollIntoView(line);
        };
        _btnRemoveLine.Click += (_, _) =>
        {
            if (_grid.SelectedItem is JrnLineVm selected && _lines.Count > 1)
            {
                _lines.Remove(selected);
                RenumberLines();
                RefreshBalances();
            }
        };
        _btnAutoBal.Click += (_, _) => AutoBalance();

        foreach (var b in new[] { _btnNew, _btnSave, _btnPost, _btnPrint, _btnAddLine, _btnRemoveLine, _btnAutoBal })
            toolbar.Children.Add(b);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // 2. Footer summary with Live Difference Badge
        var footer = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        footer.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        footer.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var footerContent = new DockPanel();
        var leftSummary = new StackPanel { Orientation = Orientation.Horizontal };
        leftSummary.Children.Add(_lblTotDr);
        leftSummary.Children.Add(new TextBlock { Width = 20 });
        leftSummary.Children.Add(_lblTotCr);
        DockPanel.SetDock(leftSummary, Dock.Left);
        footerContent.Children.Add(leftSummary);

        _badgeDiff.CornerRadius = new CornerRadius(4);
        _badgeDiff.Padding = new Thickness(10, 3, 10, 3);
        _badgeDiff.Child = _lblDiff;
        _lblDiff.FontWeight = FontWeights.SemiBold;
        _lblDiff.Foreground = Brushes.White;
        DockPanel.SetDock(_badgeDiff, Dock.Right);
        footerContent.Children.Add(_badgeDiff);

        footer.Child = footerContent;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // 3. Center Body
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        headerPanel.Children.Add(Labeled("Vouchers.VoucherNo", _txtDocNo));
        headerPanel.Children.Add(Labeled("Vouchers.Date", _dtDate));
        headerPanel.Children.Add(Labeled("Vouchers.RefNo", _txtRefNo));
        headerPanel.Children.Add(Labeled("Vouchers.Currency", _txtCur));
        headerPanel.Children.Add(Labeled("Vouchers.ExchangeRate", _txtRate));
        headerPanel.Children.Add(Labeled("Vouchers.CostCenter", _txtCc));
        headerPanel.Children.Add(Labeled("Vouchers.Memo", _txtMemo));

        Grid.SetRow(headerPanel, 0);
        body.Children.Add(headerPanel);

        // Setup DataGrid
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
        customHeaderStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource("TableHeaderTransferBrush")));
        customHeaderStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = customHeaderStyle;

        _grid.ItemsSource = _lines;
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.LineNo), 45, readOnly: true));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.AccountCode), 110));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.AccountName), 220));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.DebitText), 100));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.CreditText), 100));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.CostCenter), 110));
        _grid.Columns.Add(TextCol(nameof(JrnLineVm.Memo), 200));

        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(RefreshBalances);

        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);

        root.Children.Add(body);
        Content = root;
    }

    private void RenumberLines()
    {
        for (var i = 0; i < _lines.Count; i++)
            _lines[i].LineNo = i + 1;
    }

    private void ResetVoucher()
    {
        _txtDocNo.Text = "JV-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        _dtDate.SelectedDate = DateTime.Today;
        _txtRefNo.Text = string.Empty;
        _txtCc.Text = string.Empty;
        _txtMemo.Text = string.Empty;
        _lines.Clear();
        _lines.Add(new JrnLineVm { LineNo = 1 });
        _lines.Add(new JrnLineVm { LineNo = 2 });
        _btnSave.IsEnabled = true;
        _btnPost.IsEnabled = true;
        _grid.IsReadOnly = false;
        RefreshBalances();
    }

    private void RefreshBalances()
    {
        var totalDebit = _lines.Sum(l => l.Debit);
        var totalCredit = _lines.Sum(l => l.Credit);
        var diff = totalDebit - totalCredit;

        _lblTotDr.Text = $"{T("Vouchers.TotalDebit")}: {totalDebit:N2} {_txtCur.Text}";
        _lblTotCr.Text = $"{T("Vouchers.TotalCredit")}: {totalCredit:N2} {_txtCur.Text}";

        if (Math.Abs(diff) < 0.001m && (totalDebit > 0 || totalCredit > 0))
        {
            _badgeDiff.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50));
            _lblDiff.Text = $"{T("Vouchers.Balanced")} (0.00)";
        }
        else
        {
            _badgeDiff.Background = new SolidColorBrush(Color.FromRgb(198, 40, 40));
            _lblDiff.Text = $"{T("Vouchers.Difference")}: {Math.Abs(diff):N2} {_txtCur.Text}";
        }
    }

    private void AutoBalance()
    {
        var totalDebit = _lines.Sum(l => l.Debit);
        var totalCredit = _lines.Sum(l => l.Credit);
        var diff = totalDebit - totalCredit;

        if (Math.Abs(diff) < 0.001m) return;

        var lastLine = _lines.LastOrDefault(l => l.Debit == 0 && l.Credit == 0);
        if (lastLine == null)
        {
            lastLine = new JrnLineVm { LineNo = _lines.Count + 1 };
            _lines.Add(lastLine);
        }

        if (diff > 0)
        {
            lastLine.Credit = diff;
            lastLine.Debit = 0;
        }
        else
        {
            lastLine.Debit = Math.Abs(diff);
            lastLine.Credit = 0;
        }

        RefreshBalances();
    }

    private void SaveVoucher()
    {
        ShowPersistenceUnavailable();
    }

    private void PostVoucher()
    {
        ShowPersistenceUnavailable();
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("Vouchers.JournalTitle");
        if (_grid.Columns.Count >= 7)
        {
            _grid.Columns[0].Header = "#";
            _grid.Columns[1].Header = T("Vouchers.AccountCode");
            _grid.Columns[2].Header = T("Vouchers.AccountName");
            _grid.Columns[3].Header = T("Vouchers.Debit");
            _grid.Columns[4].Header = T("Vouchers.Credit");
            _grid.Columns[5].Header = T("Vouchers.CostCenter");
            _grid.Columns[6].Header = T("Vouchers.Memo");
        }
        RefreshLocalizedContent(Content as DependencyObject);
        RefreshBalances();
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

    private static DataGridTextColumn TextCol(string path, double width, bool readOnly = false)
        => new()
        {
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width,
            IsReadOnly = readOnly
        };

    private sealed class JrnLineVm : INotifyPropertyChanged
    {
        private int _lineNo;
        private string _accCode = "", _accName = "", _costCenter = "", _memo = "";
        private decimal _debit, _credit;

        public int LineNo { get => _lineNo; set { _lineNo = value; OnChanged(); } }
        public string AccountCode { get => _accCode; set { _accCode = value; OnChanged(); } }
        public string AccountName { get => _accName; set { _accName = value; OnChanged(); } }
        public string CostCenter { get => _costCenter; set { _costCenter = value; OnChanged(); } }
        public string Memo { get => _memo; set { _memo = value; OnChanged(); } }

        public decimal Debit { get => _debit; set { _debit = value; OnChanged(nameof(DebitText)); } }
        public decimal Credit { get => _credit; set { _credit = value; OnChanged(nameof(CreditText)); } }

        public string DebitText
        {
            get => _debit == 0 ? "" : _debit.ToString("0.##", CultureInfo.InvariantCulture);
            set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) Debit = v; else Debit = 0; }
        }

        public string CreditText
        {
            get => _credit == 0 ? "" : _credit.ToString("0.##", CultureInfo.InvariantCulture);
            set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) Credit = v; else Credit = 0; }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
