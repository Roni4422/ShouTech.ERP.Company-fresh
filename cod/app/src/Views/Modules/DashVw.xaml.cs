using System.Windows.Controls;
using ShouTech.App.Common;
using ShouTech.App.Localization;
using ShouTech.App.Services;

namespace ShouTech.App.Views.Modules
{
    public partial class DashVw : UserControl
    {
        public DashVw()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            UiLoczr.ApplyToRoot(this);

            try
            {
                var data = AppHost.Data;

                CustomersCount.Text =
                    AppFormat.FormatNumber(await data.GetTotalCustomersAsync());

                ProductsCount.Text =
                    AppFormat.FormatNumber(await data.GetTotalProductsAsync());

                PendingCount.Text =
                    AppFormat.FormatNumber(await data.GetTotalPendingApprovalsAsync());
            }
            catch (System.Exception ex)
            {
                CustomersCount.Text = "—";
                ProductsCount.Text = "—";
                PendingCount.Text = "—";
                AppHost.Logging?.LogError("Dashboard", "DSH-DB-001: Dashboard data could not be loaded from the active company database.", ex);
                AppHost.Logging?.LogOperation("Dashboard", "LoadFailed", "DSH-DB-001; dashboard metrics unavailable; placeholders displayed instead of sample data.");
            }
        }
    }
}