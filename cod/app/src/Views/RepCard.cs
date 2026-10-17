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

/// <summary>
/// </summary>
public sealed class RepCard : BaseWin
{

    /// <summary>
    /// Prevents a false success message when this screen has no database persistence wired.
    /// This intentionally does not claim to save any values.
    /// </summary>
    private void ShowPersistenceUnavailable()
    {
        const string errorCode = "APP-2010";
        const string context = "This window has no database persistence implementation.";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var exception = new NotSupportedException(context);

        // Errors are routed to errors.log; the operational trace goes to log.log.
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


    private bool _busy;
    private Guid? _currentRepId;

    private void ReportPersistenceFailure(string operation, Exception exception)
    {
        const string errorCode = "APP-2010";
        var definition = ShouTech.App.Localization.ErrorCatalog.Resolve(errorCode);
        var context = $"Operation={operation}; Window={GetType().Name}; Company={ShouTech.App.Services.UsrSession.Current?.CompanyId ?? "(none)"}";
        ShouTech.App.Services.AppHost.Logging?.LogError(
            "WindowPersistence",
            $"[{definition.Code}] {definition.Name} | {context} | {exception.Message}",
            exception);
        MessageBox.Show(this,
            $"[{definition.Code}] {exception.Message}",
            Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private readonly TextBox _txtCode = new() { Width = 110, Height = 26 };
    private readonly TextBox _txtName = new() { Width = 220, Height = 26 };
    private readonly TextBox _txtPhone = new() { Width = 130, Height = 26 };
    private readonly TextBox _txtComm = new() { Width = 90, Height = 26, Text = "2.5" };
    private readonly TextBox _txtTarget = new() { Width = 120, Height = 26, Text = "100000.00" };

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
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnDelete, "Common.Delete");

        _btnNew.Click += (_, _) => ResetCard();
        _btnSave.Click += async (_, _) => await SaveRepAsync().ConfigureAwait(true);
        _btnDelete.Click += async (_, _) => await DeleteRepAsync().ConfigureAwait(true);

        foreach (var b in new[] { _btnNew, _btnSave, _btnDelete })
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
        headerPanel.Children.Add(Labeled("SalesRepCard.Target", _txtTarget));

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
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.ActualSalesText), 130));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.CommissionEarnedText), 130));
        _grid.Columns.Add(TextCol(nameof(RepTargetVm.Status), 120));

        Grid.SetRow(_grid, 1);
        body.Children.Add(_grid);

        root.Children.Add(body);
        Content = root;
    }

    private void ResetCard()
    {
        _txtCode.Text = "REP-01";
        _txtName.Text = string.Empty;
        _txtPhone.Text = string.Empty;
        _txtComm.Text = "2.5";
        _txtTarget.Text = "100000.00";
        _targets.Clear();
        _targets.Add(new RepTargetVm { Period = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture), Target = 100000, ActualSales = 0, CommissionEarned = 0, Status = T("Invoice.StatusDraft") });
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _lblTotSales.Text = $"{T("SalesRepCard.SalesTotal")}: 0.00 SAR";
        _lblTotComm.Text = $"{T("SalesRepCard.Commission")}: 0.00 SAR";
        _lblTotSales.FontWeight = FontWeights.Bold;
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("SalesRepCard.Title");
        if (_grid.Columns.Count >= 5)
        {
            _grid.Columns[0].Header = T("Invoice.Date");
            _grid.Columns[1].Header = T("SalesRepCard.Target");
            _grid.Columns[2].Header = T("SalesRepCard.SalesTotal");
            _grid.Columns[3].Header = T("SalesRepCard.Commission");
            _grid.Columns[4].Header = T("Invoice.Status");
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

    private static DataGridTextColumn TextCol(string path, double width)
        => new()
        {
            Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            Width = width
        };

    private sealed class RepTargetVm : INotifyPropertyChanged
    {
        private string _period = "", _status = "";
        private decimal _target, _actual, _comm;

        public string Period { get => _period; set { _period = value; OnChanged(); } }
        public decimal Target { get => _target; set { _target = value; OnChanged(nameof(TargetText)); } }
        public decimal ActualSales { get => _actual; set { _actual = value; OnChanged(nameof(ActualSalesText)); } }
        public decimal CommissionEarned { get => _comm; set { _comm = value; OnChanged(nameof(CommissionEarnedText)); } }
        public string Status { get => _status; set { _status = value; OnChanged(); } }

        public string TargetText { get => _target.ToString("N2", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) Target = v; } }
        public string ActualSalesText { get => _actual.ToString("N2", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) ActualSales = v; } }
        public string CommissionEarnedText { get => _comm.ToString("N2", CultureInfo.InvariantCulture); set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) CommissionEarned = v; } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
            var commText = _txtComm.Text.Trim();
            var targetText = _txtTarget.Text.Trim();

            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("\u0631\u0642\u0645 \u0627\u0644\u0631\u062e\u0627\u0645 \u064a\u062c\u0628 \u0623\u0644\u062a\u062d\u0627\u0644.");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("\u0625\u0633\u0645 \u0627\u0644\u0631\u062e\u0627\u0645 \u064a\u062c\u0628 \u0623\u0644\u062a\u0641\u0631\u0627\u0644\u0627\u0644\u064a.");

            if (!decimal.TryParse(commText, NumberStyles.Any, CultureInfo.InvariantCulture, out var commissionRate))
                throw new InvalidOperationException("\u0646\u0633\u0628\u0629 \u0627\u0644\u062f\u0648\u0639\u064a\u0629 \u064a\u062c\u0628 \u0623\u0646 \u064a\u0643\u0648\u0646 \u0645\u0639\u0644\u0648\u0645\u0629.");
            if (commissionRate < 0 || commissionRate > 100)
                throw new InvalidOperationException("\u0646\u0633\u0628\u0629 \u0627\u0644\u062f\u0648\u0639\u064a\u0629 \u064a\u062c\u0628 \u0623\u0644\u0640 \u0628\u064a\u0646 0 \u0627\u0644\u0649 100.");

            var user = ShouTech.App.Services.UsrSession.Current;
            var companyCode = user?.CompanyId;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;

            if (string.IsNullOrWhiteSpace(companyCode))
                throw new InvalidOperationException("\u0644\u0627 \u062a\u0648\u062c\u062f \u0634\u0631\u0643\u0629 \u0646\u0634\u0637\u0629 \u0641\u064a \u062c\u0644\u0633\u0629 \u0627\u0644\u0645\u0633\u062a\u062e\u062f\u0645.");
            if (string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("\u062a\u0639\u0630\u0631 \u062a\u062d\u0641\u064a\u062f \u0627\u0644\u0645\u0633\u062a\u062e\u062f\u0645 \u0627\u0644\u062d\u0627\u0644\u064a \u0644\u062d\u0641\u0638 \u0627\u0644\u0639\u0645\u0644\u0629.");

            var repository = ShouTech.App.Services.AppHost.RepStore
                ?? throw new InvalidOperationException("\u062e\u062f\u0645\u0629 \u0633\u0644\u0627\u0645\u0627\u0629 \u0648\u0623\u0633\u0648\u0631 \u0627\u0644\u0628\u064e\u064a\u0627\u0646\u0627\u062a \u0645\u0640\u0646 \u0645\u0647\u064e\u0627\u0629.");

            var companyId = await repository.ResolveCompanyIdAsync(companyCode).ConfigureAwait(true);

            var rep = new SalesRepresentative(
                companyId,
                code,
                name,
                null,
                commissionRate,
                string.IsNullOrWhiteSpace(phone) ? null : phone);

            var repId = await repository.SaveAsync(rep, actor).ConfigureAwait(true);
            _currentRepId = repId;

            MessageBox.Show(
                this,
                T("SalesRepCard.SavedSuccessfully"),
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

    private async Task DeleteRepAsync()
    {
        if (_busy) return;
        if (!_currentRepId.HasValue)
        {
            MessageBox.Show(
                this,
                T("SalesRepCard.NoRepSelected"),
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            this,
            T("SalesRepCard.ConfirmDelete"),
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        _busy = true;
        _btnDelete.IsEnabled = false;

        try
        {
            var user = ShouTech.App.Services.UsrSession.Current;
            var actor = !string.IsNullOrWhiteSpace(user?.Id)
                ? user.Id
                : ShouTech.App.Services.AppHost.Session.CurrentUser;

            if (string.IsNullOrWhiteSpace(actor))
                throw new InvalidOperationException("\u062a\u0639\u0630\u0631 \u062a\u062d\u0641\u064a\u062f \u0627\u0644\u0645\u0633\u062a\u062e\u062f\u0645 \u0627\u0644\u062d\u0627\u0644\u064a \u0644\u062d\u0641\u0638 \u0627\u0644\u0639\u0645\u0644\u0629.");

            var repository = ShouTech.App.Services.AppHost.RepStore
                ?? throw new InvalidOperationException("\u062e\u062f\u0645\u0629 \u0633\u0644\u0627\u0645\u0627\u0629 \u0648\u0623\u0633\u0648\u0631 \u0627\u0644\u0628\u064e\u064a\u0627\u0646\u0627\u062a \u0645\u0640\u0646 \u0645\u0647\u064e\u0627\u0629.");

            var deleted = await repository.DeleteAsync(_currentRepId.Value, actor, "Deleted via UI").ConfigureAwait(true);
            if (deleted)
            {
                ResetCard();
                _currentRepId = null;
                MessageBox.Show(
                    this,
                    T("SalesRepCard.DeletedSuccessfully"),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    T("SalesRepCard.DeleteFailed"),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            ReportPersistenceFailure("Delete", ex);
        }
        finally
        {
            _busy = false;
            _btnDelete.IsEnabled = true;
        }
    }
}
