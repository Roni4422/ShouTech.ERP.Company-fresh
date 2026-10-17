using System.Linq;
using ShouTech.App.Services;
using Xunit;

namespace Kern.Tests;

public class GlPostTests
{
    [Theory]
    [InlineData(GlPost.Kind.SalesInvoice, 100, 15, 40)]
    [InlineData(GlPost.Kind.SalesInvoice, 100, 0, 0)]
    [InlineData(GlPost.Kind.SalesReturn, 100, 15, 40)]
    [InlineData(GlPost.Kind.PurchaseInvoice, 100, 15, 0)]
    [InlineData(GlPost.Kind.PurchaseReturn, 100, 15, 0)]
    public void BuildLines_IsBalanced(GlPost.Kind kind, decimal net, decimal tax, decimal cost)
    {
        var lines = GlPost.BuildLines(kind, net, tax, cost);
        Assert.NotEmpty(lines);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.All(lines, l => Assert.True(l.Debit >= 0 && l.Credit >= 0 && (l.Debit == 0 || l.Credit == 0)));
    }

    [Fact]
    public void SalesInvoice_DebitsReceivableForGrossAmount()
    {
        var lines = GlPost.BuildLines(GlPost.Kind.SalesInvoice, 100m, 15m, 40m);
        Assert.Equal(115m, lines.Single(l => l.Role == "AR").Debit);
        Assert.Equal(100m, lines.Single(l => l.Role == "SALES").Credit);
        Assert.Equal(15m, lines.Single(l => l.Role == "TAX_OUT").Credit);
    }
}
