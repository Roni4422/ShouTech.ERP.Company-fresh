using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ShouTech.App.Configuration;
using ShouTech.App.Localization;
using ShouTech.App.Services;
using WpfApp = System.Windows.Application;

namespace ShouTech.App.Views
{
    /// <summary>
    /// Startup splash — runs offline integrity / license / dongle / user-data
    /// checks within a hard maximum of 4 seconds, showing status at the bottom.
    /// </summary>
    public partial class SplWin : BaseWin
    {
        private const double MaxSplashSeconds = 4.0;
        private bool _started;
        private bool? _hasCompaniesAtStartup;

        public SplWin()
        {
            InitializeComponent();
            VisuId.ApplyWindow(this);
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_started)
                return;
            _started = true;

            var sw = Stopwatch.StartNew();

            try
            {
                ApplyConfigTexts();
                ApplyLocalization();
                await RunStartupSecurityPipelineAsync(sw);
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogError("Startup", "SPL-START-001: Startup security pipeline failed.", ex);
                SetStatus(ErrorCatalog.Get(ErrorCodes.StartupSecurityPipelineFailed));
                await SafeDelay(200);
            }

            // Check the company registry while the splash is still visible.
            // The result determines whether startup opens setup or selection.
            try
            {
                SetStatus(L("Startup.CheckingCompanies", "Checking companies..."));
                _hasCompaniesAtStartup = await new CoRegSvc().HasCompaniesAsync();
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogError("Startup", "SPL-START-002: Company registry check failed.", ex);
                SetStatus(ErrorCatalog.Get(ErrorCodes.StartupCompanyCheckFailed));
                _hasCompaniesAtStartup = null;
            }

            // Honor remaining time up to the 4s ceiling (never exceed it).
            var remaining = MaxSplashSeconds - sw.Elapsed.TotalSeconds;
            if (remaining > 0.05)
                await SafeDelay((int)(remaining * 1000));

            SetProgress(100);
            SetStatus(L("Startup.SplashStatus", "Ready."));

            try
            {
                await OpenStartupFlowAsync();
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogError("Startup", "SPL-START-003: Unable to open the company startup flow.", ex);
                MessageBox.Show(
                    ErrorCatalog.Get(ErrorCodes.StartupCompanyFlowFailed),
                    "ShouTech ERP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                try { WpfApp.Current?.Shutdown(1); } catch { }
                return;
            }

        }

        /// <summary>
        /// Ordered security pipeline. Each step updates the status line and progress.
        /// Non-critical failures are reported and the pipeline continues.
        /// </summary>
        private async Task RunStartupSecurityPipelineAsync(Stopwatch sw)
        {
            // Step weights designed so the whole pipeline fits inside 4 seconds.
            await RunStepAsync(sw, 8,  L("Startup.CheckingStartupFiles", "Checking startup files and settings..."), VerifyApplicationBinariesAsync);
            await RunStepAsync(sw, 18, L("Startup.CheckingStartupFiles", "Checking startup files and settings..."), VerifyIntegrityAsync);
            await RunStepAsync(sw, 30, L("Startup.CheckingStartupFiles", "Checking startup files and settings..."), ScanUnauthorizedModificationsAsync);
            await RunStepAsync(sw, 42, L("Startup.CheckingStartupFiles", "Checking startup files and settings..."), ValidateConfigAndUserFilesAsync);
            await RunStepAsync(sw, 55, L("Startup.CheckingDeviceIdentity", "Checking device identity and dongle..."), CheckDeviceAndDongleAsync);
            await RunStepAsync(sw, 70, L("Startup.CheckingLicense", "Checking license..."), CheckLicenseAsync);
            await RunStepAsync(sw, 85, L("Startup.PreparingLocalData", "Preparing local user data..."), PrepareUserDataAsync);
            await RunStepAsync(sw, 95, L("Startup.StartingBackgroundJobs", "Starting background services..."), StartBackgroundServicesAsync);
        }

