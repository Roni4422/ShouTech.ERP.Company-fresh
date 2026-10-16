using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Infrastructure;

/// <summary>
/// Shared currency definitions and company-scoped, append-only daily exchange-rate history.
/// The main rate is expressed as company base-currency units per one unit of the selected currency.
/// </summary>
public sealed class CurrencyRepository : ICurrencyRepository
{
    private readonly string _provider;
    private readonly string _connectionString;

    public CurrencyRepository(string provider, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A database connection string is required.", nameof(connectionString));
        _provider = NormalizeProvider(provider);
        _connectionString = connectionString;
    }

    public async Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new ArgumentException("Company code is required.", nameof(companyCode));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT company_id FROM companies WHERE company_code=@code AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(command, "@code", companyCode.Trim());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull || !Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var companyId) || companyId == Guid.Empty)
            throw new InvalidOperationException($"Active company '{companyCode.Trim()}' could not be resolved in the current database.");
        return companyId;
    }

    public async Task<string> GetCompanyBaseCurrencyCodeAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT currency_code FROM companies WHERE company_id=@company AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(command, "@company", FormatGuid(companyId));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var code = value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            throw new InvalidOperationException("The active company has no valid three-letter base currency configured.");
        return code.ToUpperInvariant();
    }

    public async Task<CurrencySnapshot?> LoadAsync(Guid companyId, string currencyCode, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        var code = NormalizeCurrencyCode(currencyCode);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var baseCurrency = await GetCompanyBaseCurrencyAsync(connection, companyId, transaction: null, cancellationToken).ConfigureAwait(false);
        var definition = await ReadDefinitionAsync(connection, transaction: null, code, activeOnly: true, cancellationToken).ConfigureAwait(false);
        if (definition is null)
            return null;

        if (await ReadDefinitionAsync(connection, transaction: null, baseCurrency, activeOnly: true, cancellationToken).ConfigureAwait(false) is null)
            throw new InvalidOperationException($"Company base currency '{baseCurrency}' is not present or is inactive in the currency catalog.");

        var rates = new List<CurrencyRateSnapshot>();
        if (!string.Equals(code, baseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT rate_id, effective_date, rate, buy_rate, sell_rate, min_rate, max_rate, notes, created_by
                FROM currency_rates
                WHERE company_id=@company AND from_currency=@from AND to_currency=@to
                ORDER BY effective_date DESC, rate_id DESC
                """;
            Add(command, "@company", FormatGuid(companyId));
            Add(command, "@from", code);
            Add(command, "@to", baseCurrency);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rates.Add(new CurrencyRateSnapshot(
                    Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                    ReadDate(reader, 1),
                    Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                    Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        return new CurrencySnapshot(definition, baseCurrency, rates.AsReadOnly());
    }

    public async Task<int> SaveAsync(
        Guid companyId,
        CurrencyDefinition definition,
        IReadOnlyCollection<CurrencyRateDraft> newRates,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(newRates);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Actor is required.", nameof(actor));
        if (newRates.Count > 0 && string.Equals(definition.Code, definition.Code, StringComparison.OrdinalIgnoreCase) is false)
            throw new InvalidOperationException("Currency code normalization failed.");

        var duplicateDates = newRates.GroupBy(rate => rate.EffectiveDate).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateDates.Length > 0)
            throw new InvalidOperationException($"Only one exchange-rate observation per currency and date is permitted. Duplicate date: {duplicateDates[0]:yyyy-MM-dd}.");

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureActiveCompanyAsync(connection, transaction, companyId, cancellationToken).ConfigureAwait(false);
            var baseCurrency = await GetCompanyBaseCurrencyAsync(connection, companyId, transaction, cancellationToken).ConfigureAwait(false);
            await EnsureActiveCurrencyAsync(connection, transaction, baseCurrency, cancellationToken).ConfigureAwait(false);

            var existingActive = await CurrencyExistsAsync(connection, transaction, definition.Code, activeOnly: true, cancellationToken).ConfigureAwait(false);
            var existingAny = await CurrencyExistsAsync(connection, transaction, definition.Code, activeOnly: false, cancellationToken).ConfigureAwait(false);
            if (existingAny && !existingActive)
                throw new InvalidOperationException($"Currency '{definition.Code}' is inactive. Reactivate it through the authorized currency administration workflow before editing it.");

            var eventType = existingActive ? "UPDATED" : "CREATED";
            if (existingActive)
            {
                await using var update = CreateCommand(connection, transaction, """
                    UPDATE currencies
                    SET currency_name_ar=@nameAr, currency_name_en=@nameEn, symbol=@symbol,
                        fraction_name=@fraction, decimal_places=@decimals, updated_at=CURRENT_TIMESTAMP
                    WHERE currency_code=@code AND is_active=
                    """ + (_provider == "PostgreSQL" ? "true" : "1"));
                Add(update, "@nameAr", definition.NameArabic);
                Add(update, "@nameEn", DbValue(definition.NameEnglish));
                Add(update, "@symbol", DbValue(definition.Symbol));
                Add(update, "@fraction", DbValue(definition.FractionName));
                Add(update, "@decimals", definition.DecimalPlaces);
                Add(update, "@code", definition.Code);
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The currency changed or was deactivated while it was being saved.");
            }
            else
            {
                await using var insert = CreateCommand(connection, transaction, """
                    INSERT INTO currencies(currency_code, currency_name_ar, currency_name_en, symbol, fraction_name, decimal_places)
                    VALUES(@code, @nameAr, @nameEn, @symbol, @fraction, @decimals)
                    """);
                Add(insert, "@code", definition.Code);
                Add(insert, "@nameAr", definition.NameArabic);
                Add(insert, "@nameEn", DbValue(definition.NameEnglish));
                Add(insert, "@symbol", DbValue(definition.Symbol));
                Add(insert, "@fraction", DbValue(definition.FractionName));
                Add(insert, "@decimals", definition.DecimalPlaces);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await AddCurrencyAuditAsync(
                connection, transaction, definition.Code, eventType, actor,
                $"NameAr={definition.NameArabic}; NameEn={definition.NameEnglish ?? "(null)"}; DecimalPlaces={definition.DecimalPlaces}",
                cancellationToken).ConfigureAwait(false);

            if (newRates.Count > 0 && string.Equals(definition.Code, baseCurrency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{definition.Code}' is the active company's base currency ({baseCurrency}); an exchange-rate history against itself is not valid.");

            foreach (var rate in newRates.OrderBy(item => item.EffectiveDate))
            {
                await using (var duplicate = CreateCommand(connection, transaction, """
                    SELECT COUNT(*) FROM currency_rates
                    WHERE company_id=@company AND from_currency=@from AND to_currency=@to AND effective_date=@date
                    """))
                {
                    Add(duplicate, "@company", FormatGuid(companyId));
                    Add(duplicate, "@from", definition.Code);
                    Add(duplicate, "@to", baseCurrency);
                    Add(duplicate, "@date", DateValue(rate.EffectiveDate));
                    if (Convert.ToInt64(await duplicate.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0)
                        throw new InvalidOperationException($"An exchange rate for {definition.Code}/{baseCurrency} already exists on {rate.EffectiveDate:yyyy-MM-dd}. Historical rates are append-only; use a new effective date.");
                }

                await using var insertRate = CreateCommand(connection, transaction, """
                    INSERT INTO currency_rates
                        (company_id, from_currency, to_currency, rate, buy_rate, sell_rate,
                         min_rate, max_rate, effective_date, notes, created_by)
                    VALUES
                        (@company, @from, @to, @rate, @buy, @sell, @min, @max, @date, @notes, @actor)
                    """);
                Add(insertRate, "@company", FormatGuid(companyId));
                Add(insertRate, "@from", definition.Code);
                Add(insertRate, "@to", baseCurrency);
                Add(insertRate, "@rate", rate.Rate);
                Add(insertRate, "@buy", rate.BuyRate);
                Add(insertRate, "@sell", rate.SellRate);
                Add(insertRate, "@min", rate.MinRate);
                Add(insertRate, "@max", rate.MaxRate);
                Add(insertRate, "@date", DateValue(rate.EffectiveDate));
                Add(insertRate, "@notes", DbValue(rate.Notes));
                Add(insertRate, "@actor", actor.Trim());
                await insertRate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await AddCurrencyAuditAsync(
                    connection, transaction, definition.Code, "RATE_ADDED", actor,
                    $"Pair={definition.Code}/{baseCurrency}; Date={rate.EffectiveDate:yyyy-MM-dd}; Rate={rate.Rate.ToString(CultureInfo.InvariantCulture)}",
                    cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return newRates.Count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<string> GetCompanyBaseCurrencyAsync(
        DbConnection connection,
        Guid companyId,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = transaction is null ? connection.CreateCommand() : CreateCommand(connection, transaction, string.Empty);
        command.CommandText = "SELECT currency_code FROM companies WHERE company_id=@company AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(command, "@company", FormatGuid(companyId));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var code = value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            throw new InvalidOperationException("The active company has no valid three-letter base currency configured.");
        return code.ToUpperInvariant();
    }

    private async Task<CurrencyDefinition?> ReadDefinitionAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string code,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        await using var command = transaction is null ? connection.CreateCommand() : CreateCommand(connection, transaction, string.Empty);
        command.CommandText = """
            SELECT currency_code, currency_name_ar, currency_name_en, symbol, fraction_name, decimal_places
            FROM currencies WHERE currency_code=@code
            """ + (activeOnly ? " AND is_active=" + (_provider == "PostgreSQL" ? "true" : "1") : string.Empty);
        Add(command, "@code", code);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;
        return new CurrencyDefinition(
            reader.GetString(0).Trim(),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            Convert.ToInt32(reader.GetValue(5), CultureInfo.InvariantCulture));
    }

    private async Task<bool> CurrencyExistsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string code,
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT COUNT(*) FROM currencies WHERE currency_code=@code" +
            (activeOnly ? " AND is_active=" + (_provider == "PostgreSQL" ? "true" : "1") : string.Empty));
        Add(command, "@code", code);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
    }

    private async Task EnsureActiveCurrencyAsync(
        DbConnection connection,
        DbTransaction transaction,
        string code,
        CancellationToken cancellationToken)
    {
        if (!await CurrencyExistsAsync(connection, transaction, code, activeOnly: true, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"Company base currency '{code}' is missing or inactive in the currency catalog. Add/reactivate the base currency before saving exchange rates.");
    }

    private async Task EnsureActiveCompanyAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction,
            "SELECT COUNT(*) FROM companies WHERE company_id=@company AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0"));
        Add(command, "@company", FormatGuid(companyId));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("The selected company is not active in the current database.");
    }

    private async Task AddCurrencyAuditAsync(
        DbConnection connection,
        DbTransaction transaction,
        string currencyCode,
        string eventType,
        string actor,
        string details,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO currency_audit_events(currency_code, event_type, actor, details)
            VALUES(@code, @type, @actor, @details)
            """);
        Add(command, "@code", currencyCode);
        Add(command, "@type", eventType);
        Add(command, "@actor", actor.Trim());
        Add(command, "@details", details.Length <= 1000 ? details : details[..1000]);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            if (_provider == "SQLite")
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA foreign_keys = ON";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private DbConnection CreateConnection() => _provider == "SQLite"
        ? new SqliteConnection(_connectionString)
        : new NpgsqlConnection(_connectionString);

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private object FormatGuid(Guid value) => _provider == "PostgreSQL" ? value : value.ToString("D");
    private object DateValue(DateOnly value) => _provider == "PostgreSQL" ? value : value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private DateOnly ReadDate(DbDataReader reader, int ordinal) => _provider == "PostgreSQL"
        ? reader.GetFieldValue<DateOnly>(ordinal)
        : DateOnly.Parse(Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

    private static string NormalizeCurrencyCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsAsciiLetter))
            throw new ArgumentException("Currency code must be a three-letter ASCII ISO code.", nameof(code));
        return code.Trim().ToUpperInvariant();
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "sqlite" or "sqlite3" => "SQLite",
        "postgres" or "postgresql" or "npgsql" => "PostgreSQL",
        _ => throw new NotSupportedException($"Unsupported database provider '{provider}'. Use SQLite or PostgreSQL.")
    };
}
