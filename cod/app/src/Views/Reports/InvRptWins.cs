// ========================================================================
// FILE: Views/Reports/InvRptWins.cs
// PROJECT: SHOUTECH ERP - Phase 7B inventory tab report windows
// Built on RptBase: filter bar, column chooser, export, print, zebra grid.
// ========================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShouTech.App.Reporting;
using ShouTech.App.Services;

namespace ShouTech.App.Views.Reports;

/// <summary>Shared helpers for the Phase 7B inventory report windows.</summary>
internal static class InvRptShared
{
    /// <summary>Material group choices (All + active groups), localized by current language.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> MaterialGroupChoices(Func<string, string> translate)
    {
        var choices = new List<KeyValuePair<string, string>> { new("", translate("Reports.Filter.All")) };
        try
        {
            choices.AddRange(MatRepo.LoadGroups()
                .Where(group => !group.IsDeleted)
                .Select(group => new KeyValuePair<string, string>(
                    group.Code,
                    string.Equals(App.CurrentLanguageCode, "ar", StringComparison.OrdinalIgnoreCase)
                        ? group.Name
                        : string.IsNullOrWhiteSpace(group.NameEn) ? group.Name : group.NameEn)));
        }
        catch
        {
            // Optional master data must never break report startup.
        }
        return choices;
    }

    public static decimal ToDec(object? v)
        => v is null ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
}

/// <summary>Material master list (Inventory.MaterialList).</summary>
public sealed class MatListWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.MaterialList",
        TitleTextKey = "Inventory.MaterialList",
        Columns = new RptCol[]
        {
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 260 },
            new() { Key = "NameEn", HeaderTextKey = "MatCard.NameEn", Width = 200 },
            new() { Key = "GroupCode", HeaderTextKey = "Inventory.Group", Width = 120 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "Barcode", HeaderTextKey = "MatCard.Barcode", Width = 140 },
            new() { Key = "Gtin", HeaderTextKey = "MatCard.Gtin", Width = 140 },
            new() { Key = "Status", HeaderTextKey = "Reports.Col.Status", Width = 90 }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var raw = await RptQuery.MaterialListAsync(filters.GetString("Material"), filters.GetString("Group"), ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["NameEn"] = r.GetValueOrDefault("MaterialNameEn"),
            ["GroupCode"] = r.GetValueOrDefault("GroupCode"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["Barcode"] = r.GetValueOrDefault("Barcode"),
            ["Gtin"] = r.GetValueOrDefault("Gtin"),
            ["Status"] = r.GetValueOrDefault("Status")
        }).ToList();
        return new RptData { Columns = BuildDefinition().Columns, Rows = rows };
    }
}

/// <summary>Material price list (Inventory.PriceList).</summary>
public sealed class PrListWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.PriceList",
        TitleTextKey = "Inventory.PriceList",
        Columns = new RptCol[]
        {
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 260 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "GroupCode", HeaderTextKey = "Inventory.Group", Width = 120 },
            new() { Key = "Cost", HeaderTextKey = "MatCard.Cost", Type = RptColType.Money, Width = 110, Format = "N2" },
            new() { Key = "RetailPrice", HeaderTextKey = "MatCard.PriceRetail", Type = RptColType.Money, Width = 110, Format = "N2" },
            new() { Key = "WholesalePrice", HeaderTextKey = "MatCard.PriceWholesale", Type = RptColType.Money, Width = 120, Format = "N2" },
            new() { Key = "Currency", HeaderTextKey = "Reports.Col.Currency", Width = 90 }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var raw = await RptQuery.MaterialPriceListAsync(filters.GetString("Material"), filters.GetString("Group"), ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["GroupCode"] = r.GetValueOrDefault("GroupCode"),
            ["Cost"] = InvRptShared.ToDec(r.GetValueOrDefault("Cost")),
            ["RetailPrice"] = InvRptShared.ToDec(r.GetValueOrDefault("RetailPrice")),
            ["WholesalePrice"] = InvRptShared.ToDec(r.GetValueOrDefault("WholesalePrice")),
            ["Currency"] = r.GetValueOrDefault("Currency")
        }).ToList();
        return new RptData { Columns = BuildDefinition().Columns, Rows = rows };
    }
}