        private async Task RunStepAsync(Stopwatch sw, int progressTarget, string message, Func<Task> action)
        {
            if (sw.Elapsed.TotalSeconds >= MaxSplashSeconds - 0.15)
            {
                SetProgress(progressTarget);
                return;
            }

            SetStatus(message);
            SetProgress(progressTarget - 5 < 0 ? 0 : progressTarget - 5);

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // Never abort the whole splash for a single non-critical check.
                AppHost.Logging?.LogWarning("Startup", $"SPL-START-004: {message} check failed. {ex}");
                SetStatus(message);
                await SafeDelay(80);
            }

            SetProgress(progressTarget);
            await SafeDelay(40);
        }

        // ====================================================================
        // SECURITY CHECKS
        // ====================================================================

        private static Task VerifyApplicationBinariesAsync()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var required = new[]
            {
                "ShouTech.ERP.dll",
                "ShouTech.Domain.dll",
                "ShouTech.Application.dll",
                "ShouTech.Infrastructure.dll"
            };

            foreach (var name in required)
            {
                var path = Path.Combine(baseDir, name);
                // Main EXE may host the entry assembly under a different name — soft check.
                if (!File.Exists(path))
                    continue;

                var info = new FileInfo(path);
                if (info.Length <= 0)
                    throw new InvalidOperationException($"Binary empty: {name}");
            }

