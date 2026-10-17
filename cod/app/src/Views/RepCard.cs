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
using System.Threading.Tasks;
using ShouTech.App.Services;
using ShouTech.App.Localization;
using ShouTech.Domain.Interfaces;
using ShouTech.Domain.Entities;

namespace ShouTech.App.Views;

/// <summary>
/// </summary>
public sealed class RepCard : BaseWin
{
    private bool _busy;
    private Guid? _currentRepId;

    private sealed class RepCardFailureException : InvalidOperationException
    {
        public string ErrorCode { get; }
        public RepCardFailureException(string errorCode, string message) : base(message) => ErrorCode = errorCode;
    }

    private readonly TextBox _txtCode = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtName = new() { Width = 220, Height = 26 };
    private readonly TextBox _txtPhone = new() { Width = 130, Height = 26 };
    private readonly TextBox _txtComm = new() { Width = 90, Height = 26, Text = "2.5" };
    private readonly Button _btnLoad = new();
    private readonly Button _btnAddTarget = new();

    private readonly Button _btnNew = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();

    private readonly TextBlock _lblTotSales = new();
    private readonly TextBlock _lblTotComm = new();

    private readonly DataGrid _grid = new();
    private readonly ObservableCollection<RepTargetVm> _targets = new();

    public RepCard()
    {
        Width = 760;
        Height = 490;
        MinWidth = 680;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        ResetCard();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(10) };

