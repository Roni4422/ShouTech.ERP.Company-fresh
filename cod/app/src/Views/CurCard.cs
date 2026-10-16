using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ShouTech.Domain.Entities;

namespace ShouTech.App.Views;

/// <summary>
/// Currency master-data maintenance with company-scoped, append-only exchange-rate history.
/// Historical rate rows are read-only; a changed quotation is recorded as a new observation.
/// </summary>
public sealed class CurCard : BaseWin
{
    private readonly TextBox _txtCode = new() { Width = 90, Height = 26, Text = "USD" };
    private readonly TextBox _txtNameAr = new() { Width = 260, Height = 26 };
    private readonly TextBox _txtNameEn = new() { Width = 260, Height = 26 };
    private readonly TextBox _txtSymbol = new() { Width = 90, Height = 26 };
    private readonly TextBox _txtFraction = new() { Width = 260, Height = 26 };
    private readonly TextBox _txtDecimals = new() { Width = 70, Height = 26, Text = "2" };
    private readonly TextBox _txtRate = new() { Width = 90, Height = 26, Text = "1.0000" };
    private readonly TextBox _txtBuyRate = new() { Width = 90, Height = 26, Text = "1.0000" };
    private readonly TextBox _txtSellRate = new() { Width = 90, Height = 26, Text = "1.0000" };
    private readonly TextBox _txtMinRate = new() { Width = 90, Height = 26, Text = "1.0000" };
    private readonly TextBox _txtMaxRate = new() { Width = 90, Height = 26, Text = "1.0000" };
    private readonly TextBox _txtRateNotes = new() { Width = 230, Height = 26 };
    private readonly DatePicker _dtRateDate = new() { Width = 125, Height = 26, SelectedDate = DateTime.Today };

    private readonly Button _btnNew = new();
    private readonly Button _btnLoad = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnAddRate = new();
    private readonly TextBlock _lblBaseCurrency = new();

    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<RateHistoryVm> _rates = new();
    private Guid? _currentCompanyId;
    private string? _baseCurrencyCode;
    private bool _busy;

