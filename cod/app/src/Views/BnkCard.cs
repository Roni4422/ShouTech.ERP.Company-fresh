using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using System.Threading.Tasks;
using ShouTech.App.Services;

namespace ShouTech.App.Views;

/// <summary>
/// Company-scoped bank card with database-backed save, load, and soft-delete operations.
/// </summary>
public sealed class BnkCard : BaseWin
{

    private readonly TextBox _txtCode = new() { Width = 120, Height = 26 };
    private readonly TextBox _txtName = new() { Width = 230, Height = 26 };
    private readonly TextBox _txtBranch = new() { Width = 150, Height = 26 };
    private readonly TextBox _txtAccNo = new() { Width = 180, Height = 26 };
    private readonly TextBox _txtIban = new() { Width = 260, Height = 26 };
    private readonly TextBox _txtCur = new() { Width = 70, Height = 26 };
    private readonly TextBox _txtGlAcc = new() { Width = 140, Height = 26 };

    private readonly TextBlock _lblBal = new();

    private readonly Button _btnNew = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnDelete = new();
    private bool _isLoadingRecord;

    public BnkCard()
    {
        Width = 680;
        Height = 400;
        MinWidth = 600;
        MinHeight = 350;
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

        metricsBorder.Child = _lblBal;
        _lblBal.FontWeight = FontWeights.Bold;

        DockPanel.SetDock(metricsBorder, Dock.Bottom);
        root.Children.Add(metricsBorder);

        // 3. Form Content
        var formPanel = new StackPanel { Margin = new Thickness(4) };

        var r1 = new WrapPanel();
        r1.Children.Add(Labeled("BankCard.Code", _txtCode));
        r1.Children.Add(Labeled("BankCard.Name", _txtName));
        r1.Children.Add(Labeled("BankCard.Branch", _txtBranch));
        formPanel.Children.Add(r1);

        var r2 = new WrapPanel();
        r2.Children.Add(Labeled("BankCard.AccountNo", _txtAccNo));
        r2.Children.Add(Labeled("BankCard.Iban", _txtIban));
        formPanel.Children.Add(r2);

        var r3 = new WrapPanel();
        r3.Children.Add(Labeled("BankCard.Currency", _txtCur));
        r3.Children.Add(Labeled("BankCard.GlAccount", _txtGlAcc));
        formPanel.Children.Add(r3);

        root.Children.Add(formPanel);
        Content = root;
    }

    private void ResetCard()
    {
        _txtCode.Text = "BNK-" + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        _txtName.Text = string.Empty;
        _txtBranch.Text = string.Empty;
        _txtAccNo.Text = string.Empty;
        _txtIban.Text = string.Empty;
        _txtGlAcc.Text = "121201";
        _lblBal.Text = $"{T("BankCard.Balance")}: 0.00 SAR";
    }


    private void UpdateBalanceUnavailable()
    {
        // No bank-ledger statement is connected to this card; never display fabricated balances.
        _lblBal.Text = $"{T("BankCard.Balance")}: —";
    }

    private BankCardRecord ReadCard() => new()
    {
        Code = _txtCode.Text,
        Name = _txtName.Text,
        Branch = _txtBranch.Text,
        AccountNumber = _txtAccNo.Text,
        Iban = _txtIban.Text,
        CurrencyCode = _txtCur.Text,
        GeneralLedgerAccountCode = _txtGlAcc.Text
    };

    private async Task SaveCardAsync()
    {
        try
        {
            var record = ReadCard();
            await BankCardSvc.SaveAsync(record);
            _txtCode.Text = record.Code.Trim();
            MessageBox.Show(this, T("BankCard.Saved"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ReportBankFailure(ex, ErrorCodes.BankSaveFailed, "Save");
        }
    }

    private async Task LoadCardAsync()
    {
        if (_isLoadingRecord) return;
        _isLoadingRecord = true;
        try
        {
            var record = await BankCardSvc.LoadByCodeAsync(_txtCode.Text);
            if (record is null)
            {
                var definition = ErrorCatalog.Resolve(ErrorCodes.BankNotFound);
                AppHost.Logging?.LogOperation("BankCard", "LoadNotFound",
                    $"Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}");
                MessageBox.Show(this, $"[{definition.Code}] {definition.Message}", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _txtCode.Text = record.Code;
            _txtName.Text = record.Name;
            _txtBranch.Text = record.Branch;
            _txtAccNo.Text = record.AccountNumber;
            _txtIban.Text = record.Iban;
            _txtCur.Text = record.CurrencyCode;
            _txtGlAcc.Text = record.GeneralLedgerAccountCode;
            UpdateBalanceUnavailable();
            AppHost.Logging?.LogOperation("BankCard", "Load",
                $"Company={AppState.CurrentCompanyCode}; Code={record.Code}; Outcome=Loaded");
        }
        catch (Exception ex)
        {
            ReportBankFailure(ex, ErrorCodes.BankLoadFailed, "Load");
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
            ReportBankFailure(new BankCardException(ErrorCodes.BankValidationFailed, "Enter a bank code first."),
                ErrorCodes.BankDeleteFailed, "Delete");
            return;
        }

        var confirmation = MessageBox.Show(this, T("BankCard.DeleteConfirmation"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        try
        {
            if (!await BankCardSvc.DeleteAsync(_txtCode.Text))
            {
                ReportBankFailure(new BankCardException(ErrorCodes.BankDeleteNotFound,
                    "The bank was not found in the active company or has already been deleted."),
                    ErrorCodes.BankDeleteFailed, "Delete");
                return;
            }
            MessageBox.Show(this, T("BankCard.Deleted"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
            ResetCard();
        }
        catch (Exception ex)
        {
            ReportBankFailure(ex, ErrorCodes.BankDeleteFailed, "Delete");
        }
    }

    private void ReportBankFailure(Exception exception, string fallbackCode, string operation)
    {
        var code = exception is BankCardException bankException ? bankException.ErrorCode : fallbackCode;
        var definition = ErrorCatalog.Resolve(code);
        AppHost.Logging?.LogError("BankCard",
            $"{definition.Code}: {definition.Name}; Operation={operation}; Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}",
            exception);
        AppHost.Logging?.LogOperation("BankCard", "Failed",
            $"ErrorCode={definition.Code}; Operation={operation}; Company={AppState.CurrentCompanyCode}; Code={_txtCode.Text.Trim()}");
        MessageBox.Show(this, $"[{definition.Code}] {definition.Message}\nLocation: {definition.Location}",
            Title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void ApplyLocalizedText()
    {
        Title = T("BankCard.Title");
        RefreshLocalizedContent(Content as DependencyObject);
        UpdateBalanceUnavailable();
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
