using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using System.Threading.Tasks;
using ShouTech.App.Services;
using ShouTech.App.Localization;

namespace ShouTech.App.Views;

/// <summary>
/// Company-scoped cost-center maintenance with persisted hierarchy and soft-delete.
/// </summary>
public sealed class CcCard : BaseWin
{

    private readonly TextBox _txtCode = new() { Width = 130, Height = 26 };
    private readonly TextBox _txtParentCode = new() { Width = 130, Height = 26 };
    private readonly TextBox _txtNameAr = new() { Width = 230, Height = 26 };
    private readonly TextBox _txtNameEn = new() { Width = 230, Height = 26 };
    private readonly TextBox _txtLevel = new() { Width = 80, Height = 26, Text = "1" };
    private readonly TextBox _txtNotes = new() { Width = 470, Height = 26 };

    private readonly TextBlock _lblDr = new();
    private readonly TextBlock _lblCr = new();
    private readonly TextBlock _lblBal = new();

    private readonly Button _btnNew = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();

    public CcCard()
    {
        Width = 660;
        Height = 380;
        MinWidth = 600;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUi();
        ApplyLocalizedText();
        ResetCard();
    }

    private void BuildUi()
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        // 1. Toolbar
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        StyleButton(_btnNew, "Common.New");
        StyleButton(_btnSave, "Common.Save");
        StyleButton(_btnDelete, "Common.Delete");