    public CurCard()
    {
        Width = 1250;
        Height = 660;
        MinWidth = 1050;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        ResetCard();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(10) };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        StyleButton(_btnNew, "Common.New");
        StyleButton(_btnLoad, "CurrencyCard.Load");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnAddRate, "Invoice.AddLine");

        _btnNew.Click += (_, _) => ResetCard();
        _btnLoad.Click += async (_, _) => await LoadCurrencyAsync().ConfigureAwait(true);
        _btnSave.Click += async (_, _) => await SaveCurrencyAsync().ConfigureAwait(true);
        _btnAddRate.Click += async (_, _) => await AddRateAsync().ConfigureAwait(true);

        foreach (var button in new[] { _btnNew, _btnLoad, _btnSave, _btnAddRate })
            toolbar.Children.Add(button);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cardPanel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        var codeRow = new WrapPanel();
        codeRow.Children.Add(Labeled("CurrencyCard.Code", _txtCode));
        codeRow.Children.Add(Labeled("CurrencyCard.Symbol", _txtSymbol));
        cardPanel.Children.Add(codeRow);
        cardPanel.Children.Add(Labeled("CurrencyCard.NameAr", _txtNameAr));
        cardPanel.Children.Add(Labeled("CurrencyCard.NameEn", _txtNameEn));
        cardPanel.Children.Add(Labeled("CurrencyCard.Fraction", _txtFraction));
        var decimalsRow = new WrapPanel();
        decimalsRow.Children.Add(Labeled("CurrencyCard.DecimalPlaces", _txtDecimals));
        cardPanel.Children.Add(decimalsRow);

        var rateRow = new WrapPanel();
        rateRow.Children.Add(Labeled("CurrencyCard.Rate", _txtRate));
        rateRow.Children.Add(Labeled("CurrencyCard.BuyRate", _txtBuyRate));
        rateRow.Children.Add(Labeled("CurrencyCard.SellRate", _txtSellRate));
        cardPanel.Children.Add(rateRow);

        var bandRow = new WrapPanel();
        bandRow.Children.Add(Labeled("CurrencyCard.MinRate", _txtMinRate));
        bandRow.Children.Add(Labeled("CurrencyCard.MaxRate", _txtMaxRate));
        cardPanel.Children.Add(bandRow);

        var dateRow = new WrapPanel();
        dateRow.Children.Add(Labeled("CurrencyCard.RateDate", _dtRateDate));
        cardPanel.Children.Add(dateRow);
        cardPanel.Children.Add(Labeled("CurrencyCard.RateNotes", _txtRateNotes));

        _lblBaseCurrency.Margin = new Thickness(0, 8, 0, 0);
        _lblBaseCurrency.TextWrapping = TextWrapping.Wrap;
        cardPanel.Children.Add(_lblBaseCurrency);

        var leftCard = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = cardPanel
            }
        };
        leftCard.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        leftCard.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Grid.SetColumn(leftCard, 0);
        body.Children.Add(leftCard);

        _grid.AutoGenerateColumns = false;
        _grid.CanUserAddRows = false;
        _grid.IsReadOnly = true;
        _grid.RowHeight = 28;
        _grid.ColumnHeaderHeight = 34;
        _grid.AlternationCount = 2;
        _grid.SetResourceReference(StyleProperty, "VisuDataGridStyle");
        _grid.SetResourceReference(DataGrid.RowStyleProperty, "VisuDataGridRowStyle");
        _grid.SetResourceReference(DataGrid.CellStyleProperty, "VisuDataGridCellStyle");
        _grid.SetResourceReference(DataGrid.RowBackgroundProperty, "SurfaceBrush");
        _grid.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty, "TableAlternateRowBrush");
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader),
            (Style)FindResource("VisuDataGridHeaderStyle"));
        headerStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource("TableHeaderCyanBrush")));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = headerStyle;
        _grid.ItemsSource = _rates;
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.DateText), 105));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.RateText), 90));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.BuyRateText), 90));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.SellRateText), 90));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.MinRateText), 90));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.MaxRateText), 90));
        _grid.Columns.Add(TextCol(nameof(RateHistoryVm.Notes), 180));
        Grid.SetColumn(_grid, 2);
        body.Children.Add(_grid);

        root.Children.Add(body);
        Content = root;
    }

    private void ResetCard()
    {
        _currentCompanyId = null;
        _baseCurrencyCode = null;
        _busy = false;
        _txtCode.IsReadOnly = false;
        _txtCode.Text = "USD";
        _txtNameAr.Text = string.Empty;
        _txtNameEn.Text = string.Empty;
        _txtSymbol.Text = "$";
        _txtFraction.Text = string.Empty;
        _txtDecimals.Text = "2";
        _txtRate.Text = "1.0000";
        _txtBuyRate.Text = "1.0000";
        _txtSellRate.Text = "1.0000";
        _txtMinRate.Text = "1.0000";
        _txtMaxRate.Text = "1.0000";
        _txtRateNotes.Text = string.Empty;
        _dtRateDate.SelectedDate = DateTime.Today;
        _rates.Clear();
        _btnLoad.IsEnabled = true;
        _btnSave.IsEnabled = true;
        _btnAddRate.IsEnabled = true;
        RefreshBaseCurrencyStatus(0);
    }

    private async Task LoadCurrencyAsync()
    {
        if (_busy) return;
        _busy = true;
        _btnLoad.IsEnabled = false;
        try
        {
            var code = NormalizeCurrencyCode(_txtCode.Text);
            var service = ShouTech.App.Services.AppHost.Currencies
                ?? throw new InvalidOperationException("خدمة العملات وأسعار الصرف غير مهيأة؛ تحقق من إعداد اتصال قاعدة البيانات.");
            var companyCode = ShouTech.App.Services.UsrSession.Current?.CompanyId;
            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("لا توجد شركة نشطة في جلسة المستخدم.");

            var companyId = await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var snapshot = await service.LoadAsync(companyId, code).ConfigureAwait(true);
            if (snapshot is null)
                throw new InvalidOperationException($"العملة '{code}' غير موجودة أو غير نشطة. استخدم «جديد» لإنشاء سجل عملة.");

            _currentCompanyId = companyId;
            _txtCode.Text = snapshot.Definition.Code;
            _txtCode.IsReadOnly = true;
            _txtNameAr.Text = snapshot.Definition.NameArabic;
            _txtNameEn.Text = snapshot.Definition.NameEnglish ?? string.Empty;
            _txtSymbol.Text = snapshot.Definition.Symbol ?? string.Empty;
            _txtFraction.Text = snapshot.Definition.FractionName ?? string.Empty;
            _txtDecimals.Text = snapshot.Definition.DecimalPlaces.ToString(CultureInfo.InvariantCulture);
            _baseCurrencyCode = snapshot.CompanyBaseCurrencyCode;

            _rates.Clear();
            foreach (var item in snapshot.Rates)
                _rates.Add(new RateHistoryVm(item));
            _dtRateDate.SelectedDate = DateTime.Today;

            if (snapshot.Rates.Count > 0)
            {
                var latest = snapshot.Rates[0];
                _txtRate.Text = latest.Rate.ToString("0.########", CultureInfo.CurrentCulture);
                _txtBuyRate.Text = latest.BuyRate.ToString("0.########", CultureInfo.CurrentCulture);
                _txtSellRate.Text = latest.SellRate.ToString("0.########", CultureInfo.CurrentCulture);
                _txtMinRate.Text = latest.MinRate.ToString("0.########", CultureInfo.CurrentCulture);
                _txtMaxRate.Text = latest.MaxRate.ToString("0.########", CultureInfo.CurrentCulture);
                _txtRateNotes.Text = latest.Notes ?? string.Empty;
            }
            else if (string.Equals(code, _baseCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                _txtRate.Text = "1";
                _txtBuyRate.Text = "1";
                _txtSellRate.Text = "1";
                _txtMinRate.Text = "1";
                _txtMaxRate.Text = "1";
                _txtRateNotes.Text = string.Empty;
                _dtRateDate.SelectedDate = DateTime.Today;
            }
            else
            {
                _txtRate.Text = string.Empty;
                _txtBuyRate.Text = string.Empty;
                _txtSellRate.Text = string.Empty;
                _txtMinRate.Text = string.Empty;
                _txtMaxRate.Text = string.Empty;
                _txtRateNotes.Text = string.Empty;
                _dtRateDate.SelectedDate = DateTime.Today;
            }

            _btnAddRate.IsEnabled = !string.Equals(code, _baseCurrencyCode, StringComparison.OrdinalIgnoreCase);
            RefreshBaseCurrencyStatus(0);
            MessageBox.Show(this, T("CurrencyCard.Loaded"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("Load", ex);
        }
        finally
        {
            _busy = false;
            _btnLoad.IsEnabled = true;
        }
    }

    private async Task AddRateAsync()
    {
        if (_busy) return;
        try
        {
            var code = NormalizeCurrencyCode(_txtCode.Text);
            var service = ShouTech.App.Services.AppHost.Currencies
                ?? throw new InvalidOperationException("خدمة العملات وأسعار الصرف غير مهيأة؛ تحقق من إعداد اتصال قاعدة البيانات.");
            var companyCode = ShouTech.App.Services.UsrSession.Current?.CompanyId;
            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("لا توجد شركة نشطة في جلسة المستخدم.");
            var companyId = _currentCompanyId ?? await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var baseCurrency = await service.GetCompanyBaseCurrencyCodeAsync(companyId).ConfigureAwait(true);
            if (string.Equals(code, baseCurrency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{code}' هي عملة الشركة الأساسية؛ لا يلزم ولا يجوز تسجيل سعر صرف لها مقابل نفسها ({baseCurrency}).");

            if (_dtRateDate.SelectedDate is not DateTime selectedDate)
                throw new InvalidOperationException("تاريخ سريان سعر الصرف مطلوب.");
            var rate = ParseRate(_txtRate.Text, "سعر الصرف");
            var buy = ParseRate(_txtBuyRate.Text, "سعر الشراء");
            var sell = ParseRate(_txtSellRate.Text, "سعر البيع");
            var minimum = ParseRate(_txtMinRate.Text, "الحد الأدنى");
            var maximum = ParseRate(_txtMaxRate.Text, "الحد الأعلى");
            var draft = new CurrencyRateDraft(DateOnly.FromDateTime(selectedDate), rate, buy, sell, minimum, maximum, _txtRateNotes.Text);

            if (_rates.Any(item => item.Date.Date == selectedDate.Date))
                throw new InvalidOperationException($"يوجد سعر مسجل بالفعل لهذا التاريخ ({selectedDate:yyyy-MM-dd}). الأسعار التاريخية لا تُستبدل؛ استخدم تاريخ سريان جديدًا.");

            _currentCompanyId = companyId;
            _baseCurrencyCode = baseCurrency;
            _txtCode.IsReadOnly = true;
            _rates.Insert(0, new RateHistoryVm(draft) { IsPending = true });
            _dtRateDate.SelectedDate = selectedDate.Date.AddDays(1);
            RefreshBaseCurrencyStatus(_rates.Count(item => item.IsPending));
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("QueueRate", ex);
        }
    }

    private async Task SaveCurrencyAsync()
    {
        if (_busy) return;
        _busy = true;
        _btnSave.IsEnabled = false;
        try
        {
            var definition = new CurrencyDefinition(
                _txtCode.Text,
                _txtNameAr.Text,
                _txtNameEn.Text,
                _txtSymbol.Text,
                _txtFraction.Text,
                ParseDecimalPlaces(_txtDecimals.Text));
            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;
            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("لا توجد شركة نشطة في جلسة المستخدم.");
            if (string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("تعذر تحديد المستخدم الحالي لحفظ العملة.");

            var service = ShouTech.App.Services.AppHost.Currencies
                ?? throw new InvalidOperationException("خدمة العملات وأسعار الصرف غير مهيأة؛ تحقق من إعداد اتصال قاعدة البيانات.");
            var companyId = _currentCompanyId ?? await service.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
            var pending = _rates.Where(item => item.IsPending)
                .Select(item => new CurrencyRateDraft(
                    DateOnly.FromDateTime(item.Date),
                    item.Rate,
                    item.BuyRate,
                    item.SellRate,
                    item.MinRate,
                    item.MaxRate,
                    item.Notes))
                .ToArray();

            var count = await service.SaveAsync(companyId, definition, pending, actor).ConfigureAwait(true);
            var snapshot = await service.LoadAsync(companyId, definition.Code).ConfigureAwait(true)
                ?? throw new InvalidOperationException("تم حفظ سجل العملة ولكن تعذر إعادة قراءته للتحقق من سلامة العملية.");

            _currentCompanyId = companyId;
            _baseCurrencyCode = snapshot.CompanyBaseCurrencyCode;
            _txtCode.Text = snapshot.Definition.Code;
            _txtCode.IsReadOnly = true;
            _rates.Clear();
            foreach (var item in snapshot.Rates)
                _rates.Add(new RateHistoryVm(item));
            RefreshBaseCurrencyStatus(0);
            _btnAddRate.IsEnabled = !string.Equals(definition.Code, _baseCurrencyCode, StringComparison.OrdinalIgnoreCase);

            MessageBox.Show(
                this,
                string.Format(CultureInfo.CurrentCulture, T("CurrencyCard.SavedSummary"), definition.Code, count),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("Save", ex);
        }
        finally
        {
            _busy = false;
            _btnSave.IsEnabled = true;
        }
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("CurrencyCard.Title");
        if (_grid.Columns.Count >= 7)
        {
            _grid.Columns[0].Header = T("CurrencyCard.RateDate");
            _grid.Columns[1].Header = T("CurrencyCard.Rate");
            _grid.Columns[2].Header = T("CurrencyCard.BuyRate");
            _grid.Columns[3].Header = T("CurrencyCard.SellRate");
            _grid.Columns[4].Header = T("CurrencyCard.MinRate");
            _grid.Columns[5].Header = T("CurrencyCard.MaxRate");
            _grid.Columns[6].Header = T("CurrencyCard.RateNotes");
        }
        RefreshLocalizedContent(Content as DependencyObject);
        RefreshBaseCurrencyStatus(_rates.Count(item => item.IsPending));
    }

    private void RefreshBaseCurrencyStatus(int pendingCount)
    {
        var baseCode = _baseCurrencyCode ?? "—";
        var text = string.Format(CultureInfo.CurrentCulture, T("CurrencyCard.BaseCurrency"), baseCode);
        _lblBaseCurrency.Text = pendingCount > 0
            ? $"{text}{Environment.NewLine}{string.Format(CultureInfo.CurrentCulture, T("CurrencyCard.PendingRates"), pendingCount)}"
            : text;
    }

    private static void RefreshLocalizedContent(DependencyObject? parent)
    {
        if (parent == null) return;
        if (parent is Button button && button.Tag is string bKey)
            button.Content = T(bKey);
        if (parent is TextBlock block && block.Tag is string tKey)
            block.Text = T(tKey);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            RefreshLocalizedContent(VisualTreeHelper.GetChild(parent, i));
    }

    private void ReportPersistenceFailure(string operation, Exception exception)
    {
        const string errorCode = "APP-2010";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var context = $"Operation={operation}; Window={GetType().Name}; Currency={_txtCode.Text.Trim()}; Company={ShouTech.App.Services.UsrSession.Current?.CompanyId ?? "(none)"}";
        ShouTech.App.Services.AppHost.Logging?.LogError(
            "WindowPersistence",
            $"[{definition.Code}] {definition.Name} | {context} | {exception.Message}",
            exception);
        ShouTech.App.Services.AppHost.Logging?.LogOperation(
            "WindowPersistence",
            "DatabaseOperationFailed",
            $"Code={definition.Code}; {context}; Error={exception.Message}");
        MessageBox.Show(this,
            $"{T("CurrencyCard.OperationFailed")}{Environment.NewLine}[{definition.Code}] {exception.Message}",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static string NormalizeCurrencyCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length != 3 || !value.Trim().All(char.IsAsciiLetter))
            throw new ArgumentException("رمز العملة يجب أن يتكون من ثلاثة أحرف لاتينية.");
        return value.Trim().ToUpperInvariant();
    }

    private static decimal ParseRate(string? value, string label)
    {
        const NumberStyles styles =
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite;
        var parsed = decimal.TryParse(value, styles, CultureInfo.CurrentCulture, out var amount) ||
                     decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out amount);
        if (!parsed || amount <= 0 || amount > 99_999_999_999.99999999m || decimal.Round(amount, 8) != amount)
            throw new ArgumentException($"{label} يجب أن يكون موجبًا وبحد أقصى ثماني منازل عشرية.");
        return amount;
    }

    private static int ParseDecimalPlaces(string? value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result is < 0 or > 6)
            throw new ArgumentException("عدد المنازل العشرية يجب أن يكون عددًا صحيحًا بين 0 و6.");
        return result;
    }

    private static StackPanel Labeled(string key, UIElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 5) };
        panel.Children.Add(new TextBlock { Text = T(key), Tag = key, Margin = new Thickness(0, 0, 0, 2), Opacity = 0.85 });
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

    private static DataGridTextColumn TextCol(string path, double width)
        => new()
        {
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width
        };

    private sealed class RateHistoryVm
    {
        public long RateId { get; init; }
        public bool IsPending { get; set; }
        public DateTime Date { get; init; } = DateTime.Today;
        public decimal Rate { get; init; }
        public decimal BuyRate { get; init; }
        public decimal SellRate { get; init; }
        public decimal MinRate { get; init; }
        public decimal MaxRate { get; init; }
        public string? Notes { get; init; }
        public string DateText => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        public string RateText => Rate.ToString("0.########", CultureInfo.InvariantCulture);
        public string BuyRateText => BuyRate.ToString("0.########", CultureInfo.InvariantCulture);
        public string SellRateText => SellRate.ToString("0.########", CultureInfo.InvariantCulture);
        public string MinRateText => MinRate.ToString("0.########", CultureInfo.InvariantCulture);
        public string MaxRateText => MaxRate.ToString("0.########", CultureInfo.InvariantCulture);

        public RateHistoryVm() { }

        public RateHistoryVm(CurrencyRateDraft draft)
        {
            Date = draft.EffectiveDate.ToDateTime(TimeOnly.MinValue);
            Rate = draft.Rate;
            BuyRate = draft.BuyRate;
            SellRate = draft.SellRate;
            MinRate = draft.MinRate;
            MaxRate = draft.MaxRate;
            Notes = draft.Notes;
        }

        public RateHistoryVm(CurrencyRateSnapshot snapshot)
        {
            RateId = snapshot.RateId;
            Date = snapshot.EffectiveDate.ToDateTime(TimeOnly.MinValue);
            Rate = snapshot.Rate;
            BuyRate = snapshot.BuyRate;
            SellRate = snapshot.SellRate;
            MinRate = snapshot.MinRate;
            MaxRate = snapshot.MaxRate;
            Notes = snapshot.Notes;
        }

    }
}
