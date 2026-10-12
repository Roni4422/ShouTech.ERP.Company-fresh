using System;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using Npgsql;
using ShouTech.App.Configuration;
using ShouTech.App.Common;
using ShouTech.App.Localization;
using ShouTech.Domain.Interfaces;
using ShouTech.App.Services;
using WpfApp = System.Windows.Application;

namespace ShouTech.App.Views
{
    /// <summary>
    /// Startup: company picker before login (OK/Cancel).
    /// Management: opened from main shell (File → Open / Switch company)
    /// with side actions: Open, Edit, Delete, Reindex, Set Default, Close.
    /// </summary>
    public enum ComWinMode
    {
        Startup,
        Management
    }

    public partial class ComWin : Window, INotifyPropertyChanged
    {
        private readonly ComWinMode _mode;
        private ObservableCollection<CompanyItem> _companies = new();
        private ObservableCollection<CompanyItem> _filteredCompanies = new();
        private CompanyItem? _selectedCompany;

        public ObservableCollection<CompanyItem> Companies
        {
            get => _companies;
            set { _companies = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CompanyItem> FilteredCompanies
        {
            get => _filteredCompanies;
            set { _filteredCompanies = value; OnPropertyChanged(); }
        }

        public CompanyItem? SelectedCompany
        {
            get => _selectedCompany;
            set { _selectedCompany = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ComWin() : this(ComWinMode.Startup) { }

        public ComWin(ComWinMode mode)
        {
            _mode = mode;
            InitializeComponent();
            VisuId.ApplyWindow(this);
            ConfigureWindowChromeForMode();
            Resources["PreferCenterScreen"] = true;   
            DataContext = this;
            ApplyLayoutForMode();
            ApplyWindowDirection();
            ApplyButtonOrder();
            ApplyLocalizedMgmtLabels();

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (_, _) =>
            {
                ApplyLayoutForMode();
                ApplyWindowDirection();
                ApplyButtonOrder();
                ApplyLocalizedMgmtLabels();
                UiLoczr.ApplyToWindow(this);
            };
            Loaded += async (_, _) => await LoadCompaniesAsync();
        }

        private void ConfigureWindowChromeForMode()
        {
            if (_mode != ComWinMode.Management)
                return;

            // The startup picker keeps the shared borderless chrome.
            // Management mode restores the native Windows title bar.
            Style = null;
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = false;

            if (TryFindResource("SurfaceBrush") is System.Windows.Media.Brush surface)
            {
                Background = surface;
                rootSurfaceGrid.Background = surface;
            }
        }

        private void ApplyLayoutForMode()
        {
            var isMgmt = _mode == ComWinMode.Management;
            mgmtPanel.Visibility = isMgmt ? Visibility.Visible : Visibility.Collapsed;
            btnBar.Visibility = isMgmt ? Visibility.Collapsed : Visibility.Visible;

            if (!isMgmt)
            {
                mgmtPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Physical docking (ignore window RTL mirror): Arabic → visual Right, else Left.
            // rootDock stays LTR so Dock.Right is always the physical right edge.
            rootDock.FlowDirection = FlowDirection.LeftToRight;
            contentGrid.FlowDirection = IsArabicUi() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

            if (IsArabicUi())
            {
                DockPanel.SetDock(mgmtPanel, Dock.Right);
                mgmtPanel.Margin = new Thickness(12, 0, 0, 0);
                mgmtPanel.FlowDirection = FlowDirection.RightToLeft;
            }
            else
            {
                DockPanel.SetDock(mgmtPanel, Dock.Left);
                mgmtPanel.Margin = new Thickness(0, 0, 12, 0);
                mgmtPanel.FlowDirection = FlowDirection.LeftToRight;
            }
        }

		// Title bar coloring is now applied centrally from App.xaml.cs
		// (GlobalWindowLoaded / ApplyCustomTitleBarColor) for every window.

        private void ApplyLocalizedMgmtLabels()
        {
            var lang = ShouTech.App.App.CurrentLanguageCode ?? "en";
            txtMgmtTitle.Text = lang switch
            {
                "tr" => "Şirket yönetimi",
                "fr" => "Gestion des sociétés",
                "en" => "Company management",
                _ => "إدارة الشركات"
            };
            btnMgmtOpen.Content = lang switch { "tr" => "Aç", "fr" => "Ouvrir", "en" => "Open", _ => "فتح" };
            btnMgmtEdit.Content = lang switch { "tr" => "Düzenle", "fr" => "Modifier", "en" => "Edit", _ => "تعديل" };
            // Button content is a Grid; update label + menu item headers
            if (txtDeleteLabel != null)
            {
                txtDeleteLabel.Text = lang switch { "tr" => "Sil", "fr" => "Supprimer", "en" => "Delete", _ => "حذف" };
            }
            btnMgmtDelete.ToolTip = lang switch
            {
                "tr" => "Silme seçenekleri",
                "fr" => "Options de suppression",
                "en" => "Delete options",
                _ => "خيارات الحذف"
            };
            if (miDeleteFromIndex != null)
            {
                miDeleteFromIndex.Header = lang switch
                {
                    "tr" => "Pencereden sil",
                    "fr" => "Retirer de la liste",
                    "en" => "Remove from list",
                    _ => "حذف من النافذة"
                };
            }
            if (miDeleteFully != null)
            {
                miDeleteFully.Header = lang switch
                {
                    "tr" => "Kalıcı sil",
                    "fr" => "Suppression définitive",
                    "en" => "Delete permanently",
                    _ => "حذف كامل"
                };
            }
            btnMgmtReindex.Content = lang switch { "tr" => "Yeniden dizinle", "fr" => "Réindexer", "en" => "Reindex", _ => "فهرسة" };
            btnMgmtDefault.Content = lang switch { "tr" => "Varsayılan", "fr" => "Par défaut", "en" => "Set default", _ => "افتراضي" };
            btnMgmtClose.Content = lang switch { "tr" => "Kapat", "fr" => "Fermer", "en" => "Close", _ => "إغلاق" };
            txtTitle.Text = lang switch
            {
                "tr" => "Şirket seçin",
                "fr" => "Sélectionner une société",
                "en" => "Select company",
                _ => "اختر الشركة"
            };
            if (btnSetupWizard != null)
            {
                btnSetupWizard.Content = lang switch
                {
                    "tr" => "Yeni şirket oluştur",
                    "fr" => "Créer une société",
                    "en" => "Create company",
                    _ => "إنشاء شركة جديدة"
                };
                btnSetupWizard.ToolTip = btnSetupWizard.Content;
            }

            txtHint.Text = _mode == ComWinMode.Management
                ? lang switch
                {
                    "tr" => "Şirket seçin, sonra yan menüden işlem yapın.",
                    "fr" => "Sélectionnez une société puis utilisez le panneau latéral.",
                    "en" => "Select a company, then use the side actions.",
                    _ => "حدد شركة ثم استخدم أزرار الجانب."
                }
                : lang switch
                {
                    "tr" => "Devam etmek için bir şirket seçin.",
                    "fr" => "Sélectionnez une société pour continuer.",
                    "en" => "Select a company to continue.",
                    _ => "حدد شركة ثم تابع."
                };
        }

        private static bool IsArabicUi()
            => (ShouTech.App.App.CurrentLanguageCode ?? "en")
                .StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        private void ApplyWindowDirection()
        {
            try { LangDirSv.ApplyToWindow(this, ShouTech.App.App.CurrentLanguageCode); } catch { }
        }

        private void ApplyButtonOrder()
        {
            try
            {
                // Keep the action order stable while RTL/LTR changes text direction.
                // Setup = left, OK = center, Cancel = right.
                Grid.SetColumn(btnSetupWizard, 0);
                Grid.SetColumn(btnOk, 2);
                Grid.SetColumn(btnCancel, 4);
            }
            catch { }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (_mode == ComWinMode.Management)
                    BtnMgmtOpen_Click(sender, e);
                else
                    _ = ContinueToLoginAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SafeCloseOrShutdown();
            }
        }

        private async Task LoadCompaniesAsync()
        {
            try
            {
                var registry = new CoRegSvc();
                var realCompanies = await registry.GetCompaniesAsync();

                Companies.Clear();
                if (realCompanies != null)
                {
                    foreach (var c in realCompanies.Where(x => x.Enabled))
                    {
                        var nameAr = (c.Name ?? string.Empty).Trim();
                        var nameEn = (c.NameEn ?? c.Name ?? string.Empty).Trim();
                        Companies.Add(new CompanyItem
                        {
                            Id = c.Code ?? nameEn ?? nameAr,
                            Name = nameAr,
                            NameAr = nameAr,
                            NameEn = nameEn ?? string.Empty,
                            Code = c.Code ?? string.Empty
                        });
                    }
                }

                if (!Companies.Any())
                {
                    if (_mode == ComWinMode.Startup)
                    {
                        OpenSetupWizard();
                        return;
                    }

                    ShowNotice(
                        "No companies are indexed yet. Use Reindex or create a company.",
                        "لا توجد شركات مفهرسة. استخدم «فهرسة» أو أنشئ شركة.");
                }

                // Keep the last company used by the current user at the top of the list.
                // The code is persisted in MultiCompany.DefaultCompanyCode after a successful
                // company selection/login, so this ordering survives application restarts.
                var lastCompanyCode = AppCfgSvc.Current?.MultiCompany?.DefaultCompanyCode;
                if (!string.IsNullOrWhiteSpace(lastCompanyCode))
                {
                    var lastCompany = Companies.FirstOrDefault(c =>
                        string.Equals(c.Code, lastCompanyCode, StringComparison.OrdinalIgnoreCase));
                    if (lastCompany != null)
                    {
                        Companies.Move(Companies.IndexOf(lastCompany), 0);
                    }
                }

                ApplyFilter(txtSearch.Text);
                SelectedCompany = Companies.FirstOrDefault();
                lstCompanies.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Loc.Get("CompanySelection.LoadErrorMessage", "Failed to load companies")}:\n{ex.Message}",
                    Loc.Get("Application.Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                if (_mode == ComWinMode.Startup)
                {
                    try
                    {
                        var pref = UsrPref.Load();
                        if (!pref.CompanySetupComplete)
                            OpenSetupWizard();
                    }
                    catch { OpenSetupWizard(); }
                }
            }
        }

        private void OpenSetupWizard()
        {
            var wizard = new StpWiz(StpWizMode.NewCompany, this);
            var accepted = wizard.ShowDialog() == true && wizard.CompletedSuccessfully;
            if (!accepted)
            {
                if (_mode == ComWinMode.Startup)
                {
                    try { WpfApp.Current?.Shutdown(); } catch { }
                }
                return;
            }

            try
            {
                var pref = UsrPref.Load();
                pref.CompanySetupComplete = true;
                pref.Save();
            }
            catch { }

            _ = LoadCompaniesAsync();
        }

        private void BtnSetupWizard_Click(object sender, RoutedEventArgs e)
        {
            if (_mode != ComWinMode.Startup)
                return;

            OpenSetupWizard();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
            => ApplyFilter(txtSearch.Text);

        private void ApplyFilter(string? query)
        {
            var q = (query ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(q))
            {
                FilteredCompanies = new ObservableCollection<CompanyItem>(Companies);
                return;
            }

            FilteredCompanies = new ObservableCollection<CompanyItem>(
                Companies.Where(c =>
                    (c.Code?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.DisplayName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.NameAr?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (c.NameEn?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        private void LstCompanies_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_mode == ComWinMode.Management)
                BtnMgmtOpen_Click(sender, e);
            else
                _ = ContinueToLoginAsync();
        }


        /// <summary>
        /// Startup mode used to call Application.Shutdown on cancel — that kills the shell
        /// when ComWin was opened after File→New. Only shut down when no main shell exists.
        /// </summary>
        private void SafeCloseOrShutdown()
        {
            if (_mode == ComWinMode.Management)
            {
                Close();
                return;
            }

            var hasShell = false;
            try
            {
                foreach (Window w in WpfApp.Current.Windows)
                {
                    // MWin is the main shell; never treat ComWin/StpWiz/LogWin as shell.
                    if (w is MWin)
                    {
                        hasShell = true;
                        break;
                    }
                }
            }
            catch { }

            if (hasShell)
            {
                Close();
                return;
            }

            try { WpfApp.Current.Shutdown(); } catch { Close(); }
        }

        private async void BtnContinue_Click(object sender, RoutedEventArgs e) => await ContinueToLoginAsync();

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SafeCloseOrShutdown();
        }

        // -------- Management actions --------

        private async void BtnMgmtOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureSelection()) return;

            try
            {
                await SwitchToCompanyAsync(SelectedCompany!);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    IsArabicUi() ? "تعذر فتح الشركة" : "Unable to open company",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task SwitchToCompanyAsync(CompanyItem company)
        {
            var shell = WpfApp.Current?.Windows.OfType<MWin>().FirstOrDefault();
            var currentCode = AppState.CurrentCompanyCode;

            if (shell != null && string.Equals(currentCode, company.Code, StringComparison.OrdinalIgnoreCase))
            {
                ShowNotice(
                    IsArabicUi() ? "هذه الشركة هي الشركة النشطة حاليًا." : "This company is already active.",
                    IsArabicUi() ? "هذه الشركة هي الشركة النشطة حاليًا." : "This company is already active.");
                return;
            }

            if (shell != null)
            {
                var answer = MessageBox.Show(
                    IsArabicUi()
                        ? "سيتم إنهاء جلسة الشركة الحالية وإغلاق جميع النوافذ والعمليات المفتوحة التابعة لها، ثم فتح تسجيل دخول جديد للشركة المحددة. ستبقى النافذة الرئيسية مفتوحة ولكن معطلة حتى نجاح تسجيل الدخول. هل تريد المتابعة؟"
                        : "The current company session will be ended and all open company windows and operations will be closed. A new login will then be required for the selected company. The main window will remain open but disabled until sign-in succeeds. Do you want to continue?",
                    IsArabicUi() ? "تبديل الشركة" : "Switch company",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes)
                    return;
            }

            var all = await CoLocalStore.LoadAllAsync();
            var record = all.FirstOrDefault(c =>
                string.Equals(c.Code, company.Code, StringComparison.OrdinalIgnoreCase));

            if (record == null)
                throw new InvalidOperationException(IsArabicUi()
                    ? "تعذر العثور على بيانات الشركة المحلية لفتحها."
                    : "The local company record required to open this company was not found.");


            if (shell != null)
            {
                AppState.InactiveMainWindow = shell;

                try
                {
                    // Freeze the shell visually and functionally. Only File/Open remains available.
                    if (shell.DataContext is ViewModels.MainShVM vm)
                        vm.FreezeForCompanySwitch();

                    CloseSecondaryWindowsExceptShell();
                    await AppHost.Auth.LogoutAsync();
                    AppState.CurrentUser = null;

                    // Bind the runtime to the selected company before displaying its login.
                    ConfigureRuntimeDatabase(record);
                    AppHost.RebindDatabaseContext();
                    AppState.SetCompany(record.Code, record.Name, record.NameEn);
                    var app = WpfApp.Current;
                    if (app != null)
                        app.Properties["SelectedCompany"] = BuildCompanyItem(record);

                    if (AppCfgSvc.Current?.MultiCompany != null)
                    {
                        AppCfgSvc.Current.MultiCompany.DefaultCompanyCode = record.Code;
                        try { AppCfgSvc.Save(); } catch { }
                    }

                    await ShowCompanyLoginAsync(shell);
                }
                catch
                {
                    // Do not silently restore the old company/session. The shell remains frozen
                    // and File → Open is the only allowed path to another company/login.
                    throw;
                }
            }
            else
            {
                ConfigureRuntimeDatabase(record);
                AppHost.RebindDatabaseContext();
                AppState.SetCompany(record.Code, record.Name, record.NameEn);
                var app = WpfApp.Current;
                if (app != null)
                    app.Properties["SelectedCompany"] = BuildCompanyItem(record);
                await ContinueToLoginAsync();
            }

            Close();
        }

        internal static CompanyItem BuildCompanyItem(StoredCoRec record) => new()
        {
            Id = record.Code,
            Code = record.Code,
            Name = record.Name,
            NameAr = record.Name,
            NameEn = record.NameEn
        };

        internal static Task ShowCompanyLoginAsync(Window shellOwner)
        {
            var login = new LogWin
            {
                Owner = shellOwner,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            // Company switching is a controlled session handoff. Keep the login dialog
            // in the foreground until the user either authenticates successfully or
            // explicitly cancels; do not allow ComWin to finish the handoff early.
            login.ShowDialog();
            return Task.CompletedTask;
        }


        private static void ConfigureRuntimeDatabase(StoredCoRec record)
        {
            var settings = AppCfgSvc.Current;
            settings.ConnectionStrings ??= new System.Collections.Generic.Dictionary<string, ConnectionStringSettings>(StringComparer.OrdinalIgnoreCase);
            if (!settings.ConnectionStrings.TryGetValue("Default", out var defaultConnection) || defaultConnection == null)
            {
                defaultConnection = new ConnectionStringSettings();
                settings.ConnectionStrings["Default"] = defaultConnection;
            }

            var provider = string.IsNullOrWhiteSpace(record.DatabaseProvider)
                ? DatabaseProviderNames.Sqlite
                : DatabaseProviderNames.Normalize(record.DatabaseProvider);

            defaultConnection.Provider = provider;
            settings.Database ??= new DatabaseSettings();
            settings.Database.DefaultProvider = provider;

            if (DatabaseRuntime.IsSqlite(provider))
            {
                if (string.IsNullOrWhiteSpace(record.DbPath))
                    throw new InvalidOperationException("مسار قاعدة بيانات SQLite للشركة غير محدد.");

                defaultConnection.ConnectionString =
                    DatabaseRuntime.NormalizeSqliteConnectionString(record.DbPath);				
				AppState.SetDatabase(
					System.IO.Path.GetFileName(record.DbPath));
					
                return;
            }

            if (DatabaseRuntime.IsPostgreSql(provider))
            {
                var current = defaultConnection.ConnectionString;
                if (string.IsNullOrWhiteSpace(current))
                    throw new InvalidOperationException("اتصال PostgreSQL الحالي غير متاح.");

                var builder = new NpgsqlConnectionStringBuilder(current);
                if (!string.IsNullOrWhiteSpace(record.DbPath))
                {
                    var dbName = Path.GetFileNameWithoutExtension(record.DbPath.TrimEnd('\\', '/'));
                    if (!string.IsNullOrWhiteSpace(dbName))
                        builder.Database = dbName;
                }
                defaultConnection.ConnectionString = builder.ConnectionString;
                return;
            }

            throw new InvalidOperationException($"قاعدة البيانات غير مدعومة: {provider}");
        }

        private void CloseSecondaryWindowsExceptShell()
        {
            foreach (Window window in WpfApp.Current.Windows.Cast<Window>().ToList())
            {
                if (ReferenceEquals(window, this) || window is MWin)
                    continue;

                try { window.Close(); } catch { }
            }
        }

        private async void BtnMgmtEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureSelection()) return;
            await EnterEditModeAsync(SelectedCompany!);
        }

        private async Task EnterEditModeAsync(CompanyItem item)
        {
            try
            {
                // Prefer full local record; fall back to list item names.
                StoredCoRec? rec = null;
                var all = await CoLocalStore.LoadAsync();
                rec = all.FirstOrDefault(c =>
                    string.Equals(c.Code, item.Code, StringComparison.OrdinalIgnoreCase));

                txtEditNameAr.Text = rec?.Name ?? item.NameAr ?? item.Name ?? string.Empty;
                txtEditNameEn.Text = rec?.NameEn ?? item.NameEn ?? item.Name ?? string.Empty;
                txtEditTax.Text = rec?.TaxNumber ?? rec?.License ?? string.Empty;
                txtEditEmail.Text = rec?.Email ?? string.Empty;
                txtEditPhone.Text = rec?.Phone ?? string.Empty;
                txtEditMobile.Text = rec?.Mobile ?? string.Empty;
                txtEditAddress.Text = rec?.AddressLine1 ?? string.Empty;
                txtEditCity.Text = rec?.City ?? string.Empty;
                txtEditCountry.Text = rec?.Country ?? string.Empty;
                txtEditCurrency.Text = string.IsNullOrWhiteSpace(rec?.CurrencyCode) ? "SYP" : rec!.CurrencyCode;

                ApplyEditModeLabels();
                listPanel.Visibility = Visibility.Collapsed;
                editPanel.Visibility = Visibility.Visible;
                mgmtPanel.IsEnabled = false;
                txtSearch.IsEnabled = false;

                var lang = ShouTech.App.App.CurrentLanguageCode ?? "ar";
                txtTitle.Text = lang switch
                {
                    "tr" => "Şirket bilgilerini düzenle",
                    "fr" => "Modifier les informations de la société",
                    "en" => "Edit company details",
                    _ => "تعديل بيانات الشركة"
                };
                txtHint.Text = lang switch
                {
                    "tr" => "Değişiklikleri kaydedin veya iptal edin.",
                    "fr" => "Enregistrez ou annulez les modifications.",
                    "en" => "Save changes or cancel to return to the list.",
                    _ => "عدّل البيانات ثم احفظ، أو ألغِ للعودة للقائمة."
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Edit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitEditMode()
        {
            editPanel.Visibility = Visibility.Collapsed;
            listPanel.Visibility = Visibility.Visible;
            mgmtPanel.IsEnabled = true;
            txtSearch.IsEnabled = true;
            ApplyLocalizedMgmtLabels();
        }

        private void ApplyEditModeLabels()
        {
            var lang = ShouTech.App.App.CurrentLanguageCode ?? "ar";
            lblEditNameAr.Text = lang switch { "tr" => "Ad (AR)", "fr" => "Nom (AR)", "en" => "Name (AR)", _ => "الاسم (عربي)" };
            lblEditNameEn.Text = lang switch { "tr" => "Ad (EN)", "fr" => "Nom (EN)", "en" => "Name (EN)", _ => "الاسم (إنجليزي)" };
            lblEditTax.Text = lang switch { "tr" => "Vergi / Sicil", "fr" => "Taxe / RC", "en" => "Tax / Registration", _ => "الرقم الضريبي / السجل" };
            lblEditEmail.Text = lang switch { "tr" => "E-posta", "fr" => "E-mail", "en" => "Email", _ => "البريد" };
            lblEditPhone.Text = lang switch { "tr" => "Telefon", "fr" => "Téléphone", "en" => "Phone", _ => "الهاتف" };
            lblEditMobile.Text = lang switch { "tr" => "Cep", "fr" => "Mobile", "en" => "Mobile", _ => "الجوال" };
            lblEditAddress.Text = lang switch { "tr" => "Adres", "fr" => "Adresse", "en" => "Address", _ => "العنوان" };
            lblEditCity.Text = lang switch { "tr" => "Şehir", "fr" => "Ville", "en" => "City", _ => "المدينة" };
            lblEditCountry.Text = lang switch { "tr" => "Ülke", "fr" => "Pays", "en" => "Country", _ => "الدولة" };
            lblEditCurrency.Text = lang switch { "tr" => "Para birimi", "fr" => "Devise", "en" => "Currency", _ => "العملة" };
            btnEditSave.Content = lang switch { "tr" => "Kaydet", "fr" => "Enregistrer", "en" => "Save", _ => "حفظ" };
            btnEditCancel.Content = lang switch { "tr" => "İptal", "fr" => "Annuler", "en" => "Cancel", _ => "إلغاء" };
        }

        private async void BtnEditSave_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedCompany == null)
            {
                ExitEditMode();
                return;
            }

            var nameAr = (txtEditNameAr.Text ?? string.Empty).Trim();
            var nameEn = (txtEditNameEn.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nameAr) && string.IsNullOrWhiteSpace(nameEn))
            {
                ShowNotice("Company name is required.", "اسم الشركة مطلوب.");
                return;
            }

            if (string.IsNullOrWhiteSpace(nameAr))
                nameAr = nameEn;
            if (string.IsNullOrWhiteSpace(nameEn))
                nameEn = nameAr;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var profile = new StoredCoRec
                {
                    Code = SelectedCompany.Code,
                    Name = nameAr,
                    NameEn = nameEn ?? string.Empty,
                    TaxNumber = (txtEditTax.Text ?? string.Empty).Trim(),
                    License = (txtEditTax.Text ?? string.Empty).Trim(),
                    Email = (txtEditEmail.Text ?? string.Empty).Trim(),
                    Phone = (txtEditPhone.Text ?? string.Empty).Trim(),
                    Mobile = (txtEditMobile.Text ?? string.Empty).Trim(),
                    AddressLine1 = (txtEditAddress.Text ?? string.Empty).Trim(),
                    City = (txtEditCity.Text ?? string.Empty).Trim(),
                    Country = (txtEditCountry.Text ?? string.Empty).Trim(),
                    CurrencyCode = string.IsNullOrWhiteSpace(txtEditCurrency.Text) ? "SYP" : txtEditCurrency.Text.Trim(),
                    Enabled = true
                };

                await CoRegSvc.UpdateCompanyProfileAsync(profile);

                ShowNotice("Company details saved.", "تم حفظ بيانات الشركة.");
                ExitEditMode();
                await LoadCompaniesAsync();

                // Reselect same company by code
                SelectedCompany = Companies.FirstOrDefault(c =>
                    string.Equals(c.Code, profile.Code, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnEditCancel_Click(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }

        private void BtnMgmtDeleteMenu_Click(object sender, RoutedEventArgs e)
        {
            OpenDeleteMenu();
        }

        /// <summary>
        /// Opens native ContextMenu children under Delete (same UX as cascading menus).
        /// </summary>
        private void OpenDeleteMenu()
        {
            if (!EnsureSelection())
                return;

            var menu = btnMgmtDelete.ContextMenu;
            if (menu == null)
                return;

            menu.PlacementTarget = btnMgmtDelete;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private async void BtnDeleteFromIndex_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureSelection())
                return;

            try
            {
                await CoLocalStore.RemoveFromIndexAsync(SelectedCompany!.Code);
                ShowNotice("Company removed from list.", "تم حذف الشركة من النافذة (الفهرس).");
                await LoadCompaniesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDeleteFully_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureSelection())
                return;

            var lang = ShouTech.App.App.CurrentLanguageCode ?? "en";
            var title = lang switch
            {
                "tr" => "Kalıcı silme",
                "fr" => "Suppression définitive",
                "en" => "Permanent delete",
                _ => "حذف نهائي"
            };
            var msg = lang switch
            {
                "tr" => $"«{SelectedCompany!.DisplayName}» kalıcı olarak silinecek.\nBu işlem geri alınamaz.\n\nDevam edilsin mi?",
                "fr" => $"« {SelectedCompany!.DisplayName} » sera supprimée définitivement.\nCette action est irréversible.\n\nContinuer ?",
                "en" => $"«{SelectedCompany!.DisplayName}» will be permanently deleted.\nThis cannot be undone.\n\nContinue?",
                _ => $"سيتم حذف الشركة «{SelectedCompany!.DisplayName}» نهائياً من قاعدة البيانات والفهرس.\nلا يمكن استرجاعها.\n\nهل تريد المتابعة؟"
            };

            var confirm = MessageBox.Show(msg, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
                return;

            try
            {
                var ok = await CoRegSvc.DeleteCompanyFullyAsync(SelectedCompany!.Code);
                if (!ok)
                    await CoLocalStore.RemoveFromIndexAsync(SelectedCompany.Code);

                ShowNotice("Company permanently deleted.", "تم حذف الشركة نهائياً.");
                await LoadCompaniesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnMgmtReindex_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var count = await CoRegSvc.RebuildCompanyIndexAsync();
                await LoadCompaniesAsync();
                ShowNotice(
                    $"Company index rebuilt. Entries: {count}",
                    $"تمت إعادة فهرسة الشركات. العدد: {count}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Reindex", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnMgmtDefault_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureSelection()) return;

            try
            {
                if (AppCfgSvc.Current.MultiCompany == null)
                    AppCfgSvc.Current.MultiCompany = new MultiCompanySettings();

                AppCfgSvc.Current.MultiCompany.DefaultCompanyCode = SelectedCompany!.Code;
                AppState.SetCompany(SelectedCompany.Code, SelectedCompany.NameAr ?? SelectedCompany.Name, SelectedCompany.NameEn);
                try { AppCfgSvc.Save(); } catch { }

                ShowNotice(
                    $"Default company: {SelectedCompany.DisplayName} ({SelectedCompany.Code})",
                    $"الشركة الافتراضية: {SelectedCompany.DisplayName} ({SelectedCompany.Code})");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Default", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnMgmtClose_Click(object sender, RoutedEventArgs e) => Close();

        private bool EnsureSelection()
        {
            if (SelectedCompany != null)
                return true;

            ShowNotice("Select a company first.", "يرجى اختيار شركة أولاً.");
            return false;
        }

        private async Task ContinueToLoginAsync()
        {
            if (SelectedCompany == null)
            {
                MessageBox.Show(
                    Loc.Get("CompanySelection.SelectCompanyFirst", "Select a company first."),
                    Loc.Get("Application.Warning", "Warning"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                var all = await CoLocalStore.LoadAllAsync();
                var record = all.FirstOrDefault(c =>
                    string.Equals(c.Code, SelectedCompany.Code, StringComparison.OrdinalIgnoreCase) && c.Enabled);

                if (record == null)
                    throw new InvalidOperationException(IsArabicUi()
                        ? "تعذر العثور على سجل الشركة المحلي."
                        : "The local company record could not be found.");

                await VerifyCompanyDatabaseAsync(record);
                ConfigureRuntimeDatabase(record);
                AppHost.RebindDatabaseContext();

                WpfApp.Current.Properties["SelectedCompany"] = SelectedCompany;
                AppState.SetCompany(record.Code, record.Name, record.NameEn);
                WpfApp.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

                var loginWindow = new LogWin();
                WpfApp.Current.MainWindow = loginWindow;
                loginWindow.Show();
                loginWindow.Activate();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Loc.Get("CompanySelection.OpenLoginFailed", "Failed to open login")}:\n{ex.Message}",
                    Loc.Get("Application.Error", "Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static async Task VerifyCompanyDatabaseAsync(StoredCoRec record)
        {
            var provider = DatabaseProviderNames.Normalize(record.DatabaseProvider);
            if (DatabaseRuntime.IsSqlite(provider))
            {
                if (string.IsNullOrWhiteSpace(record.DbPath))
                    throw new InvalidOperationException("مسار قاعدة بيانات الشركة غير محدد.");

                var connectionString = DatabaseRuntime.NormalizeSqliteConnectionString(record.DbPath);
                var dataSource = DatabaseRuntime.GetSqliteDataSource(connectionString);
                if (string.IsNullOrWhiteSpace(dataSource) || !File.Exists(dataSource))
                    throw new FileNotFoundException("قاعدة بيانات الشركة غير موجودة.", dataSource);

                await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='companies'";
                var count = Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
                if (count != 1)
                    throw new InvalidDataException("ملف قاعدة بيانات الشركة غير مهيأ بشكل صحيح.");
                return;
            }

            if (DatabaseRuntime.IsPostgreSql(provider))
            {
                if (string.IsNullOrWhiteSpace(record.DbPath))
                    throw new InvalidOperationException("اسم قاعدة بيانات PostgreSQL غير محدد.");
                return;
            }

            throw new InvalidOperationException($"قاعدة البيانات غير مدعومة: {record.DatabaseProvider}");
        }

        private static void ShowNotice(string english, string arabic)
        {
            var isArabic = IsArabicUi();
            MessageBox.Show(
                isArabic ? arabic : english,
                isArabic ? "تنبيه" : "Notice",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class CompanyItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Code { get; set; } = "";

        public string GetDisplayName(string? languageCode)
        {
            var lang = (languageCode ?? "en").Trim();
            string pick;
            if (lang.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
            {
                pick = FirstNonEmpty(NameAr, Name, NameEn);
            }
            else
            {
                pick = FirstNonEmpty(NameEn, Name, NameAr);
            }

            pick = StripLeadingCode(pick);
            // Never show internal codes (004, 005…) as the list caption.
            if (string.IsNullOrWhiteSpace(pick) || IsMostlyCode(pick))
                return (languageCode ?? "").StartsWith("ar", StringComparison.OrdinalIgnoreCase)
                    ? "شركة بدون اسم"
                    : "Unnamed company";
            return pick;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return string.Empty;
        }

        /// <summary>
        /// Removes patterns like "004 - ", "005-", "004 " from stored display names.
        /// </summary>
        private static string StripLeadingCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var s = value.Trim();
            // 004 - Name | 004- Name | 004 Name
            if (s.Length >= 3 && char.IsDigit(s[0]))
            {
                var i = 0;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == ' '))
                    i++;
                while (i < s.Length && (s[i] == '-' || s[i] == '�' || s[i] == ':' || s[i] == ' '))
                    i++;
                if (i > 0 && i < s.Length)
                    s = s[i..].Trim();
            }
            return s;
        }

        private static bool IsMostlyCode(string value)
        {
            var t = value.Trim();
            if (t.Length == 0) return true;
            // pure numeric code like 004 / 12
            return t.All(c => char.IsDigit(c) || char.IsWhiteSpace(c));
        }

        public string DisplayName => GetDisplayName(ShouTech.App.App.CurrentLanguageCode);

        public override string ToString() => DisplayName;
    }
}
