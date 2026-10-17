using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShouTech.App.Reporting;
using ShouTech.App.Services;

namespace ShouTech.App.Views.Reports;

/// <summary>
/// Generic ribbon report over a dba-owned view (migration 027). One definition per ribbon key.
/// </summary>
public sealed class QryWin : RptBase
{
    private sealed record Def(string View, string OrderBy, string? SumKey, RptCol[] Cols);

    private static RptCol Txt(string key, string header, int w) => new() { Key = key, HeaderTextKey = header, Type = RptColType.Text, Width = w };
    private static RptCol Num(string key, string header, string fmt = "N3") => new() { Key = key, HeaderTextKey = header, Type = RptColType.FractionalNumber, Width = 110, Format = fmt };
    private static RptCol Mny(string key, string header) => new() { Key = key, HeaderTextKey = header, Type = RptColType.Money, Width = 120, Format = "N2" };

    private static readonly RptCol[] StockCols =
    {
        Txt("warehouse", "Reports.Col.Warehouse", 100), Txt("material_code", "Reports.Col.MaterialCode", 120),
        Txt("name", "Reports.Col.MaterialName", 240), Txt("unit", "Reports.Col.Unit", 70),
        Num("qty", "Reports.Col.Qty"), Mny("avg_cost", "Reports.Col.AvgCost"), Mny("value", "Reports.Col.Value"),
    };

    private static readonly Dictionary<string, Def> Defs = new(StringComparer.Ordinal)
    {
        ["Inventory.NegativeOutput"] = new("v_rpt_negative_stock", "warehouse, material_code", "value", StockCols),
        ["Inventory.StagnantMaterials"] = new("v_rpt_stagnant_stock", "warehouse, material_code", "value", StockCols),
        ["Bonds.CashJournal"] = new("v_rpt_cash_journal", "doc_date DESC, doc_no DESC", "total", new[]
        {
            Txt("doc_date", "Reports.Col.Date", 100), Txt("doc_no", "Reports.Col.DocNo", 120),
            Txt("doc_kind", "Reports.Col.DocKind", 100), Txt("party_name", "Reports.Col.PartyName", 240),
            Mny("total", "Reports.Col.Total"), Txt("currency", "Reports.Col.Currency", 80),
            Txt("status", "Reports.Col.Status", 80),
        }),
        ["Accounts.ProfitLoss"] = new("v_rpt_profit_loss", "account_type DESC, account_code", "amount", new[]
        {
            Txt("account_code", "Reports.Col.AccountCode", 120), Txt("account_name", "Reports.Col.AccountName", 280),
            Txt("account_type", "Qry.Type", 110), Mny("amount", "Qry.Amount"),
        }),
        ["Inventory.SalesReport"] = new("v_rpt_sales_by_party", "total DESC", "total", new[]
        {
            Txt("party_name", "Reports.Col.PartyName", 280), Num("doc_count", "Qry.DocCount", "N0"),
            Mny("subtotal", "Reports.Col.Subtotal"), Mny("tax", "Reports.Col.Tax"), Mny("total", "Reports.Col.Total"),
        }),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["Customers.CustomerReports"] = "Inventory.SalesReport",
    };

    private readonly string _key;
    private readonly Def _def;

    public QryWin(string key)
    {
        _key = key;
        _def = Defs[key];
    }

    public static bool TryResolve(string? key, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = Aliases.GetValueOrDefault(key, key);
        if (!Defs.ContainsKey(k)) return false;
        resolved = k;
        return true;
    }

    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Qry." + _key,
        TitleTextKey = _key,
        Columns = _def.Cols,
        Filters = Array.Empty<RptFlt>()
    };

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var raw = await RptQuery.ViewAsync(_def.View, _def.OrderBy, ct);
        var ar = string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase);
        var rows = raw.Select(r =>
        {
            var d = new Dictionary<string, object?>(r, StringComparer.Ordinal);
            if (r.ContainsKey("name_ar"))
            {
                var en = Convert.ToString(r.GetValueOrDefault("name_en"), CultureInfo.InvariantCulture);
                var a = Convert.ToString(r.GetValueOrDefault("name_ar"), CultureInfo.InvariantCulture);
                d["name"] = ar || string.IsNullOrWhiteSpace(en) ? a : en;
            }
            foreach (var col in _def.Cols.Where(c => c.Type is RptColType.Money or RptColType.FractionalNumber))
                d[col.Key] = Dec(d.GetValueOrDefault(col.Key));
            return (IReadOnlyDictionary<string, object?>)d;
        }).ToList();

        var totals = new Dictionary<string, object?>();
        if (_def.SumKey is { } sk)
        {
            totals[_def.Cols[0].Key] = T("Reports.Totals");
            totals[sk] = rows.Sum(x => Dec(x.GetValueOrDefault(sk)));
        }
        return new RptData { Columns = _def.Cols, Rows = rows, Totals = totals };
    }

    private static decimal Dec(object? v)
        => v is null ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
}
