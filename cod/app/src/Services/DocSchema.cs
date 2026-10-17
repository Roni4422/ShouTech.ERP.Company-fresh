using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace ShouTech.App.Services;

/// <summary>
/// Validates the commercial-document schema provisioned by DBA migrations.
/// Schema definitions belong exclusively in dba/mig; this service performs no DDL.
/// </summary>
public static class DocSchema
{
    // Schema validation is scoped to the active provider and connection fingerprint.
    // A single process may switch companies/databases; one successful check must never
    // authorize invoice operations against a different, unvalidated database.
    private static readonly ConcurrentDictionary<string, byte> ValidatedDatabases =
        new(StringComparer.Ordinal);

    public static void Ensure()
    {
        var cs = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("No database connection is configured.");

        var isPostgreSql = DatabaseRuntime.IsPostgreSql();
        var fingerprintBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            (isPostgreSql ? "postgresql\n" : "sqlite\n") + cs));
        var fingerprint = Convert.ToHexString(fingerprintBytes);
        if (ValidatedDatabases.ContainsKey(fingerprint))
            return;

        using var connection = Open(cs, isPostgreSql);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = isPostgreSql
                ? """
                  SELECT COUNT(*)
                  FROM information_schema.tables
                  WHERE table_schema = current_schema()
                    AND table_name IN ('sales_documents', 'sales_document_lines',
                      'purchase_documents', 'purchase_document_lines', 'party_ledger', 'stock_movements')
                  """
                : """
                  SELECT COUNT(*)
                  FROM sqlite_master
                  WHERE type = 'table'
                    AND name IN ('sales_documents', 'sales_document_lines',
                      'purchase_documents', 'purchase_document_lines', 'party_ledger', 'stock_movements')
                  """;
        var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        if (count != 6)
            throw new InvalidOperationException(
                "Commercial-document schema is incomplete. Apply the provider migrations under dba/mig before using invoices.");

        // Cache only after a successful complete schema check.
        ValidatedDatabases.TryAdd(fingerprint, 0);
    }

    private static DbConnection Open(string connectionString, bool isPostgreSql)
    {
        DbConnection connection = isPostgreSql
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(connectionString));
        connection.Open();
        return connection;
    }
}
