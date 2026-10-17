using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ShouTech.App;
using ShouTech.App.Localization;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

/// <summary>
/// Company-scoped cheque batch editor. Saving commits the header and all current detail rows
/// atomically; replaced detail versions remain stored for audit. Posted batches are immutable.
/// </summary>
public sealed class ChqWin : BaseWin
{
    private bool _busy;
    private Guid? _currentBatchId;

    private readonly TextBox _txtDocNo = new() { Width = 130, Height = 26 };
    private readonly DatePicker _dtDate = new() { Width = 120, Height = 26, SelectedDate = DateTime.Today };
    private readonly TextBox _txtBox = new() { Width = 160, Height = 26 };
    private readonly TextBox _txtCur = new() { Width = 70, Height = 26 };
    private readonly TextBox _txtMemo = new() { Width = 340, Height = 26 };

    private readonly Button _btnNew = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnAddChq = new();
    private readonly Button _btnRemChq = new();
    private readonly Button _btnPrint = new();

    private readonly TextBlock _lblTotCount = new();
    private readonly TextBlock _lblTotAmount = new();

    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<ChqVm> _cheques = new();

    public ChqWin()
    {
        Width = 980;
        Height = 590;
        MinWidth = 840;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        ResetBatch();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(10) };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };

        StyleButton(_btnNew, "Common.New");
        StyleButton(_btnLoad, "Vouchers.LoadChequeBatch");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnDelete, "Common.Delete");
        StyleButton(_btnAddChq, "Vouchers.AddChequeLine");
        StyleButton(_btnRemChq, "Invoice.RemoveLine");
        StyleButton(_btnPrint, "Invoice.Print");

        _btnNew.Click += (_, _) => ResetBatch();
        _btnLoad.Click += async (_, _) => await LoadBatchAsync().ConfigureAwait(true);
        _btnSave.Click += async (_, _) => await SaveBatchAsync().ConfigureAwait(true);
        _btnDelete.Click += async (_, _) => await DeleteBatchAsync().ConfigureAwait(true);
        _btnPrint.Click += (_, _) => ShowOperationUnavailable("Print");
        _btnAddChq.Click += (_, _) => AddLine();
        _btnRemChq.Click += (_, _) => RemoveSelectedLine();
        _txtDocNo.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                await LoadBatchAsync().ConfigureAwait(true);
            }
        };

        foreach (var button in new[] { _btnNew, _btnLoad, _btnSave, _btnDelete, _btnAddChq, _btnRemChq, _btnPrint })
            toolbar.Children.Add(button);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

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
        DockPanel.SetDock(_lblTotCount, Dock.Left);
        DockPanel.SetDock(_lblTotAmount, Dock.Right);
        footerContent.Children.Add(_lblTotCount);
        footerContent.Children.Add(_lblTotAmount);
        footer.Child = footerContent;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        headerPanel.Children.Add(Labeled("Vouchers.VoucherNo", _txtDocNo));
        headerPanel.Children.Add(Labeled("Vouchers.Date", _dtDate));
        headerPanel.Children.Add(Labeled("Vouchers.Treasury", _txtBox));
        headerPanel.Children.Add(Labeled("Vouchers.Currency", _txtCur));
        headerPanel.Children.Add(Labeled("Vouchers.Memo", _txtMemo));
        Grid.SetRow(headerPanel, 0);
        body.Children.Add(headerPanel);

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
        customHeaderStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource("TableHeaderQuotationBrush")));
        customHeaderStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = customHeaderStyle;

        _grid.ItemsSource = _cheques;
        _grid.Columns.Add(TextCol(nameof(ChqVm.LineNo), 45, readOnly: true));
        _grid.Columns.Add(TextCol(nameof(ChqVm.ChequeNo), 110));
        _grid.Columns.Add(TextCol(nameof(ChqVm.BankName), 145));
        _grid.Columns.Add(TextCol(nameof(ChqVm.Branch), 105));
        _grid.Columns.Add(TextCol(nameof(ChqVm.DueDateText), 110));
        _grid.Columns.Add(TextCol(nameof(ChqVm.AmountText), 110));
        _grid.Columns.Add(TextCol(nameof(ChqVm.Drawer), 150));
        _grid.Columns.Add(TextCol(nameof(ChqVm.AccountCode), 110));
        _grid.Columns.Add(TextCol(nameof(ChqVm.Memo), 170));
        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(RefreshSummary);

        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);
        root.Children.Add(body);
        Content = root;
    }

    private void AddLine()
    {
        var line = new ChqVm { LineNo = _cheques.Count + 1, DueDate = DateTime.Today.AddDays(30) };
        _cheques.Add(line);
        _grid.SelectedItem = line;
        _grid.ScrollIntoView(line);
        RefreshSummary();
    }

    private void RemoveSelectedLine()
    {
        if (_grid.SelectedItem is not ChqVm selected)
            return;
        _cheques.Remove(selected);
        Renumber();
        RefreshSummary();
    }

    private void Renumber()
    {
        for (var i = 0; i < _cheques.Count; i++)
            _cheques[i].LineNo = i + 1;
    }

    private void ResetBatch()
    {
        _currentBatchId = null;
        _txtDocNo.IsReadOnly = false;
        _btnDelete.IsEnabled = false;
        _txtDocNo.Text = "CHQ-" + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _dtDate.SelectedDate = DateTime.Today;
        _txtBox.Text = string.Empty;
        _txtCur.Text = string.Empty;
        _txtMemo.Text = string.Empty;
        _cheques.Clear();
        _cheques.Add(new ChqVm { LineNo = 1, DueDate = DateTime.Today.AddDays(30) });
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        var entered = _cheques.Where(line => !line.IsBlank).ToArray();
        var total = entered.Sum(line => line.Amount);
        _lblTotCount.Text = $"{T("Vouchers.ChequeNo")}: {entered.Length}";
        var totalText = entered.Length == 0 || entered.All(line => line.Amount <= 0)
            ? "—"
            : total.ToString("N2", CultureInfo.CurrentCulture);
        var currency = _txtCur.Text.Trim();
        _lblTotAmount.Text = $"{T("Invoice.Total")}: {totalText}{(string.IsNullOrEmpty(currency) ? string.Empty : " " + currency)}";
        _lblTotAmount.FontWeight = FontWeights.Bold;
    }

    private async Task LoadBatchAsync()
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_txtDocNo.Text))
        {
            ReportBatchFailure("Load", new ChequeBatchException(
                ErrorCodes.ChequeValidationFailed, "Enter a batch code first."));
            return;
        }

        SetBusy(true);
        try
        {
            var batch = await ChequeBatchSvc.LoadAsync(_txtDocNo.Text.Trim()).ConfigureAwait(true);
            if (batch is null)
                throw new ChequeBatchException(ErrorCodes.ChequeNotFound,
                    "No active cheque batch was found for this code in the selected company.");

            PopulateBatch(batch);
            AppHost.Logging?.LogOperation("ChequeBatch", "Load",
                $"Company={AppState.CurrentCompanyCode}; Code={batch.Code}; BatchId={batch.BatchId:D}; LineCount={batch.Lines.Count}; Outcome=Loaded");
            MessageBox.Show(this, T("Vouchers.ChequeBatchLoaded"), Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportBatchFailure("Load", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveBatchAsync()
    {
        if (_busy) return;
        CommitGridEdits();
        SetBusy(true);
        try
        {
            var batch = ReadBatch();
            var batchId = await ChequeBatchSvc.SaveAsync(batch).ConfigureAwait(true);
            _currentBatchId = batchId;
            _txtDocNo.IsReadOnly = true;
            _btnDelete.IsEnabled = true;

            // Read the committed rows back before reporting success.
            var saved = await ChequeBatchSvc.LoadAsync(batch.Code).ConfigureAwait(true)
                ?? throw new ChequeBatchException(ErrorCodes.ChequeSaveFailed,
                    "The batch was written but could not be reloaded to verify the committed values.");
            PopulateBatch(saved);
            MessageBox.Show(this, T("Vouchers.ChequeBatchSaved"), Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportBatchFailure("Save", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeleteBatchAsync()
    {
        if (_busy) return;
        if (!_currentBatchId.HasValue)
        {
            ReportBatchFailure("Delete", new ChequeBatchException(
                ErrorCodes.ChequeValidationFailed, "Load or save a cheque batch before deleting."));
            return;
        }

        var confirmation = MessageBox.Show(this, T("Vouchers.ConfirmChequeBatchDelete"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        SetBusy(true);
        try
        {
            if (!await ChequeBatchSvc.DeleteAsync(_txtDocNo.Text.Trim()).ConfigureAwait(true))
                throw new ChequeBatchException(ErrorCodes.ChequeDeleteNotFound,
                    "The batch was not found in the active company, was already deleted, or is not a draft.");

            AppHost.Logging?.LogOperation("ChequeBatch", "SoftDelete",
                $"Company={AppState.CurrentCompanyCode}; Code={_txtDocNo.Text.Trim()}; Outcome=Committed");
            ResetBatch();
            MessageBox.Show(this, T("Vouchers.ChequeBatchDeleted"), Title,
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportBatchFailure("Delete", ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private ChequeBatchRecord ReadBatch()
    {
        if (_dtDate.SelectedDate is not DateTime batchDate)
            throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed, "A batch date is required.");

        var lines = _cheques.Where(line => !line.IsBlank)
            .Select(line => line.ToRecord())
            .ToArray();
        return new ChequeBatchRecord
        {
            BatchId = _currentBatchId ?? Guid.Empty,
            Code = _txtDocNo.Text.Trim(),
            BatchDate = batchDate.Date,
            CashBoxCode = _txtBox.Text,
            CurrencyCode = _txtCur.Text,
            Memo = _txtMemo.Text,
            Lines = lines
        };
    }

    private void PopulateBatch(ChequeBatchRecord batch)
    {
        _currentBatchId = batch.BatchId;
        _txtDocNo.Text = batch.Code;
        _txtDocNo.IsReadOnly = true;
        _dtDate.SelectedDate = batch.BatchDate;
        _txtBox.Text = batch.CashBoxCode;
        _txtCur.Text = batch.CurrencyCode;
        _txtMemo.Text = batch.Memo;
        _cheques.Clear();
        foreach (var line in batch.Lines)
        {
            _cheques.Add(new ChqVm
            {
                LineNo = line.LineNumber,
                ChequeNo = line.ChequeNumber,
                BankName = line.BankName,
                Branch = line.Branch,
                DueDate = line.DueDate,
                Amount = line.Amount,
                Drawer = line.Drawer,
                AccountCode = line.AccountCode,
                Memo = line.Memo
            });
        }
        Renumber();
        _btnDelete.IsEnabled = true;
        RefreshSummary();
    }

    private void CommitGridEdits()
    {
        _grid.CommitEdit(DataGridEditingUnit.Cell, true);
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        _btnNew.IsEnabled = !value;
        _btnLoad.IsEnabled = !value;
        _btnSave.IsEnabled = !value;
        _btnDelete.IsEnabled = !value && _currentBatchId.HasValue;
        _btnAddChq.IsEnabled = !value;
        _btnRemChq.IsEnabled = !value;
        _btnPrint.IsEnabled = !value;
    }

    private void ReportBatchFailure(string operation, Exception exception)
    {
        var fallback = operation switch
        {
            "Load" => ErrorCodes.ChequeLoadFailed,
            "Delete" => ErrorCodes.ChequeDeleteFailed,
            _ => ErrorCodes.ChequeSaveFailed
        };
        var code = exception is ChequeBatchException batchException ? batchException.ErrorCode : fallback;
        var definition = ErrorCatalog.Resolve(code);
        var context = $"Operation={operation}; Window={GetType().Name}; Company={AppState.CurrentCompanyCode ?? "(none)"}; Batch={_txtDocNo.Text.Trim()}";
        AppHost.Logging?.LogError("ChequeBatch",
            $"[{definition.Code}] {definition.Name}; {context}; Error={exception.Message}", exception);
        AppHost.Logging?.LogOperation("ChequeBatch", "DatabaseOperationFailed",
            $"Code={definition.Code}; {context}");
        MessageBox.Show(this,
            $"[{definition.Code}] {definition.Message}\nLocation: {definition.Location}\nReview the error log before retrying; some changes may already have been committed.",
            Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowOperationUnavailable(string operation)
    {
        const string errorCode = "CHQ-OPS-001";
        var context = $"Operation={operation} is not implemented in window {GetType().Name}.";
        var definition = ErrorCatalog.Resolve(errorCode);
        var exception = new NotSupportedException(context);
        AppHost.Logging?.LogError("WindowOperation",
            $"[{definition.Code}] {definition.Name}; Window={GetType().Name}; {context}", exception);
        AppHost.Logging?.LogOperation("WindowOperation", "OperationUnavailable",
            $"Code={definition.Code}; Window={GetType().Name}; {context}");
        MessageBox.Show(this, $"[{definition.Code}] {definition.Message}\n{context}", Title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("Vouchers.ChequeTitle");
        if (_grid.Columns.Count >= 9)
        {
            _grid.Columns[0].Header = "#";
            _grid.Columns[1].Header = T("Vouchers.ChequeNo");
            _grid.Columns[2].Header = T("Vouchers.Bank");
            _grid.Columns[3].Header = T("BankCard.Branch");
            _grid.Columns[4].Header = T("Vouchers.DueDate");
            _grid.Columns[5].Header = T("MfgOrder.Amount");
            _grid.Columns[6].Header = T("Vouchers.Drawer");
            _grid.Columns[7].Header = T("Vouchers.AccountCode");
            _grid.Columns[8].Header = T("Vouchers.Memo");
        }
        RefreshLocalizedContent(Content as DependencyObject);
        RefreshSummary();
    }

    private static void RefreshLocalizedContent(DependencyObject? parent)
    {
        if (parent is null) return;
        if (parent is Button button && button.Tag is string key)
            button.Content = T(key);
        if (parent is TextBlock textBlock && textBlock.Tag is string textKey)
            textBlock.Text = T(textKey);
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            RefreshLocalizedContent(VisualTreeHelper.GetChild(parent, index));
    }

    private static StackPanel Labeled(string key, UIElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 3) };
        panel.Children.Add(new TextBlock { Text = T(key), Tag = key, Margin = new Thickness(0, 0, 0, 1), Opacity = 0.85 });
        if (control is TextBox textBox)
            textBox.SetResourceReference(StyleProperty, "VisuTextBoxStyle");
        panel.Children.Add(control);
        return panel;
    }

    private static void StyleButton(Button button, string key)
    {
        button.Content = T(key);
        button.Margin = new Thickness(0, 0, 5, 0);
        button.Padding = new Thickness(9, 3, 9, 3);
        button.MinWidth = 76;
        button.Height = 28;
        button.Tag = key;
        button.SetResourceReference(StyleProperty, "VisuButtonStyle");
    }

    private static DataGridTextColumn TextCol(string path, double width, bool readOnly = false)
        => new()
        {
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width,
            IsReadOnly = readOnly
        };

    private sealed class ChqVm : INotifyPropertyChanged
    {
        private int _lineNo;
        private string _chequeNo = string.Empty;
        private string _bankName = string.Empty;
        private string _branch = string.Empty;
        private string _drawer = string.Empty;
        private string _accountCode = string.Empty;
        private string _memo = string.Empty;
        private DateTime _dueDate = DateTime.Today;
        private decimal _amount;
        private bool _dueDateValid = true;
        private bool _amountValid = true;

        public int LineNo { get => _lineNo; set { _lineNo = value; OnChanged(); } }
        public string ChequeNo { get => _chequeNo; set { _chequeNo = value ?? string.Empty; OnChanged(); } }
        public string BankName { get => _bankName; set { _bankName = value ?? string.Empty; OnChanged(); } }
        public string Branch { get => _branch; set { _branch = value ?? string.Empty; OnChanged(); } }
        public string Drawer { get => _drawer; set { _drawer = value ?? string.Empty; OnChanged(); } }
        public string AccountCode { get => _accountCode; set { _accountCode = value ?? string.Empty; OnChanged(); } }
        public string Memo { get => _memo; set { _memo = value ?? string.Empty; OnChanged(); } }

        public DateTime DueDate
        {
            get => _dueDate;
            set { _dueDate = value.Date; _dueDateValid = value != DateTime.MinValue; OnChanged(nameof(DueDateText)); }
        }

        public string DueDateText
        {
            get => _dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            set
            {
                if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date)
                    || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                {
                    _dueDate = date.Date;
                    _dueDateValid = true;
                }
                else
                {
                    _dueDateValid = false;
                }
                OnChanged();
            }
        }

        public decimal Amount
        {
            get => _amount;
            set { _amount = value; _amountValid = true; OnChanged(nameof(AmountText)); }
        }

        public string AmountText
        {
            get => _amount == 0 ? string.Empty : _amount.ToString("0.####", CultureInfo.InvariantCulture);
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _amount = 0;
                    _amountValid = true;
                }
                else if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
                    || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
                {
                    _amount = amount;
                    _amountValid = true;
                }
                else
                {
                    _amountValid = false;
                }
                OnChanged();
            }
        }

        public bool IsBlank =>
            string.IsNullOrWhiteSpace(ChequeNo) &&
            string.IsNullOrWhiteSpace(BankName) &&
            string.IsNullOrWhiteSpace(Branch) &&
            string.IsNullOrWhiteSpace(Drawer) &&
            string.IsNullOrWhiteSpace(AccountCode) &&
            string.IsNullOrWhiteSpace(Memo) &&
            Amount == 0;

        public ChequeBatchLineRecord ToRecord()
        {
            if (!_dueDateValid)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed,
                    $"Enter a valid due date on cheque line {LineNo}.");
            if (!_amountValid)
                throw new ChequeBatchException(ErrorCodes.ChequeValidationFailed,
                    $"Enter a valid amount on cheque line {LineNo}.");
            return new ChequeBatchLineRecord
            {
                LineNumber = LineNo,
                ChequeNumber = ChequeNo,
                BankName = BankName,
                Branch = Branch,
                DueDate = DueDate,
                Amount = Amount,
                Drawer = Drawer,
                AccountCode = AccountCode,
                Memo = Memo
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