/// <summary>Stock shortage list at/below minimum or reorder level (Inventory.ShortageList).</summary>
public sealed class ShortListWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.ShortageList",
        TitleTextKey = "Inventory.ShortageList",
        Columns = new RptCol[]
        {
            new() { Key = "Warehouse", HeaderTextKey = "Reports.Col.Warehouse", Width = 110 },
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 240 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "Qty", HeaderTextKey = "Reports.Col.Qty", Type = RptColType.FractionalNumber, Width = 100, Format = "N3" },
            new() { Key = "MinQty", HeaderTextKey = "Reports.Col.MinQty", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "ReorderQty", HeaderTextKey = "MatCard.ReorderQty", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "Shortage", HeaderTextKey = "Reports.Col.Shortage", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "AvgCost", HeaderTextKey = "Reports.Col.AvgCost", Type = RptColType.Money, Width = 100, Format = "N4" }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "Warehouse", LabelTextKey = "Reports.Filter.Warehouse", Kind = RptFltKind.Warehouse },
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadWarehouseChoices()
        => RptQuery.WarehouseChoices();

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var raw = await RptQuery.ShortageListAsync(
            filters.GetString("Material"), filters.GetString("Group"), filters.GetString("Warehouse"), ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["Warehouse"] = r.GetValueOrDefault("Warehouse"),
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["Qty"] = InvRptShared.ToDec(r.GetValueOrDefault("Qty")),
            ["MinQty"] = InvRptShared.ToDec(r.GetValueOrDefault("MinQty")),
            ["ReorderQty"] = InvRptShared.ToDec(r.GetValueOrDefault("ReorderQty")),
            ["Shortage"] = InvRptShared.ToDec(r.GetValueOrDefault("Shortage")),
            ["AvgCost"] = InvRptShared.ToDec(r.GetValueOrDefault("AvgCost"))
        }).ToList();
        return new RptData
        {
            Columns = BuildDefinition().Columns,
            Rows = rows,
            Totals = new Dictionary<string, object?>
            {
                ["MaterialName"] = T("Reports.Totals"),
                ["Qty"] = rows.Sum(x => InvRptShared.ToDec(x["Qty"])),
                ["Shortage"] = rows.Sum(x => InvRptShared.ToDec(x["Shortage"]))
            }
        };
    }
}

/// <summary>End of period stock valuation (Inventory.EndOfPeriod).</summary>
public sealed class EndPrdWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.EndOfPeriod",
        TitleTextKey = "Inventory.EndOfPeriod",
        Columns = new RptCol[]
        {
            new() { Key = "Warehouse", HeaderTextKey = "Reports.Col.Warehouse", Width = 110 },
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 240 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "Qty", HeaderTextKey = "Reports.Col.Qty", Type = RptColType.FractionalNumber, Width = 100, Format = "N3" },
            new() { Key = "AvgCost", HeaderTextKey = "Reports.Col.AvgCost", Type = RptColType.Money, Width = 100, Format = "N4" },
            new() { Key = "Value", HeaderTextKey = "Reports.Col.Value", Type = RptColType.Money, Width = 120, Format = "N2" }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "Warehouse", LabelTextKey = "Reports.Filter.Warehouse", Kind = RptFltKind.Warehouse },
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup },
            new()
            {
                Key = "AsOf",
                LabelTextKey = "Reports.Filter.DateTo",
                Kind = RptFltKind.DateTo,
                DefaultValue = DateTime.Today
            }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadWarehouseChoices()
        => RptQuery.WarehouseChoices();

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var asOf = filters.GetDateOr("AsOf", DateTime.Today);
        var raw = await RptQuery.EndOfPeriodAsync(
            filters.GetString("Warehouse"), filters.GetString("Material"), filters.GetString("Group"), asOf, ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["Warehouse"] = r.GetValueOrDefault("Warehouse"),
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["Qty"] = InvRptShared.ToDec(r.GetValueOrDefault("Qty")),
            ["AvgCost"] = InvRptShared.ToDec(r.GetValueOrDefault("AvgCost")),
            ["Value"] = InvRptShared.ToDec(r.GetValueOrDefault("Value"))
        }).ToList();
        return new RptData
        {
            Columns = BuildDefinition().Columns,
            Rows = rows,
            Totals = new Dictionary<string, object?>
            {
                ["MaterialName"] = T("Reports.Totals"),
                ["Qty"] = rows.Sum(x => InvRptShared.ToDec(x["Qty"])),
                ["Value"] = rows.Sum(x => InvRptShared.ToDec(x["Value"]))
            }
        };
    }
}

