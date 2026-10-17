#nullable enable

using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;

namespace ShouTech.App.Services;

/// <summary>Persisted monthly sales target; actual sales and earned commission are intentionally not stored here.</summary>
public sealed record SalesRepTargetRecord
{
    public string PeriodCode { get; init; } = string.Empty;
    public decimal TargetAmount { get; init; }
    public string Notes { get; init; } = string.Empty;
}

/// <summary>
/// Company-scoped monthly targets for sales representatives. Actual sales and commissions must be
/// calculated from posted commercial documents, never inferred from a target or a default value.
/// </summary>
public static class SalesRepTargetSvc
{
    private static bool IsPostgreSql => DatabaseRuntime.IsPostgreSql();
    private static string FalseLiteral => IsPostgreSql ? "FALSE" : "0";
    private static string TrueLiteral => IsPostgreSql ? "TRUE" : "1";
    private static readonly Regex PeriodPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<IReadOnlyList<SalesRepTargetRecord>> LoadAsync(
        Guid companyId, Guid representativeId, CancellationToken cancellationToken = default)
    {
        ValidateIds(companyId, representativeId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT period_code, target_amount, notes
            FROM sales_rep_targets
            WHERE company_id=@company AND rep_id=@rep AND is_deleted={FalseLiteral}
            ORDER BY period_code DESC;
            """;
        Add(command, "@company", FormatId(companyId));
        Add(command, "@rep", FormatId(representativeId));
        var results = new List<SalesRepTargetRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new SalesRepTargetRecord
            {
                PeriodCode = ReadString(reader, 0),
                TargetAmount = ReadDecimal(reader, 1),
                Notes = ReadString(reader, 2)
            });
        }
        return results.AsReadOnly();
    }

    /// <summary>
    /// Saves the supplied period rows atomically. Periods omitted from the collection are preserved;
    /// this method never touches invoices, posted sales, or earned commissions.
    /// </summary>
    public static async Task SaveAsync(
        Guid companyId,
        Guid representativeId,
        IEnumerable<SalesRepTargetRecord> targets,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateIds(companyId, representativeId);
        ArgumentNullException.ThrowIfNull(targets);
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("An audit actor is required.", nameof(actor));

        var rows = targets.ToArray();
        if (rows.Length > 240)
            throw new ArgumentOutOfRangeException(nameof(targets), "No more than 240 monthly targets may be saved in one operation.");

        var seenPeriods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is null || !PeriodPattern.IsMatch(row.PeriodCode))
                throw new ArgumentException("Each target must use a valid YYYY-MM period code.", nameof(targets));
            if (!seenPeriods.Add(row.PeriodCode))
                throw new ArgumentException($"Duplicate target period: {row.PeriodCode}.", nameof(targets));
            if (row.TargetAmount < 0 || row.TargetAmount > 999999999999999.9999m ||
                decimal.Round(row.TargetAmount, 4) != row.TargetAmount)
                throw new ArgumentOutOfRangeException(nameof(targets), "Monthly targets must be non-negative and have no more than four decimal places.");
            if (row.Notes.Trim().Length > 500)
                throw new ArgumentOutOfRangeException(nameof(targets), "Target notes cannot exceed 500 characters.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var validate = connection.CreateCommand())
            {
                validate.Transaction = transaction;
                validate.CommandText = $"""
                    SELECT COUNT(*) FROM sales_representatives
                    WHERE company_id=@company AND rep_id=@rep AND is_deleted={FalseLiteral};
                    """;
                Add(validate, "@company", FormatId(companyId));
                Add(validate, "@rep", FormatId(representativeId));
                var found = Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                    CultureInfo.InvariantCulture);
                if (found != 1)
                    throw new InvalidOperationException("The sales representative was not found in the selected company.");
            }

            foreach (var row in rows)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO sales_rep_targets
                        (target_id, company_id, rep_id, period_code, target_amount, notes,
                         is_deleted, created_at, created_by, modified_at, modified_by)
                    VALUES
                        (@id, @company, @rep, @period, @amount, @notes,
                         {FalseLiteral}, @createdAt, @createdBy, NULL, NULL)
                    ON CONFLICT (company_id, rep_id, period_code) DO UPDATE SET
                        target_amount=excluded.target_amount,
                        notes=excluded.notes,
                        is_deleted={FalseLiteral},
                        modified_at=excluded.created_at,
                        modified_by=excluded.created_by;
                    """;
                Add(command, "@id", FormatId(Guid.NewGuid()));
                Add(command, "@company", FormatId(companyId));
                Add(command, "@rep", FormatId(representativeId));
                Add(command, "@period", row.PeriodCode);
                Add(command, "@amount", row.TargetAmount);
                Add(command, "@notes", row.Notes.Trim());
                Add(command, "@createdAt", TimestampValue());
                Add(command, "@createdBy", actor.Trim());
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("SalesRepTarget", "Save",
                $"CompanyId={companyId:D}; RepresentativeId={representativeId:D}; PeriodCount={rows.Length}; Outcome=Committed");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static void ValidateIds(Guid companyId, Guid representativeId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("A valid company ID is required.", nameof(companyId));
        if (representativeId == Guid.Empty)
            throw new ArgumentException("A valid representative ID is required.", nameof(representativeId));
    }

    private static async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = DatabaseRuntime.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("The active database connection is not configured.");

        DbConnection connection = IsPostgreSql
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(DatabaseRuntime.NormalizeSqliteConnectionString(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static object FormatId(Guid value) =>
        IsPostgreSql ? value : value.ToString("D", CultureInfo.InvariantCulture);

    private static object TimestampValue() =>
        IsPostgreSql ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static string ReadString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty :
        Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;

    private static decimal ReadDecimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0m :
        Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
