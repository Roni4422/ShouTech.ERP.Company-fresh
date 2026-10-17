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
using ShouTech.Domain.Entities;

namespace ShouTech.App.Views;

public enum CshKind
{
    Receipt,
    Payment
}

/// <summary>
/// Supports Receipt & Payment modes with split allocations and alternating zebra table.
/// </summary>
public sealed class CshWin : BaseWin
{
    /// <summary>Logs unavailable operations instead of reporting a false success.</summary>
    private void ShowOperationUnavailable(string operation)
    {
        const string errorCode = "CSH-OPS-001";
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


    private readonly CshKind _kind;

    private readonly TextBox _txtDocNo = new() { Width = 110, Height = 26 };
    private readonly DatePicker _dtDate = new() { Width = 120, Height = 26, SelectedDate = DateTime.Today };
    private readonly TextBox _txtBox = new() { Width = 150, Height = 26 };
    private readonly TextBox _txtParty = new() { Width = 220, Height = 26 };
    private readonly TextBox _txtAmount = new() { Width = 110, Height = 26, Text = "0.00" };
    private readonly TextBox _txtCur = new() { Width = 70, Height = 26, Text = "SAR" };
    private readonly TextBox _txtRate = new() { Width = 70, Height = 26, Text = "1.00" };
    private readonly TextBox _txtRefNo = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtCc = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtMemo = new() { Width = 400, Height = 26 };

    private readonly Button _btnNew = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnPost = new();
    private readonly Button _btnReverse = new();
    private readonly Button _btnPrint = new();
    private readonly Button _btnAddLine = new();
    private readonly Button _btnRemoveLine = new();

    private readonly TextBlock _lblTotCalc = new();
    private readonly TextBlock _lblStatus = new();

    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<CshLineVm> _lines = new();
    private Guid? _currentVoucherId;
    private Guid? _currentJournalId;
    private Guid? _currentReversalJournalId;
    private CashVoucherStatus _currentStatus = CashVoucherStatus.Draft;

    public CshWin(CshKind kind = CshKind.Receipt)
    {
        _kind = kind;
        Width = 880;
        Height = 560;
        MinWidth = 760;
        MinHeight = 440;
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
        StyleButton(_btnLoad, "Vouchers.Load");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnPost, "Vouchers.Post");
        StyleButton(_btnReverse, "Vouchers.ReverseVoucher");
        StyleButton(_btnPrint, "Invoice.Print");
        StyleButton(_btnAddLine, "Invoice.AddLine");
        StyleButton(_btnRemoveLine, "Invoice.RemoveLine");

        _btnNew.Click += (_, _) => ResetVoucher();
        _btnLoad.Click += async (_, _) => await LoadVoucherAsync().ConfigureAwait(true);
        _btnSave.Click += async (_, _) => await SaveVoucherAsync().ConfigureAwait(true);
        _btnPost.Click += async (_, _) => await PostVoucherAsync().ConfigureAwait(true);
        _btnReverse.Click += async (_, _) => await ReverseVoucherAsync().ConfigureAwait(true);
        _btnPrint.Click += (_, _) => ShowOperationUnavailable("Print");
        _btnAddLine.Click += (_, _) =>
        {
            var l = new CshLineVm { LineNo = _lines.Count + 1 };
            _lines.Add(l);
            _grid.SelectedItem = l;
            _grid.ScrollIntoView(l);
        };
        _btnRemoveLine.Click += (_, _) =>
        {
            if (_grid.SelectedItem is CshLineVm selected && _lines.Count > 1)
            {
                _lines.Remove(selected);
                RenumberLines();
                RefreshCalculations();
            }
        };

        foreach (var b in new[] { _btnNew, _btnLoad, _btnSave, _btnPost, _btnReverse, _btnPrint, _btnAddLine, _btnRemoveLine })
            toolbar.Children.Add(b);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // 2. Footer
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
        DockPanel.SetDock(_lblTotCalc, Dock.Left);
        DockPanel.SetDock(_lblStatus, Dock.Right);
        footerContent.Children.Add(_lblTotCalc);
        footerContent.Children.Add(_lblStatus);
        footer.Child = footerContent;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // 3. Body
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        headerPanel.Children.Add(Labeled("Vouchers.VoucherNo", _txtDocNo));
        headerPanel.Children.Add(Labeled("Vouchers.Date", _dtDate));
        headerPanel.Children.Add(Labeled("Vouchers.TreasuryAccountCode", _txtBox));
        headerPanel.Children.Add(Labeled(_kind == CshKind.Receipt ? "Vouchers.ReceivedFrom" : "Vouchers.PaidTo", _txtParty));
        headerPanel.Children.Add(Labeled("Invoice.Total", _txtAmount));
        headerPanel.Children.Add(Labeled("Vouchers.Currency", _txtCur));
        headerPanel.Children.Add(Labeled("Vouchers.ExchangeRate", _txtRate));
        headerPanel.Children.Add(Labeled("Vouchers.RefNo", _txtRefNo));
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

        var headerBrushKey = _kind == CshKind.Receipt ? "TableHeaderStockInBrush" : "TableHeaderStockOutBrush";
        var customHeaderStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
            (Style)FindResource("VisuDataGridHeaderStyle"));
        customHeaderStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource(headerBrushKey)));
        customHeaderStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = customHeaderStyle;

        _grid.ItemsSource = _lines;
        _grid.Columns.Add(TextCol(nameof(CshLineVm.LineNo), 45, readOnly: true));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.AccountCode), 120));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.AccountName), 220));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.AmountText), 110));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.CostCenter), 110));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.InvoiceRef), 100));
        _grid.Columns.Add(TextCol(nameof(CshLineVm.Memo), 200));

        _grid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(RefreshCalculations);
        _txtAmount.TextChanged += (_, _) => RefreshCalculations();

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
        _currentVoucherId = null;
        _currentJournalId = null;
        _currentReversalJournalId = null;
        _currentStatus = CashVoucherStatus.Draft;

        var prefix = _kind == CshKind.Receipt ? "RCV-" : "PAY-";
        _txtDocNo.Text = prefix + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        _dtDate.SelectedDate = DateTime.Today;
        _txtBox.Text = string.Empty;
        _txtParty.Text = string.Empty;
        _txtAmount.Text = "0.00";
        _txtCur.Text = "SAR";
        _txtRate.Text = "1.00";
        _txtRefNo.Text = string.Empty;
        _txtCc.Text = string.Empty;
        _txtMemo.Text = string.Empty;
        _lines.Clear();
        _lines.Add(new CshLineVm { LineNo = 1 });

        _btnLoad.IsEnabled = true;
        _btnSave.IsEnabled = true;
        _btnPost.IsEnabled = true;
        _btnReverse.IsEnabled = false;
        SetVoucherReadOnly(false);
        RefreshCalculations();
    }

    private void RefreshCalculations()
    {
        var sum = _lines.Sum(line => line.Amount);
        if (!TryParseDecimalInput(_txtAmount.Text, out var headerTotal) || !CshLineVm.IsValidAmount(headerTotal))
        {
            _lblTotCalc.Text = $"{T("Invoice.Total")}: {sum:N4} {_txtCur.Text}";
            _lblStatus.Text = T("Vouchers.InvalidAmount");
            return;
        }

        if (sum == 0m && headerTotal > 0m && _lines.Count > 0 &&
            _lines.All(line => line.Amount == 0m && line.AmountTextIsValid))
        {
            _lines[0].Amount = headerTotal;
            sum = headerTotal;
        }

        _lblTotCalc.Text = $"{T("Invoice.Total")}: {sum:N4} {_txtCur.Text}";
        if (_lines.Any(line => !line.AmountTextIsValid))
            _lblStatus.Text = T("Vouchers.InvalidAmount");
        else if (sum > 0m && headerTotal > 0m && sum != headerTotal)
            _lblStatus.Text = T("Vouchers.TotalMismatch");
        else
        {
            var statusText = _currentStatus switch
            {
                CashVoucherStatus.Posted => T("Invoice.StatusPosted"),
                CashVoucherStatus.Reversed => T("Vouchers.StatusReversed"),
                _ => T("Invoice.StatusDraft")
            };
            _lblStatus.Text = $"{T("Invoice.Status")}: {statusText}";
        }
    }

    private async Task SaveVoucherAsync()
    {
        if (_currentVoucherId.HasValue)
        {
            MessageBox.Show(this, "هذا السند محفوظ بالفعل. افتح سنداً جديداً لإدخال سند آخر.", "السند محفوظ",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            RefreshCalculations();

            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;
            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("لا توجد شركة نشطة في جلسة المستخدم.");
            if (string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("تعذر تحديد المستخدم الحالي لحفظ السند.");
            if (ShouTech.App.Services.AppHost.CashVouchers is not { } service)
                throw new InvalidOperationException("خدمة سندات القبض والصرف غير مهيأة؛ تحقق من اتصال قاعدة البيانات.");

            if (_dtDate.SelectedDate is not DateTime selectedDate)
                throw new InvalidOperationException("تاريخ السند مطلوب.");
            if (string.IsNullOrWhiteSpace(_txtDocNo.Text))
                throw new InvalidOperationException("رقم السند مطلوب.");
            if (string.IsNullOrWhiteSpace(_txtBox.Text))
                throw new InvalidOperationException("رمز حساب الصندوق أو البنك مطلوب.");
            if (!TryParseDecimalInput(_txtRate.Text, out var exchangeRate) ||
                exchangeRate <= 0m || exchangeRate > 99_999_999_999.99999999m ||
                decimal.Round(exchangeRate, 8) != exchangeRate)
                throw new InvalidOperationException("سعر الصرف يجب أن يكون موجباً وبحد أقصى ثماني منازل عشرية.");
            var currency = _txtCur.Text.Trim().ToUpperInvariant();
            if (currency.Length != 3 || !currency.All(char.IsAsciiLetter))
                throw new InvalidOperationException("رمز العملة يجب أن يتكون من ثلاثة أحرف لاتينية.");

            if (_lines.Any(line => !line.AmountTextIsValid))
                throw new InvalidOperationException("يوجد مبلغ غير صالح أو يتجاوز الدقة العشرية المسموحة.");

            var activeLines = _lines
                .Where(line => !string.IsNullOrWhiteSpace(line.AccountCode) ||
                               line.Amount != 0m ||
                               !string.IsNullOrWhiteSpace(line.CostCenter) ||
                               !string.IsNullOrWhiteSpace(line.InvoiceRef) ||
                               !string.IsNullOrWhiteSpace(line.Memo))
                .ToList();
            if (activeLines.Count == 0)
                throw new InvalidOperationException("يجب إدخال سطر توزيع واحد على الأقل.");

            var companyId = await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var treasuryAccountId = await service.ResolveAccountIdAsync(companyId, _txtBox.Text).ConfigureAwait(true);
            var voucherLines = new List<CashVoucherLine>();
            foreach (var line in activeLines)
            {
                if (string.IsNullOrWhiteSpace(line.AccountCode))
                    throw new InvalidOperationException($"رمز الحساب مطلوب في سطر التوزيع {line.LineNo}.");
                if (line.Amount <= 0m)
                    throw new InvalidOperationException($"مبلغ سطر التوزيع {line.LineNo} يجب أن يكون موجباً.");
                var accountId = await service.ResolveAccountIdAsync(companyId, line.AccountCode).ConfigureAwait(true);
                line.AccountName = await service.ResolveAccountNameAsync(companyId, line.AccountCode).ConfigureAwait(true);
                var costCenter = string.IsNullOrWhiteSpace(line.CostCenter) ? _txtCc.Text : line.CostCenter;
                voucherLines.Add(new CashVoucherLine(accountId, line.Amount, costCenter, line.InvoiceRef, line.Memo));
            }

            var amountSum = voucherLines.Sum(line => line.Amount);
            if (!TryParseDecimalInput(_txtAmount.Text, out var enteredTotal) || !CshLineVm.IsValidAmount(enteredTotal))
                throw new InvalidOperationException("إجمالي السند غير صالح.");
            var total = enteredTotal > 0m ? enteredTotal : amountSum;
            if (total != amountSum)
                throw new InvalidOperationException("إجمالي السند يجب أن يساوي مجموع مبالغ التوزيع.");
            var voucher = new CashVoucher(
                _kind == CshKind.Receipt ? CashVoucherKind.Receipt : CashVoucherKind.Payment,
                DateOnly.FromDateTime(selectedDate),
                _txtDocNo.Text,
                treasuryAccountId,
                _txtParty.Text,
                total,
                currency,
                exchangeRate,
                voucherLines,
                _txtRefNo.Text,
                _txtCc.Text,
                _txtMemo.Text);

            var id = await service.CreateDraftAsync(companyId, voucher, actor).ConfigureAwait(true);
            _currentVoucherId = id;
            _currentJournalId = null;
            _currentReversalJournalId = null;
            _currentStatus = CashVoucherStatus.Draft;
            _txtAmount.Text = total.ToString("0.####", CultureInfo.CurrentCulture);
            _btnSave.IsEnabled = false;
            _btnPost.IsEnabled = true;
            _btnReverse.IsEnabled = false;
            SetVoucherReadOnly(true);
            RefreshCalculations();
            MessageBox.Show(this, $"تم حفظ مسودة السند في قاعدة البيانات.\nالمعرف: {id:D}", "تم الحفظ",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("SaveDraft", ex);
        }
    }

    private async Task PostVoucherAsync()
    {
        if (_currentStatus != CashVoucherStatus.Draft)
            return;
        try
        {
            if (!_currentVoucherId.HasValue)
            {
                await SaveVoucherAsync().ConfigureAwait(true);
                if (!_currentVoucherId.HasValue)
                    return;
            }

            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;
            if (string.IsNullOrWhiteSpace(companyCode) || string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("تعذر تحديد الشركة أو المستخدم الحالي لترحيل السند.");
            if (ShouTech.App.Services.AppHost.CashVouchers is not { } service)
                throw new InvalidOperationException("خدمة سندات القبض والصرف غير مهيأة.");

            var companyId = await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var journalId = await service.PostAsync(companyId,
                _kind == CshKind.Receipt ? CashVoucherKind.Receipt : CashVoucherKind.Payment,
                _currentVoucherId.Value, actor).ConfigureAwait(true);
            _currentJournalId = journalId;
            _currentStatus = CashVoucherStatus.Posted;
            _btnSave.IsEnabled = false;
            _btnPost.IsEnabled = false;
            _btnReverse.IsEnabled = true;
            SetVoucherReadOnly(true);
            RefreshCalculations();
            MessageBox.Show(this, $"تم ترحيل السند والقيد العام في قاعدة البيانات.\nرقم القيد: {journalId:D}",
                "تم الترحيل", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("Post", ex);
        }
    }

    private async Task LoadVoucherAsync()
    {
        var number = _txtDocNo.Text.Trim();
        if (string.IsNullOrWhiteSpace(number))
        {
            MessageBox.Show(this, "أدخل رقم السند المراد فتحه.", "رقم السند مطلوب",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("لا توجد شركة نشطة في جلسة المستخدم.");
            if (ShouTech.App.Services.AppHost.CashVouchers is not { } service)
                throw new InvalidOperationException("خدمة سندات القبض والصرف غير مهيأة.");

            var companyId = await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var snapshot = await service.FindByDocumentNumberAsync(
                companyId,
                _kind == CshKind.Receipt ? CashVoucherKind.Receipt : CashVoucherKind.Payment,
                number).ConfigureAwait(true);
            if (snapshot is null)
                throw new InvalidOperationException($"لم يُعثر على السند '{number}' في الشركة النشطة.");

            _currentVoucherId = snapshot.Id;
            _currentJournalId = snapshot.JournalEntryId;
            _currentReversalJournalId = snapshot.ReversalJournalEntryId;
            _currentStatus = snapshot.Status;
            _txtDocNo.Text = snapshot.VoucherNumber;
            _dtDate.SelectedDate = snapshot.VoucherDate.ToDateTime(TimeOnly.MinValue);
            _txtBox.Text = snapshot.TreasuryAccountCode;
            _txtParty.Text = snapshot.PartyName ?? string.Empty;
            _txtAmount.Text = snapshot.TotalAmount.ToString("0.####", CultureInfo.CurrentCulture);
            _txtCur.Text = snapshot.CurrencyCode;
            _txtRate.Text = snapshot.ExchangeRate.ToString("0.########", CultureInfo.CurrentCulture);
            _txtRefNo.Text = snapshot.ReferenceNumber ?? string.Empty;
            _txtCc.Text = snapshot.CostCenter ?? string.Empty;
            _txtMemo.Text = snapshot.Memo ?? string.Empty;

            _lines.Clear();
            foreach (var line in snapshot.Lines)
            {
                _lines.Add(new CshLineVm
                {
                    LineNo = line.LineNumber,
                    AccountCode = line.AccountCode,
                    AccountName = line.AccountName,
                    Amount = line.Amount,
                    CostCenter = line.CostCenter ?? string.Empty,
                    InvoiceRef = line.InvoiceReference ?? string.Empty,
                    Memo = line.Note ?? string.Empty
                });
            }

            _btnSave.IsEnabled = false;
            _btnPost.IsEnabled = snapshot.Status == CashVoucherStatus.Draft;
            _btnReverse.IsEnabled = snapshot.Status == CashVoucherStatus.Posted;
            SetVoucherReadOnly(true);
            RefreshCalculations();
            MessageBox.Show(this,
                $"تم تحميل السند من قاعدة البيانات.\nالحالة: {snapshot.Status}\nالمعرف: {snapshot.Id:D}",
                "تم التحميل", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("LoadByDocumentNumber", ex);
        }
    }

    private async Task ReverseVoucherAsync()
    {
        if (_currentStatus != CashVoucherStatus.Posted || !_currentVoucherId.HasValue)
            return;
        var reason = PromptReversalReason();
        if (string.IsNullOrWhiteSpace(reason))
            return;

        try
        {
            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;
            if (string.IsNullOrWhiteSpace(companyCode) || string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("تعذر تحديد الشركة أو المستخدم الحالي لعكس السند.");
            if (ShouTech.App.Services.AppHost.CashVouchers is not { } service)
                throw new InvalidOperationException("خدمة سندات القبض والصرف غير مهيأة.");

            var companyId = await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var reversalId = await service.ReverseAsync(companyId,
                _kind == CshKind.Receipt ? CashVoucherKind.Receipt : CashVoucherKind.Payment,
                _currentVoucherId.Value, actor, reason).ConfigureAwait(true);
            _currentReversalJournalId = reversalId;
            _currentStatus = CashVoucherStatus.Reversed;
            _btnSave.IsEnabled = false;
            _btnPost.IsEnabled = false;
            _btnReverse.IsEnabled = false;
            SetVoucherReadOnly(true);
            RefreshCalculations();
            MessageBox.Show(this, $"تم عكس السند بإنشاء قيد عكسي موثق.\nرقم القيد العكسي: {reversalId:D}",
                "تم العكس", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("Reverse", ex);
        }
    }

    private string? PromptReversalReason()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = T("Vouchers.ReversalReasonTitle"),
            Width = 440,
            Height = 190,
            MinWidth = 440,
            MinHeight = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            FlowDirection = FlowDirection
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var prompt = new TextBlock
        {
            Text = T("Vouchers.ReversalReasonPrompt"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(prompt, Dock.Top);
        root.Children.Add(prompt);
        var reasonBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56 };
        root.Children.Add(reasonBox);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var confirm = new Button
        {
            Content = T("Common.Confirm"),
            IsDefault = true,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 3, 8, 3)
        };
        confirm.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                MessageBox.Show(dialog, T("Vouchers.ReversalReasonPrompt"), dialog.Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dialog.DialogResult = true;
        };
        var cancel = new Button
        {
            Content = T("Common.Cancel"),
            IsCancel = true,
            MinWidth = 90,
            Padding = new Thickness(8, 3, 8, 3)
        };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog.ShowDialog() == true ? reasonBox.Text.Trim() : null;
    }

    private void SetVoucherReadOnly(bool readOnly)
    {
        _grid.IsReadOnly = readOnly;
        _dtDate.IsEnabled = !readOnly;
        _txtDocNo.IsReadOnly = readOnly;
        _txtBox.IsReadOnly = readOnly;
        _txtParty.IsReadOnly = readOnly;
        _txtAmount.IsReadOnly = readOnly;
        _txtCur.IsReadOnly = readOnly;
        _txtRate.IsReadOnly = readOnly;
        _txtRefNo.IsReadOnly = readOnly;
        _txtCc.IsReadOnly = readOnly;
        _txtMemo.IsReadOnly = readOnly;
        _btnAddLine.IsEnabled = !readOnly;
        _btnRemoveLine.IsEnabled = !readOnly;
    }

    private void ReportPersistenceFailure(string operation, Exception exception)
    {
        const string errorCode = "CSH-PER-001";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var context = $"Operation={operation}; Window={GetType().Name}; Kind={_kind}; Company={ShouTech.App.Services.UsrSession.Current?.CompanyId ?? "(none)"}";
        ShouTech.App.Services.AppHost.Logging?.LogError(
            "WindowPersistence",
            $"[{definition.Code}] {definition.Name} | {context} | {exception.Message}",
            exception);
        ShouTech.App.Services.AppHost.Logging?.LogOperation(
            "WindowPersistence",
            "DatabaseOperationFailed",
            $"Code={definition.Code}; {context}; Error={exception.Message}");
        MessageBox.Show(this,
            $"لم تنجح العملية ولم تُعرض كنجاح.\n[{definition.Code}] {exception.Message}",
            "فشل حفظ السند",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static bool TryParseDecimalInput(string? value, out decimal amount)
    {
        const NumberStyles styles =
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite;
        if (decimal.TryParse(value, styles, CultureInfo.CurrentCulture, out amount))
            return true;
        return decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out amount);
    }

    protected override void ApplyLocalizedText()
    {
        Title = T(_kind == CshKind.Receipt ? "Vouchers.ReceiptTitle" : "Vouchers.PaymentTitle");
        if (_grid.Columns.Count >= 7)
        {
            _grid.Columns[0].Header = "#";
            _grid.Columns[1].Header = T("Vouchers.AccountCode");
            _grid.Columns[2].Header = T("Vouchers.AccountName");
            _grid.Columns[3].Header = T("MfgOrder.Amount");
            _grid.Columns[4].Header = T("Vouchers.CostCenter");
            _grid.Columns[5].Header = T("Vouchers.RefNo");
            _grid.Columns[6].Header = T("Vouchers.Memo");
        }
        RefreshLocalizedContent(Content as DependencyObject);
        RefreshCalculations();
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

    private sealed class CshLineVm : INotifyPropertyChanged
    {
        private const decimal MaximumAmount = 999_999_999_999_999.9999m;
        private int _lineNo;
        private string _accCode = "", _accName = "", _costCenter = "", _invoiceRef = "", _memo = "";
        private string _amountText = string.Empty;
        private decimal _amount;
        private bool _amountTextIsValid = true;

        public int LineNo { get => _lineNo; set { _lineNo = value; OnChanged(); } }
        public string AccountCode { get => _accCode; set { _accCode = value; OnChanged(); } }
        public string AccountName { get => _accName; set { _accName = value; OnChanged(); } }
        public string CostCenter { get => _costCenter; set { _costCenter = value; OnChanged(); } }
        public string InvoiceRef { get => _invoiceRef; set { _invoiceRef = value; OnChanged(); } }
        public string Memo { get => _memo; set { _memo = value; OnChanged(); } }

        public decimal Amount
        {
            get => _amount;
            set
            {
                _amount = value;
                _amountText = value == 0m ? string.Empty : value.ToString("0.####", CultureInfo.CurrentCulture);
                _amountTextIsValid = IsValidAmount(value);
                OnChanged();
                OnChanged(nameof(AmountText));
                OnChanged(nameof(AmountTextIsValid));
            }
        }

        public string AmountText
        {
            get => _amountText;
            set
            {
                _amountText = value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(_amountText))
                {
                    _amount = 0m;
                    _amountTextIsValid = true;
                }
                else if (TryParseDecimalInput(_amountText, out var parsed) && IsValidAmount(parsed))
                {
                    _amount = parsed;
                    _amountTextIsValid = true;
                }
                else
                {
                    _amount = 0m;
                    _amountTextIsValid = false;
                }
                OnChanged(nameof(Amount));
                OnChanged();
                OnChanged(nameof(AmountTextIsValid));
            }
        }

        public bool AmountTextIsValid => _amountTextIsValid;

        public static bool IsValidAmount(decimal amount) =>
            amount >= 0m &&
            amount <= MaximumAmount &&
            decimal.Round(amount, 4) == amount;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}