/// <summary>Materials flagged for expiry control (Inventory.ExpiredMaterials).</summary>
public sealed class ExpMatWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.ExpiredMaterials",
        TitleTextKey = "Inventory.ExpiredMaterials",
        Columns = new RptCol[]
        {
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 260 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "GroupCode", HeaderTextKey = "Inventory.Group", Width = 120 },
            new() { Key = "QtyOnHand", HeaderTextKey = "MatCard.QtyOnHand", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "ShelfLifeDays", HeaderTextKey = "MatCard.ShelfDays", Type = RptColType.WholeNumber, Width = 110 },
            new() { Key = "AlertDays", HeaderTextKey = "Reports.Col.AlertDays", Type = RptColType.WholeNumber, Width = 110 }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var raw = await RptQuery.ExpiringMaterialsAsync(filters.GetString("Material"), filters.GetString("Group"), ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["GroupCode"] = r.GetValueOrDefault("GroupCode"),
            ["QtyOnHand"] = InvRptShared.ToDec(r.GetValueOrDefault("QtyOnHand")),
            ["ShelfLifeDays"] = InvRptShared.ToDec(r.GetValueOrDefault("ShelfLifeDays")),
            ["AlertDays"] = InvRptShared.ToDec(r.GetValueOrDefault("AlertDays"))
        }).ToList();
        return new RptData { Columns = BuildDefinition().Columns, Rows = rows };
    }
}

/// <summary>Materials with no movement in the selected period (Inventory.StagnantMaterials).</summary>
public sealed class StagMatWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.StagnantMaterials",
        TitleTextKey = "Inventory.StagnantMaterials",
        Columns = new RptCol[]
        {
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 260 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "GroupCode", HeaderTextKey = "Inventory.Group", Width = 120 },
            new() { Key = "QtyOnHand", HeaderTextKey = "MatCard.QtyOnHand", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "LastMove", HeaderTextKey = "Reports.Col.LastMove", Type = RptColType.Date, Width = 120 }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "From", LabelTextKey = "Reports.Filter.DateFrom", Kind = RptFltKind.DateFrom, DefaultValue = DateTime.Today.AddMonths(-1) },
            new() { Key = "To", LabelTextKey = "Reports.Filter.DateTo", Kind = RptFltKind.DateTo, DefaultValue = DateTime.Today },
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material },
            new() { Key = "Group", LabelTextKey = "Inventory.MaterialGroupFilter", Kind = RptFltKind.MaterialGroup }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadMaterialGroupChoices()
        => InvRptShared.MaterialGroupChoices(T);

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var from = filters.GetDateOr("From", DateTime.Today.AddMonths(-1));
        var to = filters.GetDateOr("To", DateTime.Today);
        var raw = await RptQuery.StagnantMaterialsAsync(
            filters.GetString("Material"), filters.GetString("Group"), from, to, ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["GroupCode"] = r.GetValueOrDefault("GroupCode"),
            ["QtyOnHand"] = InvRptShared.ToDec(r.GetValueOrDefault("QtyOnHand")),
            ["LastMove"] = r.GetValueOrDefault("LastMove")
        }).ToList();
        return new RptData { Columns = BuildDefinition().Columns, Rows = rows };
    }
}

/// <summary>Most active materials by movement count (Inventory.MostActiveMaterials).</summary>
public sealed class ActMatWin : RptBase
{
    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.MostActiveMaterials",
        TitleTextKey = "Inventory.MostActiveMaterials",
        Columns = new RptCol[]
        {
            new() { Key = "MaterialCode", HeaderTextKey = "Reports.Col.MaterialCode", Width = 120 },
            new() { Key = "MaterialName", HeaderTextKey = "Reports.Col.MaterialName", Width = 260 },
            new() { Key = "Unit", HeaderTextKey = "Reports.Col.Unit", Width = 80 },
            new() { Key = "MoveCount", HeaderTextKey = "Reports.Col.MoveCount", Type = RptColType.WholeNumber, Width = 110 },
            new() { Key = "QtyIn", HeaderTextKey = "Reports.Col.QtyIn", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "QtyOut", HeaderTextKey = "Reports.Col.QtyOut", Type = RptColType.FractionalNumber, Width = 110, Format = "N3" },
            new() { Key = "MoveValue", HeaderTextKey = "Reports.Col.Value", Type = RptColType.Money, Width = 120, Format = "N2" }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "From", LabelTextKey = "Reports.Filter.DateFrom", Kind = RptFltKind.DateFrom, DefaultValue = DateTime.Today.AddMonths(-1) },
            new() { Key = "To", LabelTextKey = "Reports.Filter.DateTo", Kind = RptFltKind.DateTo, DefaultValue = DateTime.Today },
            new() { Key = "Warehouse", LabelTextKey = "Reports.Filter.Warehouse", Kind = RptFltKind.Warehouse },
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadWarehouseChoices()
        => RptQuery.WarehouseChoices();

