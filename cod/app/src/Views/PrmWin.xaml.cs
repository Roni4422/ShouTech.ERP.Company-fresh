using System;
using System.Windows;
using ShouTech.App.Services;
using ShouTech.App.Localization;
using ShouTech.App.ViewModels;

namespace ShouTech.App.Views;

public partial class PrmWin : Window
{
    private readonly PrmWinVM _vm;

    public PrmWin(string userId)
    {
        if (!AppHost.Permissions.Has("SYS.PERMISSIONS.MANAGE"))
            throw new UnauthorizedAccessException("You are not authorized to manage user permissions.");

        InitializeComponent();
        VisuId.ApplyWindow(this);
        _vm = new PrmWinVM(userId);
        DataContext = _vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            UiLoczr.ApplyToWindow(this);
            LangDirSv.ApplyToWindow(this, App.CurrentLanguageCode);
            await _vm.LoadAsync();
        }
        catch (Exception ex)
        {
            ReportFailure("LoadPermissions", ex);
            MessageBox.Show(this, FormatError("PRM-DB-001"), "ShouTech ERP", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnSave.IsEnabled = false;
            await _vm.SaveAsync();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ReportFailure("SavePermissions", ex);
            MessageBox.Show(this, FormatError("PRM-DB-001"), "ShouTech ERP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { BtnSave.IsEnabled = true; }
    }

    private static string FormatError(string code)
    {
        var definition = ErrorCatalog.Resolve(code);
        return $"[{definition.Code}] {definition.Message}{Environment.NewLine}Location: {definition.Location}";
    }

    private void ReportFailure(string operation, Exception exception)
    {
        const string code = "PRM-DB-001";
        var definition = ErrorCatalog.Resolve(code);
        var context = $"Operation={operation}; Window={GetType().Name}; User={UsrSession.Current?.Id ?? "(none)"}; Company={UsrSession.Current?.CompanyId ?? "(none)"}";
        AppHost.Logging?.LogError("PermissionsWindow", $"[{definition.Code}] {definition.Name} | {context}", exception);
        AppHost.Logging?.LogOperation("PermissionsWindow", "OperationFailed", $"Code={definition.Code} | {context}");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