        _btnNew.Click += (_, _) => ResetCard();
        _btnSave.Click += async (_, _) => await SaveCardAsync();
        _btnDelete.Click += async (_, _) => await DeleteCardAsync();
        _txtCode.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                await LoadCardAsync();
            }
        };

        foreach (var b in new[] { _btnNew, _btnSave, _btnDelete })
            toolbar.Children.Add(b);

        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        // 2. Metrics Bar
        var metricsBorder = new Border
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        metricsBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondaryBrush");
        metricsBorder.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var metricsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        metricsPanel.Children.Add(_lblDr);
        metricsPanel.Children.Add(new TextBlock { Width = 25 });
        metricsPanel.Children.Add(_lblCr);
        metricsPanel.Children.Add(new TextBlock { Width = 25 });
        metricsPanel.Children.Add(_lblBal);
        metricsBorder.Child = metricsPanel;

        DockPanel.SetDock(metricsBorder, Dock.Bottom);
        root.Children.Add(metricsBorder);

        // 3. Form Content
        var formPanel = new StackPanel { Margin = new Thickness(4) };

        var r1 = new WrapPanel();
        r1.Children.Add(Labeled("CostCenter.Code", _txtCode));
        r1.Children.Add(Labeled("CostCenter.ParentCode", _txtParentCode));
        r1.Children.Add(Labeled("CostCenter.Level", _txtLevel));
        formPanel.Children.Add(r1);

        var r2 = new WrapPanel();
        r2.Children.Add(Labeled("CostCenter.NameAr", _txtNameAr));
        r2.Children.Add(Labeled("CostCenter.NameEn", _txtNameEn));
        formPanel.Children.Add(r2);

        var r3 = new WrapPanel();
        r3.Children.Add(Labeled("CostCenter.Notes", _txtNotes));
        formPanel.Children.Add(r3);

        root.Children.Add(formPanel);
        Content = root;
    }

    private void ResetCard()
    {
        _txtCode.Text = "CC-" + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtParentCode.Text = string.Empty;
        _txtNameAr.Text = string.Empty;
        _txtNameEn.Text = string.Empty;
        _txtLevel.Text = "1";
        _txtNotes.Text = string.Empty;
        UpdateBalancesUnavailable();
    }

    private void UpdateBalances(decimal debit, decimal credit)
    {
        _lblDr.Text = $"{T("CostCenter.Debit")}: {debit:N2} SAR";
        _lblCr.Text = $"{T("CostCenter.Credit")}: {credit:N2} SAR";
        var bal = debit - credit;
        _lblBal.Text = $"{T("CostCenter.Balance")}: {bal:N2} SAR";
        _lblBal.FontWeight = FontWeights.Bold;
    }


    private CostCenterCardRecord ReadCard()
    {
        if (!int.TryParse(_txtLevel.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var level)
            && !int.TryParse(_txtLevel.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out level))
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed, "Enter a whole number for the hierarchy level.");

        return new CostCenterCardRecord
        {
            Code = _txtCode.Text,
            ParentCode = _txtParentCode.Text,
            Level = level,
            NameAr = _txtNameAr.Text,
            NameEn = _txtNameEn.Text,
            Notes = _txtNotes.Text
        };
    }

    private async Task SaveCardAsync()
    {
        try
        {
            var record = ReadCard();
            await CostCenterCardSvc.SaveAsync(record);
            _txtCode.Text = record.Code.Trim();
            MessageBox.Show(this, T("CostCenter.Saved"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportCostCenterFailure(ex, ErrorCodes.CostCenterSaveFailed, "Save");
        }
    }

    private async Task LoadCardAsync()
    {
        if (_isLoadingRecord) return;
        _isLoadingRecord = true;
        try
        {
            var record = await CostCenterCardSvc.LoadByCodeAsync(_txtCode.Text);
            if (record is null)
            {
                var definition = ErrorCatalog.Resolve(ErrorCodes.CostCenterNotFound);
                AppHost.Logging?.LogOperation("CostCenterCard", "LoadNotFound",
                    $"Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}");
                MessageBox.Show(this, $"[{definition.Code}] {definition.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _txtCode.Text = record.Code;
            _txtParentCode.Text = record.ParentCode;
            _txtLevel.Text = record.Level.ToString(CultureInfo.CurrentCulture);
            _txtNameAr.Text = record.NameAr;
            _txtNameEn.Text = record.NameEn;
            _txtNotes.Text = record.Notes;
            UpdateBalancesUnavailable();
            AppHost.Logging?.LogOperation("CostCenterCard", "Load",
                $"Company={AppState.CurrentCompanyCode}; Code={record.Code}; Outcome=Loaded");
        }
        catch (Exception ex)
        {
            ReportCostCenterFailure(ex, ErrorCodes.CostCenterLoadFailed, "Load");
        }
        finally
        {
            _isLoadingRecord = false;
        }
    }

    private async Task DeleteCardAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtCode.Text))
        {
            ReportCostCenterFailure(
                new CostCenterCardException(ErrorCodes.CostCenterValidationFailed, "Enter a cost-center code first."),
                ErrorCodes.CostCenterDeleteFailed, "Delete");
            return;
        }

        var confirmation = MessageBox.Show(this, T("CostCenter.DeleteConfirmation"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            if (!await CostCenterCardSvc.DeleteAsync(_txtCode.Text))
            {
                ReportCostCenterFailure(
                    new CostCenterCardException(ErrorCodes.CostCenterDeleteRejected,
                        "The cost center was not found, or it has active child cost centers that must be moved or deleted first."),
                    ErrorCodes.CostCenterDeleteFailed, "Delete");
                return;
            }

            MessageBox.Show(this, T("CostCenter.Deleted"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            ResetCard();
        }
        catch (Exception ex)
        {
            ReportCostCenterFailure(ex, ErrorCodes.CostCenterDeleteFailed, "Delete");
        }
    }

    private void ReportCostCenterFailure(Exception exception, string fallbackCode, string operation)
    {
        var code = exception is CostCenterCardException costCenterException
            ? costCenterException.ErrorCode
            : fallbackCode;
        var definition = ErrorCatalog.Resolve(code);
        AppHost.Logging?.LogError("CostCenterCard",
            $"{definition.Code}: {definition.Name}; Operation={operation}; Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}",
            exception);
        AppHost.Logging?.LogOperation("CostCenterCard", "Failed",
            $"ErrorCode={definition.Code}; Operation={operation}; Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}");
        MessageBox.Show(this, $"[{definition.Code}] {definition.Message}\nLocation: {definition.Location}",
            Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("CostCenter.Title");
        RefreshLocalizedContent(Content as DependencyObject);
        UpdateBalances(0, 0);
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
        var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 6) };
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
}
