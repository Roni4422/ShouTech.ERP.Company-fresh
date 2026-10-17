using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using Npgsql;
using ShouTech.App.Reporting.Invoices;

namespace ShouTech.App.Services;

/// <summary>
/// Posts the general-ledger entry for a commercial document inside the caller's transaction.
/// Sales: debit receivables (cash for POS), credit revenue and output tax; debit cost of goods sold, credit inventory.
/// Purchases: debit inventory and input tax, credit payables. Returns post the mirror entry.
/// A company with no account mappings keeps the legacy behaviour and no entry is created.
/// Ledger amounts are in the company base currency: document amount multiplied by the document exchange rate.
/// </summary>
public static class GlPost
{
    public const string Receivables = "RECEIVABLES";
    public const string Payables = "PAYABLES";
    public const string Cash = "CASH";
    public const string Bank = "BANK";
    public const string SalesRevenue = "SALES_REVENUE";
    public const string SalesReturns = "SALES_RETURNS";
    public const string OutputTax = "OUTPUT_TAX";
    public const string InputTax = "INPUT_TAX";
    public const string Inventory = "INVENTORY";
    public const string CostOfGoodsSold = "COST_OF_GOODS_SOLD";

    /// <summary>One planned ledger line, identified by account mapping key.</summary>
    public sealed record GlLine(string AccountKey, decimal Debit, decimal Credit, string Note);

    /// <summary>The journal entry created for a document.</summary>
    public sealed record PostedJournal(Guid JournalEntryId, string DocumentNumber, decimal Total);

    /// <summary>True for the document kinds that create ledger entries.</summary>
    public static bool PostsToLedger(InvDocKind kind) =>
        kind is InvDocKind.SalesInvoice or InvDocKind.SalesReturn or InvDocKind.PosSale
            or InvDocKind.PurchaseInvoice or InvDocKind.PurchaseReturn;

    /// <summary>
    /// Plans the balanced ledger lines for a document. Amounts are base-currency and non-negative.
    /// <paramref name="cost"/> is the inventory cost of goods moved (sales and sales returns only).
    /// Zero-amount lines are omitted. Returns an empty list for kinds that do not post.
    /// </summary>
    public static IReadOnlyList<GlLine> BuildLines(InvDocKind kind, decimal net, decimal tax, decimal cost, string? paymentMethod = null)
    {
        if (net < 0m || tax < 0m || cost < 0m)
            throw new InvalidOperationException("Document amounts must not be negative.");
        net = Round(net);
        tax = Round(tax);
        cost = Round(cost);
        var total = net + tax;
        var lines = new List<GlLine>();

        void Debit(string key, decimal amount, string note)
        {
            if (amount > 0m) lines.Add(new GlLine(key, amount, 0m, note));
        }

        void Credit(string key, decimal amount, string note)
        {
            if (amount > 0m) lines.Add(new GlLine(key, 0m, amount, note));
        }

        switch (kind)
        {
            case InvDocKind.SalesInvoice:
                Debit(Receivables, total, "Sale");
                Credit(SalesRevenue, net, "Sales revenue");
                Credit(OutputTax, tax, "Output tax");
                Debit(CostOfGoodsSold, cost, "Cost of goods sold");
                Credit(Inventory, cost, "Inventory issued");
                break;
            case InvDocKind.PosSale:
                var tenderAccount = (paymentMethod ?? "Cash").Trim().ToUpperInvariant() switch
                {
                    "CASH" => Cash,
                    "CREDIT" => Receivables,
                    "VISA" or "K-NET" or "KNET" => Bank,
                    _ => throw new InvalidOperationException("Unsupported POS payment method.")
                };
                Debit(tenderAccount, total, "POS sale");
                Credit(SalesRevenue, net, "Sales revenue");
                Credit(OutputTax, tax, "Output tax");
                Debit(CostOfGoodsSold, cost, "Cost of goods sold");
                Credit(Inventory, cost, "Inventory issued");
                break;
            case InvDocKind.SalesReturn:
                Debit(SalesReturns, net, "Sales return");
                Debit(OutputTax, tax, "Output tax reversed");
                Credit(Receivables, total, "Customer credit");
                Debit(Inventory, cost, "Inventory returned");
                Credit(CostOfGoodsSold, cost, "Cost of goods sold reversed");
                break;
            case InvDocKind.PurchaseInvoice:
                Debit(Inventory, net, "Inventory purchased");
                Debit(InputTax, tax, "Input tax");
                Credit(Payables, total, "Supplier payable");
                break;
            case InvDocKind.PurchaseReturn:
                Debit(Payables, total, "Supplier debit");
                Credit(Inventory, net, "Inventory returned to supplier");
                Credit(InputTax, tax, "Input tax reversed");
                break;
            default:
                return lines;
        }

        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        if (debits != credits)
            throw new InvalidOperationException("The planned ledger entry is not balanced.");
        return lines;
    }

