using System;
using System.Threading.Tasks;
using ShouTech.App;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public sealed class RecycleBinSecurityTests
{
    [Fact]
    public async Task RestoreAsync_ShouldRejectItemFromAnotherCompanyBeforeOpeningDatabase()
    {
        var previousCompany = AppState.CurrentCompanyCode;
        AppState.CurrentCompanyCode = "001";
        try
        {
            var item = CreateItem("002");
            var result = await RecycleBinSvc.RestoreAsync(item);

            Assert.False(result.Succeeded);
            Assert.Contains("active company", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppState.CurrentCompanyCode = previousCompany;
        }
    }

    [Fact]
    public async Task PurgeAsync_ShouldRejectItemFromAnotherCompanyBeforeOpeningDatabase()
    {
        var previousCompany = AppState.CurrentCompanyCode;
        AppState.CurrentCompanyCode = "001";
        try
        {
            var item = CreateItem("002");
            var result = await RecycleBinSvc.PurgeAsync(item);

            Assert.False(result.Succeeded);
            Assert.Contains("active company", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            AppState.CurrentCompanyCode = previousCompany;
        }
    }

    [Fact]
    public async Task ListAsync_ShouldRejectCompanyOtherThanActiveCompany()
    {
        var previousCompany = AppState.CurrentCompanyCode;
        AppState.CurrentCompanyCode = "001";
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => RecycleBinSvc.ListAsync("002"));
        }
        finally
        {
            AppState.CurrentCompanyCode = previousCompany;
        }
    }

    private static RecycleBinItem CreateItem(string companyCode) => new()
    {
        TableName = "materials",
        DatasetKey = "Products",
        PrimaryKeyColumn = "material_id",
        PrimaryKeyValue = "1",
        DisplayLabel = "Test material",
        CompanyCode = companyCode
    };
}
