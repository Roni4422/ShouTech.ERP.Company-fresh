using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ShouTech.App.Configuration;
using ShouTech.App.Services;
using ShouTech.App.Localization;

namespace ShouTech.App.Views
{
    /// <summary>
    /// Lists soft-deleted companies and restores the selected one into DB + index.
    /// Opened from: Other Tools → Restore company.
    /// </summary>
    public partial class RestoreCoWin : Window
    {
        private readonly ObservableCollection<DeletedCompanyItem> _items = new();

        public RestoreCoWin()
        {
            InitializeComponent();
            VisuId.ApplyWindow(this);
            lstDeleted.ItemsSource = _items;
            ApplyLanguage();
            Loaded += async (_, _) => await ReloadAsync();
        }

        private void ApplyLanguage()
        {
            var lang = ShouTech.App.App.CurrentLanguageCode ?? "ar";
            Title = lang switch
            {
                "tr" => "Şirket geri yükleme",
                "fr" => "Restaurer une société",
                "en" => "Restore company",
                _ => "استعادة شركة"
            };
            txtTitle.Text = Title;
            txtHint.Text = lang switch
            {
                "tr" => "Silinmiş bir şirket seçin, ardından Geri yükle'ye basın.",
                "fr" => "Sélectionnez une société supprimée puis cliquez sur Restaurer.",
                "en" => "Select a soft-deleted company, then click Restore.",
                _ => "اختر شركة محذوفة منطقياً ثم اضغط استعادة."
            };
            btnRefresh.Content = lang switch { "tr" => "Yenile", "fr" => "Actualiser", "en" => "Refresh", _ => "تحديث" };
            btnRestore.Content = lang switch { "tr" => "Geri yükle", "fr" => "Restaurer", "en" => "Restore", _ => "استعادة" };
            btnClose.Content = lang switch { "tr" => "Kapat", "fr" => "Fermer", "en" => "Close", _ => "إغلاق" };
        }

        private async Task ReloadAsync()
        {
            _items.Clear();
            try
            {
                var deleted = await CoRegSvc.GetDeletedCompaniesAsync();
                foreach (var c in deleted.OrderBy(x => x.Name))
                {
                    _items.Add(new DeletedCompanyItem
                    {
                        Code = c.Code ?? string.Empty,
                        NameAr = c.Name ?? string.Empty,
                        NameEn = c.NameEn ?? c.Name ?? string.Empty
                    });
                }

                if (_items.Count == 0)
                {
                    var lang = ShouTech.App.App.CurrentLanguageCode ?? "ar";
                    MessageBox.Show(
                        lang switch
                        {
                            "tr" => "Geri yüklenecek kayıt yok.\nNot: Daha önce indeksten tamamen silinen şirketler burada görünmez.",
                            "fr" => "Aucun enregistrement à restaurer.\nNote: les sociétés purement retirées de l'index avant cette version n'apparaissent pas.",
                            "en" => "No deleted companies to restore.\nNote: companies fully purged from the index before this update cannot appear here.",
                            _ => "لا توجد شركات محذوفة للاستعادة.\nملاحظة: الشركات التي حُذفت من الفهرس بالكامل قبل هذا التحديث لا يمكن استرجاعها من هنا."
                        },
                        Title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogError("CompanyRestore", "RCO-LOAD-001: Failed to load deleted companies.", ex);
                AppHost.Logging?.LogOperation("CompanyRestore", "LoadFailed", "RCO-LOAD-001");
                MessageBox.Show(
                    ErrorCatalog.Get(ErrorCodes.DeletedCompanyLoadFailed),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
            => await ReloadAsync();

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (lstDeleted.SelectedItem is not DeletedCompanyItem item)
            {
                AppHost.Logging?.LogOperation("CompanyRestore", "ValidationFailed", "RCO-SELECT-001; no company selected.");
                MessageBox.Show(
                    ErrorCatalog.Get(ErrorCodes.DeletedCompanySelectionRequired),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                var ok = await CoRegSvc.RestoreCompanyAsync(item.Code);
                if (!ok)
                {
                    AppHost.Logging?.LogOperation("CompanyRestore", "RestoreRejected", "RCO-RESTORE-002; company service returned false.");
                    MessageBox.Show(
                        ErrorCatalog.Get(ErrorCodes.DeletedCompanyRestoreRejected),
                        Title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(
                    (ShouTech.App.App.CurrentLanguageCode ?? "en").StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                        ? $"تمت استعادة «{item.DisplayName}» وإعادة فهرستها."
                        : $"«{item.DisplayName}» was restored and re-indexed.",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await ReloadAsync();
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogError("CompanyRestore", "RCO-RESTORE-001: Failed to restore deleted company.", ex);
                AppHost.Logging?.LogOperation("CompanyRestore", "RestoreFailed", $"RCO-RESTORE-001; company={item.Code}");
                MessageBox.Show(
                    ErrorCatalog.Get(ErrorCodes.DeletedCompanyRestoreFailed),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }

    public sealed class DeletedCompanyItem
    {
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string NameEn { get; set; } = "";

        public string DisplayName
        {
            get
            {
                var lang = ShouTech.App.App.CurrentLanguageCode ?? "ar";
                if (lang.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(NameAr) ? (NameEn.Length > 0 ? NameEn : Code) : NameAr;
                return string.IsNullOrWhiteSpace(NameEn) ? (NameAr.Length > 0 ? NameAr : Code) : NameEn;
            }
        }
    }
}
