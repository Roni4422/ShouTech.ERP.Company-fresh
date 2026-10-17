using System;
using System.Collections.Generic;
using System.Linq;

namespace ShouTech.App.Reporting.Invoices;

/// <summary>Commercial document kinds used across sales and purchases.</summary>
public enum InvDocKind
{
    SalesInvoice = 0,
    SalesReturn = 1,
    PosSale = 2,
    PurchaseInvoice = 10,
    PurchaseReturn = 11,
    StockIn = 20,
    StockOut = 21,
    StockTransfer = 22,
    OpeningStock = 23,
    InventoryCount = 24,
    Quotation = 30
}

/// <summary>Lifecycle of a commercial document.</summary>
public enum InvDocStatus
{
    Draft = 0,
    Posted = 1,
    Cancelled = 2
}

/// <summary>One line on a commercial invoice / return.</summary>
public sealed class InvLine
{
    public int LineNo { get; set; }
    public long? MaterialId { get; set; }
    public string MaterialCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal TaxRate { get; set; }
    public int? WarehouseId { get; set; }

    public decimal LineSubtotal
    {
        get
        {
            var gross = Qty * UnitPrice;
            var disc = gross * (DiscountPct / 100m);
            return decimal.Round(gross - disc, 4, MidpointRounding.AwayFromZero);
        }
    }

    public decimal LineTax
        => decimal.Round(LineSubtotal * (TaxRate / 100m), 4, MidpointRounding.AwayFromZero);

    public decimal LineTotal => LineSubtotal + LineTax;
}

/// <summary>
/// Full commercial document model (sales or purchase).
/// Tax is calculated per line to support mixed rates (GCC / Levant / EU style).
/// </summary>
public sealed class InvDoc
{
    public long DocId { get; set; }
    public InvDocKind Kind { get; set; } = InvDocKind.SalesInvoice;
    public string DocNo { get; set; } = string.Empty;
    public DateTime DocDate { get; set; } = DateTime.Today;
    public long? PartyId { get; set; }
    public string PartyCode { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public int? TargetWarehouseId { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public string PaymentMethod { get; set; } = "Cash";
    public decimal ExchangeRate { get; set; } = 1m;
    public string? TaxCode { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public string? Notes { get; set; }
    public InvDocStatus Status { get; set; } = InvDocStatus.Draft;
    public List<InvLine> Lines { get; set; } = new();

    public string KindCode => Kind switch
    {
        InvDocKind.SalesInvoice => "SAL",
        InvDocKind.SalesReturn => "SRT",
        InvDocKind.PosSale => "POS",
        InvDocKind.PurchaseInvoice => "PUR",
        InvDocKind.PurchaseReturn => "PRT",
        InvDocKind.StockIn => "ENT",
        InvDocKind.StockOut => "EXT",
        InvDocKind.StockTransfer => "TRN",
        InvDocKind.OpeningStock => "OPN",
        InvDocKind.InventoryCount => "CNT",
        InvDocKind.Quotation => "QUO",
        _ => "DOC"
    };

    public bool IsSalesSide => Kind is InvDocKind.SalesInvoice or InvDocKind.SalesReturn or InvDocKind.PosSale or InvDocKind.Quotation;

    public decimal Subtotal => Lines.Sum(l => l.LineSubtotal);
    public decimal TaxAmount => Lines.Sum(l => l.LineTax);
    public decimal GrandTotal => Subtotal + TaxAmount;

    public void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
    }

    public void RecalcLineTaxesFromDefault()
    {
        foreach (var line in Lines)
        {
            if (line.TaxRate == 0 && DefaultTaxRate > 0)
                line.TaxRate = DefaultTaxRate;
        }
    }
}