    protected override async Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var from = filters.GetDateOr("From", DateTime.Today.AddMonths(-1));
        var to = filters.GetDateOr("To", DateTime.Today);
        var raw = await RptQuery.MostActiveMaterialsAsync(
            filters.GetString("Material"), filters.GetString("Warehouse"), from, to, ct);
        var rows = raw.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["MaterialCode"] = r.GetValueOrDefault("MaterialCode"),
            ["MaterialName"] = r.GetValueOrDefault("MaterialName"),
            ["Unit"] = r.GetValueOrDefault("Unit"),
            ["MoveCount"] = InvRptShared.ToDec(r.GetValueOrDefault("MoveCount")),
            ["QtyIn"] = InvRptShared.ToDec(r.GetValueOrDefault("QtyIn")),
            ["QtyOut"] = InvRptShared.ToDec(r.GetValueOrDefault("QtyOut")),
            ["MoveValue"] = InvRptShared.ToDec(r.GetValueOrDefault("MoveValue"))
        }).ToList();
        return new RptData
        {
            Columns = BuildDefinition().Columns,
            Rows = rows,
            Totals = new Dictionary<string, object?>
            {
                ["MaterialName"] = T("Reports.Totals"),
                ["MoveCount"] = rows.Sum(x => InvRptShared.ToDec(x["MoveCount"])),
                ["QtyIn"] = rows.Sum(x => InvRptShared.ToDec(x["QtyIn"])),
                ["QtyOut"] = rows.Sum(x => InvRptShared.ToDec(x["QtyOut"])),
                ["MoveValue"] = rows.Sum(x => InvRptShared.ToDec(x["MoveValue"]))
            }
        };
    }
}

/// <summary>
/// Generic report workbench for the remaining Inventory tab keys.
/// Provides the full RptBase shell (filters, columns, export, print) with a
/// Date/Document/Description/Value layout, ready for per-report data wiring.
/// </summary>
public sealed class GenInvRptWin : RptBase
{
    private readonly string _titleKey;

    public GenInvRptWin(string titleKey) => _titleKey = titleKey;

    protected override RptDef BuildDefinition() => new()
    {
        ReportKey = "Inv.Generic:" + _titleKey,
        TitleTextKey = _titleKey,
        Columns = new RptCol[]
        {
            new() { Key = "EntryDate", HeaderTextKey = "Reports.Col.Date", Type = RptColType.Date, Width = 110 },
            new() { Key = "DocNo", HeaderTextKey = "Reports.Col.DocNo", Width = 130 },
            new() { Key = "Description", HeaderTextKey = "Reports.Col.Description", Width = 320 },
            new() { Key = "Value", HeaderTextKey = "Reports.Col.Value", Type = RptColType.Money, Width = 120, Format = "N2" }
        },
        Filters = new RptFlt[]
        {
            new() { Key = "From", LabelTextKey = "Reports.Filter.DateFrom", Kind = RptFltKind.DateFrom, DefaultValue = DateTime.Today.AddMonths(-1) },
            new() { Key = "To", LabelTextKey = "Reports.Filter.DateTo", Kind = RptFltKind.DateTo, DefaultValue = DateTime.Today },
            new() { Key = "Warehouse", LabelTextKey = "Reports.Filter.Warehouse", Kind = RptFltKind.Warehouse },
            new() { Key = "Material", LabelTextKey = "Inventory.MaterialFilter", Kind = RptFltKind.Material }
        }
    };

    protected override IReadOnlyList<KeyValuePair<string, string>> LoadWarehouseChoices()
        => RptQuery.WarehouseChoices();

    protected override Task<RptData> LoadDataAsync(RptFltValues filters, CancellationToken ct)
    {
        var empty = new List<IReadOnlyDictionary<string, object?>>() as IReadOnlyList<IReadOnlyDictionary<string, object?>>;
        return Task.FromResult(new RptData { Columns = BuildDefinition().Columns, Rows = empty });
    }
}