    /// <summary>
    /// Creates and posts the journal entry for a document using the caller's connection and transaction.
    /// Returns null when the document kind does not post, the company has no account mappings,
    /// or every amount is zero. Throws when mappings are incomplete or no open fiscal period covers the date,
    /// so the caller's transaction rolls back and the document stays a draft.
    /// </summary>
    public static PostedJournal? PostDocument(DbConnection connection, DbTransaction transaction, InvDoc doc, long docId, string userId)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(doc);
        if (!PostsToLedger(doc.Kind)) return null;

        var isPostgres = connection is NpgsqlConnection;
        var actor = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();
        var companyCode = AppState.CurrentCompanyCode ?? string.Empty;

        var companyId = ScalarObject(connection, transaction,
            "SELECT company_id FROM companies WHERE company_code=@code AND is_deleted=" + (isPostgres ? "false" : "0"),
            ("@code", companyCode));
        if (companyId is null) return null;

        var mappings = LoadMappings(connection, transaction, companyId);
        if (mappings.Count == 0) return null;

        var isSales = doc.Kind is InvDocKind.SalesInvoice or InvDocKind.SalesReturn or InvDocKind.PosSale;
        var headerTable = isSales ? "sales_documents" : "purchase_documents";
        var rate = ReadExchangeRate(connection, transaction, headerTable, docId);
        var net = Round(doc.Subtotal * rate);
        var tax = Round(doc.TaxAmount * rate);
        var cost = isSales
            ? InventoryCost(connection, transaction, companyCode, doc, isPostgres)
            : 0m;

        var plan = BuildLines(doc.Kind, net, tax, cost, doc.PaymentMethod);
        if (plan.Count == 0) return null;

        var missing = new List<string>();
        var resolved = new List<(object AccountId, GlLine Line)>();
        foreach (var line in plan)
        {
            var key = line.AccountKey;
            if (!mappings.TryGetValue(key, out var accountId) && key == SalesReturns)
                mappings.TryGetValue(SalesRevenue, out accountId);
            if (accountId is null)
            {
                if (!missing.Contains(key)) missing.Add(key);
                continue;
            }
            resolved.Add((accountId, line));
        }
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Ledger posting is configured for this company but these account mappings are missing: {string.Join(", ", missing)}. Configure them before posting {doc.DocNo}.");