        // 1. Toolbar
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        StyleButton(_btnNew, "Common.New");
        StyleButton(_btnLoad, "SalesRepCard.Load");
        StyleButton(_btnAddTarget, "SalesRepCard.AddTarget");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnDelete, "Common.Delete");

        _btnNew.Click += (_, _) => ResetCard();
        _btnLoad.Click += async (_, _) => await LoadRepAsync().ConfigureAwait(true);
        _btnAddTarget.Click += (_, _) => AddTargetRow();
        _btnSave.Click += async (_, _) => await SaveRepAsync().ConfigureAwait(true);
        _btnDelete.Click += async (_, _) => await DeleteRepAsync().ConfigureAwait(true);
        _txtCode.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                await LoadRepAsync().ConfigureAwait(true);
            }
        };

        foreach (var b in new[] { _btnNew, _btnLoad, _btnAddTarget, _btnSave, _btnDelete })
            toolbar.Children.Add(b);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // 2. Metrics Bar
        var metricsBorder = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(10, 6, 10, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        metricsBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        metricsBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var metricsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        metricsPanel.Children.Add(_lblTotSales);
        metricsPanel.Children.Add(new TextBlock { Width = 25 });
        metricsPanel.Children.Add(_lblTotComm);
        metricsBorder.Child = metricsPanel;

        DockPanel.SetDock(metricsBorder, Dock.Bottom);
        root.Children.Add(metricsBorder);

        // 3. Body
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        headerPanel.Children.Add(Labeled("SalesRepCard.Code", _txtCode));
        headerPanel.Children.Add(Labeled("SalesRepCard.Name", _txtName));
        headerPanel.Children.Add(Labeled("SalesRepCard.Phone", _txtPhone));
        headerPanel.Children.Add(Labeled("SalesRepCard.Commission", _txtComm));

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
        customHeaderStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)FindResource("TableHeaderSalesBrush")));
        customHeaderStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        _grid.ColumnHeaderStyle = customHeaderStyle;

        _grid.ItemsSource = _targets;
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.Period), 110));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.TargetText), 120));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.ActualSalesText), 130, readOnly: true));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.CommissionEarnedText), 130, readOnly: true));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.Notes), 200));

        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);

        root.Children.Add(body);
        Content = root;
    }

    private void ResetCard()
    {
        _currentRepId = null;
        _txtCode.IsReadOnly = false;
        _btnSave.IsEnabled = true;
        _btnDelete.IsEnabled = false;
        _txtCode.Text = "REP-" + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtName.Text = string.Empty;
        _txtPhone.Text = string.Empty;
        _txtComm.Text = "0";
        _targets.Clear();
        UpdateSummary();
    }

    private void AddTargetRow()
    {
        var period = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var existing = _targets.FirstOrDefault(x => string.Equals(x.Period, period, StringComparison.Ordinal));
        if (existing is not null)
        {
            _grid.SelectedItem = existing;
            _grid.ScrollIntoView(existing);
            return;
        }
        var row = new RepTargetVm { Period = period };
        _targets.Add(row);
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row);
    }

    private void UpdateSummary()
    {
        // Financial actuals must come from posted documents; no aggregate query is wired here yet.
        _lblTotSales.Text = $"{T("SalesRepCard.SalesTotal")}: —";
        _lblTotComm.Text = $"{T("SalesRepCard.Commission")}: —";
        _lblTotSales.FontWeight = FontWeights.Bold;
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("SalesRepCard.Title");
        if (_grid.Columns.Count >= 5)
        {
            _grid.Columns[0].Header = T("SalesRepCard.Period");
            _grid.Columns[1].Header = T("SalesRepCard.Target");
            _grid.Columns[2].Header = T("SalesRepCard.SalesTotal");
            _grid.Columns[3].Header = T("SalesRepCard.Commission");
            _grid.Columns[4].Header = T("SalesRepCard.Notes");
        }
        RefreshLocalizedContent(Content as DependencyObject);
        UpdateSummary();
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
        var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 4) };
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

    private static DataGridTextColumn TextCol(string path, double width, bool readOnly = false)
        => new()
        {
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width,
            IsReadOnly = readOnly
        };

    private sealed class RepTargetVm : INotifyPropertyChanged
    {
        private string _period = string.Empty, _targetText = string.Empty, _notes = string.Empty;
        private decimal? _actualSales, _commissionEarned;

        public string Period { get => _period; set { _period = value?.Trim() ?? string.Empty; OnChanged(); } }
        public string TargetText { get => _targetText; set { _targetText = value?.Trim() ?? string.Empty; OnChanged(); } }
        public string Notes { get => _notes; set { _notes = value ?? string.Empty; OnChanged(); } }
        public decimal? ActualSales { get => _actualSales; set { _actualSales = value; OnChanged(nameof(ActualSalesText)); } }
        public decimal? CommissionEarned { get => _commissionEarned; set { _commissionEarned = value; OnChanged(nameof(CommissionEarnedText)); } }
        public string ActualSalesText => _actualSales?.ToString("N2", CultureInfo.InvariantCulture) ?? "—";
        public string CommissionEarnedText => _commissionEarned?.ToString("N2", CultureInfo.InvariantCulture) ?? "—";
        public bool IsBlankDraft => string.IsNullOrWhiteSpace(TargetText) && string.IsNullOrWhiteSpace(Notes);

        public static RepTargetVm FromRecord(SalesRepTargetRecord record) => new()
        {
            Period = record.PeriodCode,
            TargetText = record.TargetAmount.ToString("0.####", CultureInfo.InvariantCulture),
            Notes = record.Notes
        };

        public SalesRepTargetRecord ToRecord()
        {
            if (!DateOnly.TryParseExact(Period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed,
                    "Every target period must use the YYYY-MM format with a valid month.");
            if (!decimal.TryParse(TargetText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed,
                    $"Enter a valid monthly target amount for period {Period}.");
            return new SalesRepTargetRecord { PeriodCode = Period, TargetAmount = amount, Notes = Notes.Trim() };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static async Task<(ISalesRepresentativeRepository Repository, string CompanyCode, string Actor, Guid CompanyId)> ResolvePersistenceContextAsync()
    {
        var user = UsrSession.Current;
        var companyCode = user?.CompanyId?.Trim();
        var actor = !string.IsNullOrWhiteSpace(user?.Id) ? user.Id.Trim() : AppHost.Session.CurrentUser;
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new RepCardFailureException(ErrorCodes.SalesRepContextFailed,
                "Select an active company before using the sales-representative card.");
        if (string.IsNullOrWhiteSpace(actor))
            throw new RepCardFailureException(ErrorCodes.SalesRepContextFailed,
                "The current user identity could not be resolved for this operation.");
        var repository = AppHost.RepStore
            ?? throw new RepCardFailureException(ErrorCodes.SalesRepContextFailed,
                "The sales-representative repository is not configured.");
        var companyId = await repository.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);
        return (repository, companyCode, actor, companyId);
    }

    private async Task LoadRepAsync()
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_txtCode.Text))
        {
            ReportPersistenceFailure("Load", new RepCardFailureException(
                ErrorCodes.SalesRepValidationFailed, "Enter a sales-representative code first."));
            return;
        }
        _busy = true;
        _btnLoad.IsEnabled = false;
        try
        {
            var context = await ResolvePersistenceContextAsync().ConfigureAwait(true);
            var snapshot = await context.Repository.FindByCodeAsync(context.CompanyId, _txtCode.Text.Trim()).ConfigureAwait(true);
            if (snapshot is null)
            {
                ReportPersistenceFailure("Load", new RepCardFailureException(
                    ErrorCodes.SalesRepNotFound, "No active sales representative was found for this code in the selected company."));
                return;
            }
            _currentRepId = snapshot.Id;
            _txtCode.Text = snapshot.Code;
            _txtCode.IsReadOnly = true;
            _txtName.Text = snapshot.Name;
            _txtPhone.Text = snapshot.Phone;
            _txtComm.Text = snapshot.CommissionRate.ToString("0.##", CultureInfo.InvariantCulture);
            _btnDelete.IsEnabled = true;
            _targets.Clear();
            foreach (var target in await SalesRepTargetSvc.LoadAsync(context.CompanyId, snapshot.Id).ConfigureAwait(true))
                _targets.Add(RepTargetVm.FromRecord(target));
            UpdateSummary();
            AppHost.Logging?.LogOperation("SalesRepresentativeCard", "Load",
                $"Company={context.CompanyCode}; Code={snapshot.Code}; Outcome=Loaded; TargetCount={_targets.Count}");
        }
        catch (Exception ex) { ReportPersistenceFailure("Load", ex); }
        finally { _busy = false; _btnLoad.IsEnabled = true; }
    }

    private async Task SaveRepAsync()
    {
        if (_busy) return;
        _busy = true;
        _btnSave.IsEnabled = false;
        try
        {
            var code = _txtCode.Text.Trim();
            var name = _txtName.Text.Trim();
            var phone = _txtPhone.Text.Trim();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 50)
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed,
                    "Enter a sales-representative code with no more than 50 characters.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed,
                    "Enter a representative name with no more than 200 characters.");
            if (phone.Length > 30)
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed,
                    "The phone number cannot exceed 30 characters.");
            if (!decimal.TryParse(_txtComm.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var commissionRate)
                && !decimal.TryParse(_txtComm.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out commissionRate))
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed, "Enter a valid commission percentage.");
            if (commissionRate < 0 || commissionRate > 100)
                throw new RepCardFailureException(ErrorCodes.SalesRepValidationFailed, "Commission percentage must be between 0 and 100.");

            var targetRows = _targets.Where(row => !row.IsBlankDraft).Select(row => row.ToRecord()).ToArray();
            var context = await ResolvePersistenceContextAsync().ConfigureAwait(true);
            var existingByCode = await context.Repository.FindByCodeIncludingDeletedAsync(context.CompanyId, code).ConfigureAwait(true);
            var currentByCode = existingByCode is { IsDeleted: false } ? existingByCode : null;
            Guid representativeId;
            if (_currentRepId.HasValue)
            {
                if (currentByCode is null || currentByCode.Id != _currentRepId.Value)
                    throw new RepCardFailureException(ErrorCodes.SalesRepNotFound,
                        "The loaded representative no longer exists in the selected company. Reload the card.");
                var entity = SalesRepresentative.Rehydrate(currentByCode);
                var modifierId = Guid.TryParse(context.Actor, out var parsedActor) ? parsedActor : Guid.Empty;
                entity.Update(name: name, area: currentByCode.Area, commissionRate: commissionRate,
                    phone: phone, modifiedBy: modifierId);
                if (!await context.Repository.UpdateAsync(entity, context.Actor).ConfigureAwait(true))
                    throw new RepCardFailureException(ErrorCodes.SalesRepSaveFailed,
                        "The representative record changed during save. Reload it and try again.");
                representativeId = currentByCode.Id;
            }
            else
            {
                if (existingByCode is { IsDeleted: true })
                    throw new RepCardFailureException(ErrorCodes.SalesRepCodeRequiresRestore,
                        "This code belongs to a deleted record. Restore it from the Recycle Bin before reusing the code.");
                if (currentByCode is not null)
                    throw new RepCardFailureException(ErrorCodes.SalesRepDuplicateCode,
                        "This code already exists in the selected company. Load it before editing.");
                var entity = new SalesRepresentative(context.CompanyId, code, name, null, commissionRate,
                    string.IsNullOrWhiteSpace(phone) ? null : phone);
                representativeId = await context.Repository.SaveAsync(entity, context.Actor).ConfigureAwait(true);
                // Preserve the persisted identity so a target-save retry can update the same record safely.
                _currentRepId = representativeId;
                _txtCode.IsReadOnly = true;
                _btnDelete.IsEnabled = true;
            }

            if (targetRows.Length > 0)
                await SalesRepTargetSvc.SaveAsync(context.CompanyId, representativeId, targetRows, context.Actor).ConfigureAwait(true);
            _currentRepId = representativeId;
            _txtCode.Text = code;
            _txtCode.IsReadOnly = true;
            _btnDelete.IsEnabled = true;
            _targets.Clear();
            foreach (var target in await SalesRepTargetSvc.LoadAsync(context.CompanyId, representativeId).ConfigureAwait(true))
                _targets.Add(RepTargetVm.FromRecord(target));
            UpdateSummary();
            AppHost.Logging?.LogOperation("SalesRepresentativeCard", "Save",
                $"Company={context.CompanyCode}; Code={code}; RepresentativeId={representativeId:D}; TargetCount={targetRows.Length}; Outcome=Saved");
            MessageBox.Show(this, T("SalesRepCard.Saved"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ReportPersistenceFailure("Save", ex); }
        finally { _busy = false; _btnSave.IsEnabled = true; }
    }

    private async Task DeleteRepAsync()
    {
        if (_busy) return;
        if (!_currentRepId.HasValue)
        {
            ReportPersistenceFailure("Delete", new RepCardFailureException(
                ErrorCodes.SalesRepValidationFailed, "Load or save a sales representative before deleting."));
            return;
        }
        var result = MessageBox.Show(this, T("SalesRepCard.ConfirmDelete"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _busy = true;
        _btnDelete.IsEnabled = false;
        try
        {
            var context = await ResolvePersistenceContextAsync().ConfigureAwait(true);
            var deleted = await context.Repository.DeleteAsync(
                _currentRepId.Value, context.CompanyId, context.Actor, "Deleted via UI").ConfigureAwait(true);
            if (!deleted)
                throw new RepCardFailureException(ErrorCodes.SalesRepDeleteNotFound,
                    "The representative was not found in the active company or was already deleted.");
            AppHost.Logging?.LogOperation("SalesRepresentativeCard", "SoftDelete",
                $"Company={context.CompanyCode}; RepresentativeId={_currentRepId.Value:D}; Outcome=Committed");
            ResetCard();
            MessageBox.Show(this, T("SalesRepCard.Deleted"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ReportPersistenceFailure("Delete", ex); }
        finally { _busy = false; _btnDelete.IsEnabled = _currentRepId.HasValue; }
    }

    private void ReportPersistenceFailure(string operation, Exception exception)
    {
        var fallbackCode = operation switch
        {
            "Load" => ErrorCodes.SalesRepLoadFailed,
            "Delete" => ErrorCodes.SalesRepDeleteFailed,
            _ => ErrorCodes.SalesRepSaveFailed
        };
        var code = exception is RepCardFailureException failure ? failure.ErrorCode : fallbackCode;
        var definition = ErrorCatalog.Resolve(code);
        var context = $"Operation={operation}; Window={GetType().Name}; Company={UsrSession.Current?.CompanyId ?? "(none)"}; Code={_txtCode.Text.Trim()}";
        AppHost.Logging?.LogError("SalesRepresentativeCard",
            $"[{definition.Code}] {definition.Name}; {context}; Error={exception.Message}", exception);
        AppHost.Logging?.LogOperation("SalesRepresentativeCard", "DatabaseOperationFailed",
            $"Code={definition.Code}; {context}");
        MessageBox.Show(this,
            $"[{definition.Code}] {definition.Message}\nLocation: {definition.Location}\nReview the error log before retrying; some changes may already have been committed.",
            Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

}