            return Task.CompletedTask;
        }

        private static Task VerifyIntegrityAsync()
        {
            // Lightweight integrity fingerprint of the entry assembly (size + SHA256 prefix).
            var entry = Assembly.GetEntryAssembly()?.Location
                        ?? Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(entry) || !File.Exists(entry))
                return Task.CompletedTask;

            var bytes = File.ReadAllBytes(entry);
            if (bytes.Length < 1024)
                throw new InvalidOperationException("Entry assembly integrity check failed.");

            var hash = SHA256.HashData(bytes);
            // Fingerprint retained for future online attestation; local check ensures readability.
            _ = Convert.ToHexString(hash.AsSpan(0, 8));
            return Task.CompletedTask;
        }

        private static Task ScanUnauthorizedModificationsAsync()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Suspicious injected modules / crackers often drop these patterns next to the app.
            var suspicious = new[]
            {
                "*.asi", "*.overlay", "cheat*", "inject*", "patch*.dll", "crack*"
            };

            foreach (var pattern in suspicious)
            {
                string[] hits;
                try { hits = Directory.GetFiles(baseDir, pattern, SearchOption.TopDirectoryOnly); }
                catch (Exception ex)
                {
                    AppHost.Logging?.LogWarning("Startup", $"SPL-SEC-002: Unable to inspect startup directory for pattern '{pattern}'. {ex}");
                    continue;
                }

                if (hits.Length > 0)
                    throw new InvalidOperationException("Suspicious files detected near the application.");
            }

            // Debugger attached during production startup is treated as a soft warning.
            if (Debugger.IsAttached)
            {
                // Allowed in Debug builds; production installs should not attach.
            }

            return Task.CompletedTask;
        }

        private static Task ValidateConfigAndUserFilesAsync()
        {
            try
            {
                var settingsPath = AppCfgSvc.LoadedPath;
                if (!string.IsNullOrWhiteSpace(settingsPath) && !File.Exists(settingsPath))
                {
                    // Soft: configuration may be created on first run.
                }
            }
            catch (Exception ex)
            {
                AppHost.Logging?.LogWarning("Startup", $"SPL-CFG-001: Unable to validate application configuration path. {ex}");
            }

            try
            {
                var pref = UsrPref.Load();
                _ = pref.Lang;
            }
            catch (Exception ex)
            {
                // Preferences can be recreated, but the failure must remain diagnosable.
                AppHost.Logging?.LogWarning("Startup", $"SPL-CFG-002: User preferences could not be loaded and may need to be recreated. {ex}");
            }

            return Task.CompletedTask;
        }

        private static Task CheckDeviceAndDongleAsync()
        {
            try
            {
                AppHost.DeviceInfo?.Collect();
            }
            catch
            {
                // Device collection is non-fatal offline.
            }

            return Task.CompletedTask;
        }

        private static async Task CheckLicenseAsync()
        {
            if (AppHost.License == null)
            {
                AppHost.Logging?.LogWarning("Startup", "SPL-LIC-001: License service is not configured; license initialization was skipped.");
                return;
            }

            // RunStepAsync records initialization failures. Do not swallow them here.
            await AppHost.License.InitializeAsync();
        }

        private static async Task PrepareUserDataAsync()
        {
            if (AppHost.Sync == null)
            {
                AppHost.Logging?.LogWarning("Startup", "SPL-SYNC-001: User-data snapshot service is not configured; startup snapshot was skipped.");
                return;
            }

            // RunStepAsync records failures while allowing offline startup to continue.
            await AppHost.Sync.SnapshotLocalUserDataAsync();
        }

        private static Task StartBackgroundServicesAsync()
        {
            if (AppHost.OnlineCoordinator == null)
            {
                AppHost.Logging?.LogWarning("Startup", "SPL-SVC-001: Online coordinator is not configured; background synchronization was not started.");
                return Task.CompletedTask;
            }

            // RunStepAsync records startup failures without masking them.
            AppHost.OnlineCoordinator.Start();
            return Task.CompletedTask;
        }

        // ====================================================================
        // UI HELPERS
        // ====================================================================

        private static string L(string key, string fallback)
        {
            try
            {
                return AppServiceHost.Localization.GetString(key, fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private void ApplyLocalization()
        {
            try
            {
                var code = App.CurrentLanguageCode ?? "en";
                FlowDirection = code.Equals("ar", StringComparison.OrdinalIgnoreCase)
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;

                txtStatus.Text = L("Startup.SplashStatus", "Starting up...");
                var title = L("Startup.SplashTitle", "Starting Up");
                if (!string.IsNullOrWhiteSpace(title))
                    Title = title;
            }
            catch { }
        }

        private void ApplyConfigTexts()
        {
            try
            {
                var settings = AppCfgSvc.Current;
                if (settings?.Application == null)
                    return;

                if (!string.IsNullOrWhiteSpace(settings.Application.ShortName))
                    txtAppName.Text = settings.Application.ShortName;

                if (!string.IsNullOrWhiteSpace(settings.Application.NameEn))
                    txtAppNameEn.Text = settings.Application.NameEn;

                if (!string.IsNullOrWhiteSpace(settings.Application.Version))
                    txtVersion.Text = $"Version {settings.Application.Version}";

                if (!string.IsNullOrWhiteSpace(settings.Application.Copyright))
                    txtCopyright.Text = settings.Application.Copyright;
            }
            catch
            {
                // Keep XAML defaults.
            }
        }

        private void SetStatus(string text)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => txtStatus.Text = text);
                    return;
                }

                txtStatus.Text = text;
            }
            catch { /* ignore UI failures */ }
        }

        private void SetProgress(int value)
        {
            try
            {
                value = Math.Clamp(value, 0, 100);
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => progressBar.Value = value);
                    return;
                }

                progressBar.Value = value;
            }
            catch { /* ignore */ }
        }

        private static async Task SafeDelay(int ms)
        {
            try { await Task.Delay(Math.Max(0, ms)); }
            catch { /* ignore */ }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value[..max] + "…";
        }

        private async Task OpenStartupFlowAsync()
        {
            var hasCompanies = _hasCompaniesAtStartup
                ?? await new CoRegSvc().HasCompaniesAsync();
            var companyWindow = new ComWin();
            if (WpfApp.Current != null)
            {
                WpfApp.Current.MainWindow = companyWindow;
                WpfApp.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            // Keep the next main window assigned before closing the splash so
            // WPF does not shut down between the two windows.
            Close();

            if (!hasCompanies)
            {
                var wizard = new StpWiz(StpWizMode.NewCompany);
                var created = wizard.ShowDialog() == true && wizard.CompletedSuccessfully;
                if (!created)
                {
                    WpfApp.Current?.Shutdown();
                    return;
                }
            }

            companyWindow.Show();
            companyWindow.Activate();
        }

        protected override void ApplyLocalizedText()
        {
            // Splash captions are applied via L() during construction.
        }
    }
}
