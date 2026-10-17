using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;

namespace ShouTech.App.Services;

/// <summary>
/// Posts the general-ledger journal entry for a commercial document inside the caller's
/// transaction. Accounts come from gl_account_defaults (dba migration 030).
/// Behavior: no defaults configured for the company => no journal entry (returns null);
/// partially configured or no open fiscal period => throws, so the document is not posted.
/// </summary>
public static class GlPost
{
    public enum Kind { SalesInvoice, SalesReturn, PurchaseInvoice, PurchaseReturn }

    public sealed record Input(
        Kind Kind, string CompanyCode, string DocNo, DateTime DocDate, string Currency, decimal ExchangeRate,
        decimal Net, decimal Tax, decimal Cost, string User, string? PartyName);

    public sealed record Line(string Role, decimal Debit, decimal Credit);

    /// <summary>Pure entry builder: debits always equal credits (Net + Tax = Gross).</summary>
    public static IReadOnlyList<Line> BuildLines(Kind kind, decimal net, decimal tax, decimal cost)
    {
        net = decimal.Round(net, 4); tax = decimal.Round(tax, 4); cost = decimal.Round(cost, 4);
        var gross = net + tax;
        var l = new List<Line>();
        switch (kind)
        {
            case Kind.SalesInvoice:
                l.Add(new("AR", gross, 0)); l.Add(new("SALES", 0, net)); l.Add(new("TAX_OUT", 0, tax));
                l.Add(new("COGS", cost, 0)); l.Add(new("INVENTORY", 0, cost));
                break;
            case Kind.SalesReturn:
                l.Add(new("SALES_RETURN", net, 0)); l.Add(new("TAX_OUT", tax, 0)); l.Add(new("AR", 0, gross));
                l.Add(new("INVENTORY", cost, 0)); l.Add(new("COGS", 0, cost));
                break;
            case Kind.PurchaseInvoice:
                l.Add(new("INVENTORY", net, 0)); l.Add(new("TAX_IN", tax, 0)); l.Add(new("AP", 0, gross));
                break;
            case Kind.PurchaseReturn:
                l.Add(new("AP", gross, 0)); l.Add(new("INVENTORY", 0, net)); l.Add(new("TAX_IN", 0, tax));
                break;
        }
        return l.Where(x => x.Debit != 0 || x.Credit != 0).ToList();
    }

    /// <summary>Returns the journal entry id, or null when GL integration is not configured.</summary>
    public static Guid? Post(DbConnection c, DbTransaction tx, Input i)
    {
        var companyId = Scalar(c, tx, "SELECT company_id FROM companies WHERE company_code=@c", ("@c", i.CompanyCode));
        if (companyId is null) return null;
        var cid = Convert.ToString(companyId, CultureInfo.InvariantCulture)!;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = Cmd(c, tx, "SELECT role, account_id FROM gl_account_defaults WHERE company_id=@c", ("@c", Id(c, cid))))
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                map[Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!] =
                    Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture)!;
        if (map.Count == 0) return null;

        var lines = BuildLines(i.Kind, i.Net, i.Tax, i.Cost);
        var missing = lines.Select(x => x.Role).Distinct().Where(role => !map.ContainsKey(role)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "GL account defaults are incomplete; missing: " + string.Join(", ", missing) + ". Posting was blocked.");
        if (lines.Count == 0 || lines.Sum(x => x.Debit) != lines.Sum(x => x.Credit))
            throw new InvalidOperationException("The document produces no balanced journal entry; posting was blocked.");

        var reference = $"{i.Kind}:{i.DocNo}";
        if (Scalar(c, tx, "SELECT 1 FROM journal_entries WHERE company_id=@c AND reference_number=@r",
                ("@c", Id(c, cid)), ("@r", reference)) is not null)
            throw new InvalidOperationException("A journal entry already exists for this document.");

        var closedFalse = IsPg(c) ? (object)false : 0;
        var period = Scalar(c, tx,
            "SELECT fp.fiscal_period_id FROM fiscal_periods fp JOIN fiscal_years fy ON fy.fiscal_year_id=fp.fiscal_year_id " +
            "WHERE fp.company_id=@c AND @d BETWEEN fp.start_date AND fp.end_date AND fp.status='OPEN' AND fy.is_closed=@x LIMIT 1",
            ("@c", Id(c, cid)), ("@d", Date(c, i.DocDate)), ("@x", closedFalse))
            ?? throw new InvalidOperationException("No open fiscal period covers the document date; posting was blocked.");

        var jid = Guid.NewGuid();
        var desc = $"{i.Kind} {i.DocNo}" + (string.IsNullOrWhiteSpace(i.PartyName) ? "" : " - " + i.PartyName!.Trim());
        Exec(c, tx,
            "INSERT INTO journal_entries(journal_entry_id, company_id, entry_date, description, status, created_by, " +
            "fiscal_period_id, reference_number, currency_code, exchange_rate) " +
            "VALUES(@id,@c,@d,@desc,0,@u,@p,@ref,@cur,@rate)",
            ("@id", Id(c, jid.ToString())), ("@c", Id(c, cid)), ("@d", Date(c, i.DocDate)),
            ("@desc", Trunc(desc, 500)), ("@u", Trunc(i.User, 100)), ("@p", Id(c, Convert.ToString(period, CultureInfo.InvariantCulture)!)),
            ("@ref", Trunc(reference, 100)), ("@cur", Trunc(i.Currency, 3)), ("@rate", i.ExchangeRate <= 0 ? 1m : i.ExchangeRate));
        var n = 0;
        foreach (var l in lines)
            Exec(c, tx,
                "INSERT INTO journal_entry_lines(journal_entry_id, line_number, account_id, debit, credit, note) VALUES(@id,@n,@a,@d,@cr,@note)",
                ("@id", Id(c, jid.ToString())), ("@n", ++n), ("@a", Id(c, map[l.Role])), ("@d", l.Debit), ("@cr", l.Credit), ("@note", l.Role));
        // Posting runs the database integrity triggers (balance, open period, postable accounts).
        Exec(c, tx, "UPDATE journal_entries SET status=1, posted_by=@u, posted_at=CURRENT_TIMESTAMP WHERE journal_entry_id=@id AND status=0",
            ("@u", Trunc(i.User, 100)), ("@id", Id(c, jid.ToString())));
        return jid;
    }

    private static bool IsPg(DbConnection c) => c.GetType().Name.StartsWith("Npgsql", StringComparison.Ordinal);
    private static object Id(DbConnection c, string v) => IsPg(c) ? Guid.Parse(v) : v;
    private static object Date(DbConnection c, DateTime d)
        => IsPg(c) ? d.Date : d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    private static DbCommand Cmd(DbConnection c, DbTransaction tx, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
        return cmd;
    }

    private static object? Scalar(DbConnection c, DbTransaction tx, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, tx, sql, args);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : v;
    }

    private static void Exec(DbConnection c, DbTransaction tx, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(c, tx, sql, args);
        cmd.ExecuteNonQuery();
    }
}
