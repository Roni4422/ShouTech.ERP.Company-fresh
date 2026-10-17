using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App.Views;

namespace ShouTech.App.Services;

/// <summary>
/// Company-scoped master-data lists stored in lookup_items (migration 024).
/// Ribbon keys are mapped to a list kind; LkpWin edits the list.
/// </summary>
public static class LkpSvc
{
    public sealed class Row
    {
        public string Code { get; set; } = "";
        public string NameAr { get; set; } = "";
        public string NameEn { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }

    private static readonly Dictionary<string, string> KeyToKind = new(StringComparer.Ordinal)
    {
        ["Customers.Areas"] = "cust_area",
        ["Customers.CustomerGroups"] = "cust_group",
        ["SalesRep.SalesRepGroups"] = "rep_group",
        ["Bonds.DefinePaymentTypes"] = "pay_method",
        ["Bonds.PaymentTypes"] = "pay_method",
        ["OtherTools.PaymentMethods"] = "pay_method",
        ["View.PaymentMethods"] = "pay_method",
        ["Bonds.DefineVoucherTypes"] = "vch_type",
        ["Bonds.DefineSecurities"] = "security_type",
        ["Invoices.DefineInvoices"] = "inv_type",
        ["Orders.DefineOrders"] = "ord_type",
        ["POS.DefinePOS"] = "pos_term",
        ["OtherTools.UnitsOfMeasure"] = "uom",
        ["OtherTools.CustomFields"] = "custom_field",
        ["OtherTools.DriverCard"] = "driver",
    };

    public static bool TryGetKind(string? key, out string kind)
    {
        kind = "";
        return !string.IsNullOrWhiteSpace(key) && KeyToKind.TryGetValue(key.Trim(), out kind!);
    }

    public static IReadOnlyList<Row> Load(string kind)
    {
        var list = new List<Row>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"""
            SELECT code, name_ar, name_en, is_active FROM lookup_items
            WHERE company_id = {CompanySql()} AND kind=@k AND is_deleted = {False()}
            ORDER BY code;
            """;
        AddCompany(cmd);
        Add(cmd, "@k", kind);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Row
            {
                Code = r.GetString(0),
                NameAr = r.GetString(1),
                NameEn = r.GetString(2),
                IsActive = Convert.ToBoolean(r.GetValue(3), CultureInfo.InvariantCulture)
            });
        return list;
    }

    /// <summary>Upserts rows (blank codes are rejected) and soft-deletes kind rows missing from the list.</summary>
    public static void Save(string kind, IReadOnlyList<Row> rows, string user)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            row.Code = row.Code.Trim();
            if (row.Code.Length == 0) throw new InvalidOperationException("Code is required.");
            if (!seen.Add(row.Code)) throw new InvalidOperationException($"Duplicate code: {row.Code}");
        }

        using var c = Open();
        using var tx = c.BeginTransaction();
        var pg = DatabaseRuntime.IsPostgreSql();
        var now = DateTimeOffset.UtcNow;

        foreach (var row in rows)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"""
                INSERT INTO lookup_items (item_id, company_id, kind, code, name_ar, name_en, is_active, is_deleted, created_at, created_by)
                VALUES (@id, {CompanySql()}, @k, @code, @ar, @en, @act, {False()}, @t, @u)
                ON CONFLICT (company_id, kind, code) DO UPDATE SET
                    name_ar = excluded.name_ar, name_en = excluded.name_en, is_active = excluded.is_active,
                    is_deleted = {False()}, modified_at = excluded.created_at, modified_by = excluded.created_by;
                """;
            Add(cmd, "@id", pg ? Guid.NewGuid() : Guid.NewGuid().ToString());
            AddCompany(cmd);
            Add(cmd, "@k", kind);
            Add(cmd, "@code", row.Code);
            Add(cmd, "@ar", row.NameAr ?? "");
            Add(cmd, "@en", row.NameEn ?? "");
            Add(cmd, "@act", pg ? row.IsActive : (row.IsActive ? 1 : 0));
            Add(cmd, "@t", pg ? now : now.ToString("o", CultureInfo.InvariantCulture));
            Add(cmd, "@u", user);
            cmd.ExecuteNonQuery();
        }

        foreach (var existing in Load(kind, tx, c))
        {
            if (seen.Contains(existing)) continue;
            using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = $"""
                UPDATE lookup_items SET is_deleted = {True()}, modified_by=@u
                WHERE company_id = {CompanySql()} AND kind=@k AND code=@code;
                """;
            Add(del, "@u", user);
            AddCompany(del);
            Add(del, "@k", kind);
            Add(del, "@code", existing);
            del.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static List<string> Load(string kind, DbTransaction tx, DbConnection c)
    {
        var codes = new List<string>();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT code FROM lookup_items WHERE company_id = {CompanySql()} AND kind=@k AND is_deleted = {False()};";
        AddCompany(cmd);
        Add(cmd, "@k", kind);
        using var r = cmd.ExecuteReader();
        while (r.Read()) codes.Add(r.GetString(0));
        return codes;
    }

    private static string False() => DatabaseRuntime.IsPostgreSql() ? "false" : "0";
    private static string True() => DatabaseRuntime.IsPostgreSql() ? "true" : "1";
    private static string CompanySql() => "(SELECT company_id FROM companies WHERE company_code = @company)";

    private static void AddCompany(DbCommand cmd) => Add(cmd, "@company", AppState.CurrentCompanyCode ?? "");

    private static DbConnection Open()
    {
        var cs = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("No database connection is configured.");
        DbConnection connection = DatabaseRuntime.IsPostgreSql()
            ? new NpgsqlConnection(cs)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(cs));
        connection.Open();
        return connection;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        command.Parameters.Add(p);
    }
}