        var currency = (doc.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (currency.Length != 3)
            throw new InvalidOperationException($"Document {doc.DocNo} has an invalid currency code '{doc.CurrencyCode}'.");

        object dateValue = isPostgres
            ? DateOnly.FromDateTime(doc.DocDate)
            : doc.DocDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var periodId = ScalarObject(connection, transaction,
            "SELECT fp.fiscal_period_id FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id " +
            "WHERE fp.company_id=@company AND @date BETWEEN fp.start_date AND fp.end_date AND fp.status='OPEN' AND fy.is_closed=" + (isPostgres ? "false" : "0"),
            ("@company", companyId), ("@date", dateValue));
        if (periodId is null)
            throw new InvalidOperationException(
                $"No open fiscal period covers {doc.DocDate:yyyy-MM-dd}. Open or create the fiscal year before posting {doc.DocNo}.");

        var entryGuid = Guid.NewGuid();
        object entryId = isPostgres ? entryGuid : entryGuid.ToString();
        var documentNumber = $"GL:{doc.KindCode}:{doc.DocNo}";
        if (documentNumber.Length > 100)
            throw new InvalidOperationException($"Document number '{doc.DocNo}' is too long to link to a journal entry.");

        Execute(connection, transaction,
            "INSERT INTO journal_entries (journal_entry_id, company_id, entry_date, description, status, created_by, fiscal_period_id, reference_number, currency_code, exchange_rate, document_number) " +
            "VALUES (@id, @company, @date, @description, 0, @actor, @period, @reference, @currency, @rate, @documentNumber)",
            ("@id", entryId), ("@company", companyId), ("@date", dateValue),
            ("@description", $"{doc.KindCode} {doc.DocNo}"), ("@actor", actor), ("@period", periodId),
            ("@reference", doc.DocNo), ("@currency", currency), ("@rate", rate), ("@documentNumber", documentNumber));

        var lineNumber = 0;
        foreach (var (accountId, line) in resolved)
        {
            Execute(connection, transaction,
                "INSERT INTO journal_entry_lines (journal_entry_id, line_number, account_id, debit, credit, note) VALUES (@id, @line, @account, @debit, @credit, @note)",
                ("@id", entryId), ("@line", ++lineNumber), ("@account", accountId),
                ("@debit", line.Debit), ("@credit", line.Credit), ("@note", line.Note));
        }

        Execute(connection, transaction,
            "INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details) VALUES (@id, 1, @actor, @details)",
            ("@id", entryId), ("@actor", actor), ("@details", $"Created from {doc.KindCode} {doc.DocNo}"));

        var posted = Execute(connection, transaction,
            "UPDATE journal_entries SET status=1, posted_by=@actor, posted_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=0",
            ("@id", entryId), ("@actor", actor));
        if (posted != 1)
            throw new InvalidOperationException($"The ledger entry for {doc.DocNo} could not be posted.");

        Execute(connection, transaction,
            "INSERT INTO journal_audit_events (journal_entry_id, event_type, actor, details) VALUES (@id, 2, @actor, @details)",
            ("@id", entryId), ("@actor", actor), ("@details", $"Posted from {doc.KindCode} {doc.DocNo}"));

        return new PostedJournal(entryGuid, documentNumber, plan.Where(l => l.Debit > 0m).Sum(l => l.Debit));
    }

    private static Dictionary<string, object> LoadMappings(DbConnection connection, DbTransaction transaction, object companyId)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT mapping_key, account_id FROM account_mappings WHERE company_id=@company";
        Add(command, "@company", companyId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture);
            var account = reader.GetValue(1);
            if (!string.IsNullOrEmpty(key) && account is not DBNull)
                result[key] = account;
        }
        return result;
    }

    private static decimal ReadExchangeRate(DbConnection connection, DbTransaction transaction, string headerTable, long docId)
    {
        var value = ScalarObject(connection, transaction,
            $"SELECT exchange_rate FROM {headerTable} WHERE doc_id=@id", ("@id", docId));
        if (value is null) return 1m;
        var rate = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        return rate > 0m ? rate : 1m;
    }

    /// <summary>Inventory cost of the goods on the document at current average cost, in base currency.</summary>
    private static decimal InventoryCost(DbConnection connection, DbTransaction transaction, string companyCode, InvDoc doc, bool isPostgres)
    {
        var notDeleted = isPostgres ? "false" : "0";
        var total = 0m;
        foreach (var line in doc.Lines)
        {
            if (line.Qty <= 0m) continue;
            object? materialId = line.MaterialId is > 0 ? line.MaterialId : null;
            if (materialId is null && !string.IsNullOrWhiteSpace(line.MaterialCode))
            {
                materialId = ScalarObject(connection, transaction,
                    $"SELECT material_id FROM materials WHERE company_id=@company AND code=@code AND is_deleted={notDeleted} LIMIT 1",
                    ("@company", companyCode), ("@code", line.MaterialCode));
            }
            if (materialId is null) continue;

            var warehouseId = line.WarehouseId ?? doc.WarehouseId;
            var sql = "SELECT average_cost FROM material_stock_balances WHERE company_id=@company AND material_id=@material AND is_deleted=" + notDeleted;
            var parameters = new List<(string Name, object? Value)> { ("@company", companyCode), ("@material", materialId) };
            if (warehouseId is > 0)
            {
                sql += " AND warehouse_id=@warehouse";
                parameters.Add(("@warehouse", warehouseId.Value));
            }
            sql += " ORDER BY warehouse_id LIMIT 1";
            var average = ScalarObject(connection, transaction, sql, parameters.ToArray());
            if (average is null) continue;
            total += Round(line.Qty * Convert.ToDecimal(average, CultureInfo.InvariantCulture));
        }
        return Round(total);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    private static object? ScalarObject(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) Add(command, name, value);
        var result = command.ExecuteScalar();
        return result is null or DBNull ? null : result;
    }

    private static int Execute(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) Add(command, name, value);
        return command.ExecuteNonQuery();
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
