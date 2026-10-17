using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Infrastructure;

/// <summary>
/// Provider-neutral bank persistence for SQLite and PostgreSQL.
/// </summary>
public sealed class BankRepository : IBankRepository
{
    private readonly string _provider;
    private readonly string _connectionString;

    public BankRepository(string provider, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A database connection string is required.", nameof(connectionString));
        _provider = NormalizeProvider(provider);
        _connectionString = connectionString;
    }

    private static string NormalizeProvider(string provider) =>
        provider switch { "SQLite" => "SQLite", "PostgreSQL" => "PostgreSQL", _ => "SQLite" };

    private DbConnection CreateConnection() =>
        _provider == "PostgreSQL" ? new NpgsqlConnection(_connectionString) : new SqliteConnection(_connectionString);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private string FormatGuid(Guid guid) => _provider == "PostgreSQL" ? guid.ToString() : guid.ToString("D");

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        if (transaction != null)
            command.Transaction = transaction;
        return command;
    }

    public async Task<Guid> ResolveCompanyIdAsync(string companyCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyCode))
            throw new ArgumentException("Company code is required.", nameof(companyCode));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT company_id FROM companies WHERE company_code=@code AND is_active=" +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted=" +
            (_provider == "PostgreSQL" ? "false" : "0");

        Add(command, "@code", companyCode.Trim());
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

        if (value is null or DBNull || !Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var companyId) || companyId == Guid.Empty)
            throw new InvalidOperationException($"Active company '{companyCode.Trim()}' could not be resolved to a valid database company ID.");

        return companyId;
    }

    public async Task<Guid> SaveAsync(Bank bank, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // Check for duplicate code
            await using var checkCmd = CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM banks WHERE company_id=@company AND bank_code=@code AND is_deleted=" +
                (_provider == "PostgreSQL" ? "false" : "0"));
            Add(checkCmd, "@company", FormatGuid(bank.CompanyId));
            Add(checkCmd, "@code", bank.Code);

            if (Convert.ToInt64(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException($"Bank code '{bank.Code}' already exists for this company.");

            // Insert bank
            await using var insertCmd = CreateCommand(connection, transaction, @"
                INSERT INTO banks (
                    bank_id, company_id, bank_code, name, type,
                    gl_account_id, account_number, address, phone, is_active, is_deleted,
                    created_at, created_by, modified_at, modified_by
                ) VALUES (
                    @id, @company, @code, @name, @type,
                    @glAccountId, @accountNumber, @address, @phone, @isActive, @isDeleted,
                    @createdAt, @createdBy, @modifiedAt, @modifiedBy
                )");

            Add(insertCmd, "@id", FormatGuid(bank.Id));
            Add(insertCmd, "@company", FormatGuid(bank.CompanyId));
            Add(insertCmd, "@code", bank.Code);
            Add(insertCmd, "@name", bank.Name);
            Add(insertCmd, "@type", (int)bank.Type);
            Add(insertCmd, "@glAccountId", bank.GlAccountId.HasValue ? FormatGuid(bank.GlAccountId.Value) : DBNull.Value);
            Add(insertCmd, "@accountNumber", bank.AccountNumber);
            Add(insertCmd, "@address", bank.Address);
            Add(insertCmd, "@phone", bank.Phone);
            Add(insertCmd, "@isActive", bank.IsActive);
            Add(insertCmd, "@isDeleted", false);
            Add(insertCmd, "@createdAt", bank.CreatedAt);
            Add(insertCmd, "@createdBy", userId);
            Add(insertCmd, "@modifiedAt", bank.ModifiedAt);
            Add(insertCmd, "@modifiedBy", userId);

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return bank.Id;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<BankSnapshot?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT bank_id, company_id, bank_code, name, type,
                   gl_account_id, account_number, address, phone, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM banks
            WHERE bank_id = @id";

        Add(command, "@id", FormatGuid(id));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapBankSnapshot(reader);
    }

    public async Task<BankSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT bank_id, company_id, bank_code, name, type,
                   gl_account_id, account_number, address, phone, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM banks
            WHERE company_id = @company AND bank_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapBankSnapshot(reader);
    }

    public async Task<IReadOnlyList<BankSnapshot>> GetAllAsync(Guid companyId, CancellationToken ct = default)
    {
        var results = new List<BankSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT bank_id, company_id, bank_code, name, type,
                   gl_account_id, account_number, address, phone, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM banks
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            " ORDER BY name";

        Add(command, "@company", FormatGuid(companyId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapBankSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<bool> UpdateAsync(Bank bank, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var updateCmd = CreateCommand(connection, transaction, @"
                UPDATE banks SET
                    name = @name,
                    type = @type,
                    gl_account_id = @glAccountId,
                    account_number = @accountNumber,
                    address = @address,
                    phone = @phone,
                    is_active = @isActive,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE bank_id = @id");

            Add(updateCmd, "@id", FormatGuid(bank.Id));
            Add(updateCmd, "@name", bank.Name);
            Add(updateCmd, "@type", (int)bank.Type);
            Add(updateCmd, "@glAccountId", bank.GlAccountId.HasValue ? FormatGuid(bank.GlAccountId.Value) : DBNull.Value);
            Add(updateCmd, "@accountNumber", bank.AccountNumber);
            Add(updateCmd, "@address", bank.Address);
            Add(updateCmd, "@phone", bank.Phone);
            Add(updateCmd, "@isActive", bank.IsActive);
            Add(updateCmd, "@modifiedAt", bank.ModifiedAt);
            Add(updateCmd, "@modifiedBy", userId);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return rowsAffected > 0;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, string deletedBy, string? reason = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deletedBy))
            throw new ArgumentException("Deleted by user ID is required.", nameof(deletedBy));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var deleteCmd = CreateCommand(connection, transaction, @"
                UPDATE banks SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy
                WHERE bank_id = @id AND is_deleted = " +
                (_provider == "PostgreSQL" ? "false" : "0"));

            Add(deleteCmd, "@id", FormatGuid(id));
            Add(deleteCmd, "@isDeleted", true);
            Add(deleteCmd, "@deletedAt", DateTime.UtcNow);
            Add(deleteCmd, "@deletedBy", deletedBy);

            var rowsAffected = await deleteCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return rowsAffected > 0;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> RestoreAsync(Guid id, string restoredBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(restoredBy))
            throw new ArgumentException("Restored by user ID is required.", nameof(restoredBy));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var restoreCmd = CreateCommand(connection, transaction, @"
                UPDATE banks SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE bank_id = @id AND is_deleted = " +
                (_provider == "PostgreSQL" ? "true" : "1"));

            Add(restoreCmd, "@id", FormatGuid(id));
            Add(restoreCmd, "@isDeleted", false);
            Add(restoreCmd, "@deletedAt", DBNull.Value);
            Add(restoreCmd, "@deletedBy", DBNull.Value);
            Add(restoreCmd, "@modifiedAt", DateTime.UtcNow);
            Add(restoreCmd, "@modifiedBy", restoredBy);

            var rowsAffected = await restoreCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return rowsAffected > 0;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<bool> CodeExistsAsync(Guid companyId, string code, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var sql = @"
            SELECT COUNT(*)
            FROM banks
            WHERE company_id = @company AND bank_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        if (excludeId != null)
            sql += " AND bank_id != @excludeId";

        command.CommandText = sql;
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        if (excludeId != null)
            Add(command, "@excludeId", FormatGuid(excludeId.Value));

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return count > 0;
    }

    public async Task<BankQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        BankType? type = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var results = new List<BankSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var whereClauses = new List<string>();
        if (!includeDeleted)
            whereClauses.Add("is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));
        if (code != null)
            whereClauses.Add("bank_code LIKE @code");
        if (name != null)
            whereClauses.Add("name LIKE @name");
        if (type != null)
            whereClauses.Add("type = @type");
        if (isActive != null)
            whereClauses.Add("is_active = @isActive");

        // Count query
        await using var countCmd = connection.CreateCommand();
        var countSql = @"SELECT COUNT(*) FROM banks WHERE company_id = @company";
        if (whereClauses.Count > 0)
            countSql += " AND " + string.Join(" AND ", whereClauses);

        countCmd.CommandText = countSql;
        Add(countCmd, "@company", FormatGuid(companyId));
        if (code != null) Add(countCmd, "@code", $"%{code}%");
        if (name != null) Add(countCmd, "@name", $"%{name}%");
        if (type != null) Add(countCmd, "@type", (int)type);
        if (isActive != null) Add(countCmd, "@isActive", isActive);

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Data query
        await using var dataCmd = connection.CreateCommand();
        var dataSql = @"
            SELECT bank_id, company_id, bank_code, name, type,
                   gl_account_id, account_number, address, phone, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM banks
            WHERE company_id = @company";

        if (whereClauses.Count > 0)
            dataSql += " AND " + string.Join(" AND ", whereClauses);

        dataSql += " ORDER BY name";
        if (_provider == "PostgreSQL")
            dataSql += " OFFSET @offset LIMIT @limit";
        else
            dataSql += " LIMIT @limit OFFSET @offset";

        dataCmd.CommandText = dataSql;
        Add(dataCmd, "@company", FormatGuid(companyId));
        if (code != null) Add(dataCmd, "@code", $"%{code}%");
        if (name != null) Add(dataCmd, "@name", $"%{name}%");
        if (type != null) Add(dataCmd, "@type", (int)type);
        if (isActive != null) Add(dataCmd, "@isActive", isActive);
        Add(dataCmd, "@offset", (pageNumber - 1) * pageSize);
        Add(dataCmd, "@limit", pageSize);

        await using var reader = await dataCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapBankSnapshot(reader));

        return new BankQueryResult(results.AsReadOnly(), totalCount);
    }

    public async Task<IReadOnlyList<BankSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        var results = new List<BankSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT bank_id, company_id, bank_code, name, type,
                   gl_account_id, account_number, address, phone, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM banks
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            @" AND (bank_code LIKE @search OR name LIKE @search OR account_number LIKE @search)
            ORDER BY name
            LIMIT @limit";

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@search", $"%{searchTerm}%");
        Add(command, "@limit", maxResults);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapBankSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT COUNT(*)
            FROM banks
            WHERE company_id = @company" +
            (includeDeleted ? "" : " AND is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));

        Add(command, "@company", FormatGuid(companyId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private BankSnapshot MapBankSnapshot(DbDataReader reader)
    {
        var bankId = Guid.Parse(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        var companyId = Guid.Parse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!);
        var code = reader.GetString(2);
        var name = reader.GetString(3);
        var type = (BankType)reader.GetInt32(4);
        Guid? glAccountId = reader.IsDBNull(5) ? null : Guid.Parse(Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture)!);
        var accountNumber = reader.GetString(6);
        var address = reader.GetString(7);
        var phone = reader.GetString(8);
        var isActive = reader.GetBoolean(9);
        var isDeleted = reader.GetBoolean(10);
        var createdAt = _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(11)
            : DateTime.Parse(Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        Guid? createdBy = reader.IsDBNull(12) ? null : Guid.Parse(Convert.ToString(reader.GetValue(12), CultureInfo.InvariantCulture)!);
        DateTime? modifiedAt = reader.IsDBNull(13) ? null : _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(13)
            : DateTime.Parse(Convert.ToString(reader.GetValue(13), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        Guid? modifiedBy = reader.IsDBNull(14) ? null : Guid.Parse(Convert.ToString(reader.GetValue(14), CultureInfo.InvariantCulture)!);

        return new BankSnapshot(
            bankId,
            companyId,
            code,
            name,
            type,
            glAccountId,
            accountNumber,
            address,
            phone,
            isActive,
            isDeleted,
            createdAt,
            createdBy,
            modifiedAt,
            modifiedBy);
    }
}

/// <summary>
/// Factory for creating BankRepository instances.
/// </summary>
public static class BankRepositoryFactory
{
    public static BankRepository Create(string provider, string connectionString) =>
        new BankRepository(provider, connectionString);
}

/// <summary>
/// Extensions for BankRepository.
/// </summary>
public static class BankRepositoryExtensions
{
    public static async Task<Bank?> FindByIdOrNullAsync(
        this IBankRepository repository,
        Guid id,
        CancellationToken ct = default)
    {
        var snapshot = await repository.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (snapshot == null)
            return null;

        return Bank.Rehydrate(snapshot);
    }
}
