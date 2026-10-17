using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App.Views;

namespace ShouTech.App.Services;

/// <summary>
/// Per-company enterprise feature flags stored in company_features.
/// Language keys live under the "Enterprise" section of the language files.
/// </summary>
public static class FeatSvc
{
    public const string AuditTrail = "AuditTrail";
    public const string MultiCurrency = "MultiCurrency";
    public const string MultiBranch = "MultiBranch";
    public const string ApprovalWorkflow = "ApprovalWorkflow";

    public static readonly IReadOnlyList<string> Keys =
        new[] { AuditTrail, MultiCurrency, MultiBranch, ApprovalWorkflow };

    public static bool IsEnabled(string key) => Load().TryGetValue(key, out var v) && v;

    public static IReadOnlyDictionary<string, bool> Load()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in Keys) result[k] = false;
        var cs = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return result;
        try
        {
            using var connection = Open(cs);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT feature_key, is_enabled FROM company_features WHERE company_id=@c;";
            Add(cmd, "@c", CompanyParam());
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result[r.GetString(0)] = Convert.ToBoolean(r.GetValue(1));
        }
        catch
        {
            // Table missing or unreachable: all features stay disabled.
        }
        return result;
    }

    public static bool Set(string key, bool enabled)
    {
        if (!Keys.Contains(key)) return false;
        var cs = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs)) return false;
        try
        {
            using var connection = Open(cs);
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO company_features (company_id, feature_key, is_enabled, modified_at, modified_by)
                VALUES (@c, @k, @e, @t, @u)
                ON CONFLICT (company_id, feature_key)
                DO UPDATE SET is_enabled = excluded.is_enabled, modified_at = excluded.modified_at, modified_by = excluded.modified_by;
                """;
            Add(cmd, "@c", CompanyParam());
            Add(cmd, "@k", key);
            Add(cmd, "@e", DatabaseRuntime.IsPostgreSql() ? enabled : (enabled ? 1 : 0));
            Add(cmd, "@t", DatabaseRuntime.IsPostgreSql() ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.ToString("o"));
            Add(cmd, "@u", Environment.UserName);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch
        {
            return false;
        }
    }

    private static object CompanyParam()
    {
        var id = AppState.CurrentCompanyCode ?? "";
        return DatabaseRuntime.IsPostgreSql() && Guid.TryParse(id, out var g) ? g : id;
    }

    private static DbConnection Open(string connectionString)
    {
        DbConnection connection = DatabaseRuntime.IsPostgreSql()
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(connectionString));
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
