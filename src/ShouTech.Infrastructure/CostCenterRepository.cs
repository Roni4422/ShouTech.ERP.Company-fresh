using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Infrastructure;

/// <summary>
/// Provider-neutral cost center persistence for SQLite and PostgreSQL.
/// </summary>
public sealed class CostCenterRepository : ICostCenterRepository
{
    private readonly string _provider;
    private readonly string _connectionString;

    public CostCenterRepository(string provider, string connectionString)
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

    public async Task<Guid> SaveAsync(CostCenter costCenter, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(costCenter);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // Check for duplicate code
            await using var checkCmd = CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM cost_centers WHERE company_id=@company AND cost_center_code=@code AND is_deleted=" +
                (_provider == "PostgreSQL" ? "false" : "0"));
            Add(checkCmd, "@company", FormatGuid(costCenter.CompanyId));
            Add(checkCmd, "@code", costCenter.Code);

            if (Convert.ToInt64(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException($"Cost center code '{costCenter.Code}' already exists for this company.");

            // Insert cost center
            await using var insertCmd = CreateCommand(connection, transaction, @"
                INSERT INTO cost_centers (
                    cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                    is_active, is_deleted,
                    created_at, created_by, modified_at, modified_by
                ) VALUES (
                    @id, @company, @code, @nameAr, @nameEn, @description,
                    @isActive, @isDeleted,
                    @createdAt, @createdBy, @modifiedAt, @modifiedBy
                )");

            Add(insertCmd, "@id", FormatGuid(costCenter.Id));
            Add(insertCmd, "@company", FormatGuid(costCenter.CompanyId));
            Add(insertCmd, "@code", costCenter.Code);
            Add(insertCmd, "@nameAr", costCenter.NameAr);
            Add(insertCmd, "@nameEn", costCenter.NameEn);
            Add(insertCmd, "@description", costCenter.Description);
            Add(insertCmd, "@isActive", costCenter.IsActive);
            Add(insertCmd, "@isDeleted", false);
            Add(insertCmd, "@createdAt", costCenter.CreatedAt);
            Add(insertCmd, "@createdBy", userId);
            Add(insertCmd, "@modifiedAt", costCenter.ModifiedAt);
            Add(insertCmd, "@modifiedBy", userId);

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return costCenter.Id;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CostCenterSnapshot?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                   is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM cost_centers
            WHERE cost_center_id = @id";

        Add(command, "@id", FormatGuid(id));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapCostCenterSnapshot(reader);
    }

    public async Task<CostCenterSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                   is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM cost_centers
            WHERE company_id = @company AND cost_center_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapCostCenterSnapshot(reader);
    }

    public async Task<IReadOnlyList<CostCenterSnapshot>> GetAllAsync(Guid companyId, CancellationToken ct = default)
    {
        var results = new List<CostCenterSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                   is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM cost_centers
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            " ORDER BY name_ar";

        Add(command, "@company", FormatGuid(companyId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCostCenterSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<bool> UpdateAsync(CostCenter costCenter, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(costCenter);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var updateCmd = CreateCommand(connection, transaction, @"
                UPDATE cost_centers SET
                    name_ar = @nameAr,
                    name_en = @nameEn,
                    description = @description,
                    is_active = @isActive,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE cost_center_id = @id");

            Add(updateCmd, "@id", FormatGuid(costCenter.Id));
            Add(updateCmd, "@nameAr", costCenter.NameAr);
            Add(updateCmd, "@nameEn", costCenter.NameEn);
            Add(updateCmd, "@description", costCenter.Description);
            Add(updateCmd, "@isActive", costCenter.IsActive);
            Add(updateCmd, "@modifiedAt", costCenter.ModifiedAt);
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
                UPDATE cost_centers SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy
                WHERE cost_center_id = @id AND is_deleted = " +
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
                UPDATE cost_centers SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE cost_center_id = @id AND is_deleted = " +
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
            FROM cost_centers
            WHERE company_id = @company AND cost_center_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        if (excludeId != null)
            sql += " AND cost_center_id != @excludeId";

        command.CommandText = sql;
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        if (excludeId != null)
            Add(command, "@excludeId", FormatGuid(excludeId.Value));

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return count > 0;
    }

    public async Task<CostCenterQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var results = new List<CostCenterSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var whereClauses = new List<string>();
        if (!includeDeleted)
            whereClauses.Add("is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));
        if (code != null)
            whereClauses.Add("cost_center_code LIKE @code");
        if (name != null)
            whereClauses.Add("(name_ar LIKE @name OR name_en LIKE @name)");
        if (isActive != null)
            whereClauses.Add("is_active = @isActive");

        // Count query
        await using var countCmd = connection.CreateCommand();
        var countSql = @"SELECT COUNT(*) FROM cost_centers WHERE company_id = @company";
        if (whereClauses.Count > 0)
            countSql += " AND " + string.Join(" AND ", whereClauses);

        countCmd.CommandText = countSql;
        Add(countCmd, "@company", FormatGuid(companyId));
        if (code != null) Add(countCmd, "@code", $"%{code}%");
        if (name != null) Add(countCmd, "@name", $"%{name}%");
        if (isActive != null) Add(countCmd, "@isActive", isActive);

        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Data query
        await using var dataCmd = connection.CreateCommand();
        var dataSql = @"
            SELECT cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                   is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM cost_centers
            WHERE company_id = @company";

        if (whereClauses.Count > 0)
            dataSql += " AND " + string.Join(" AND ", whereClauses);

        dataSql += " ORDER BY name_ar";
        if (_provider == "PostgreSQL")
            dataSql += " OFFSET @offset LIMIT @limit";
        else
            dataSql += " LIMIT @limit OFFSET @offset";

        dataCmd.CommandText = dataSql;
        Add(dataCmd, "@company", FormatGuid(companyId));
        if (code != null) Add(dataCmd, "@code", $"%{code}%");
        if (name != null) Add(dataCmd, "@name", $"%{name}%");
        if (isActive != null) Add(dataCmd, "@isActive", isActive);
        Add(dataCmd, "@offset", (pageNumber - 1) * pageSize);
        Add(dataCmd, "@limit", pageSize);

        await using var reader = await dataCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCostCenterSnapshot(reader));

        return new CostCenterQueryResult(results.AsReadOnly(), totalCount);
    }

    public async Task<IReadOnlyList<CostCenterSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        var results = new List<CostCenterSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT cost_center_id, company_id, cost_center_code, name_ar, name_en, description,
                   is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM cost_centers
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            @" AND (cost_center_code LIKE @search OR name_ar LIKE @search OR name_en LIKE @search)
            ORDER BY name_ar
            LIMIT @limit";

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@search", $"%{searchTerm}%");
        Add(command, "@limit", maxResults);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCostCenterSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT COUNT(*)
            FROM cost_centers
            WHERE company_id = @company" +
            (includeDeleted ? "" : " AND is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));

        Add(command, "@company", FormatGuid(companyId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private CostCenterSnapshot MapCostCenterSnapshot(DbDataReader reader)
    {
        var costCenterId = Guid.Parse(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        var companyId = Guid.Parse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!);
        var code = reader.GetString(2);
        var nameAr = reader.GetString(3);
        var nameEn = reader.GetString(4);
        var description = reader.GetString(5);
        var isActive = reader.GetBoolean(6);
        var isDeleted = reader.GetBoolean(7);
        var createdAt = _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(8)
            : DateTime.Parse(Convert.ToString(reader.GetValue(8), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        var createdBy = reader.IsDBNull(9) ? (Guid?)null : Guid.Parse(Convert.ToString(reader.GetValue(9), CultureInfo.InvariantCulture)!);
        var modifiedAt = reader.IsDBNull(10) ? (DateTime?)null : _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(10)
            : DateTime.Parse(Convert.ToString(reader.GetValue(10), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        var modifiedBy = reader.IsDBNull(11) ? (Guid?)null : Guid.Parse(Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture)!);

        return new CostCenterSnapshot(
            costCenterId,
            companyId,
            code,
            nameAr,
            nameEn,
            description,
            isActive,
            isDeleted,
            createdAt,
            createdBy,
            modifiedAt,
            modifiedBy);
    }
}

/// <summary>
/// Factory for creating CostCenterRepository instances.
/// </summary>
public static class CostCenterRepositoryFactory
{
    public static CostCenterRepository Create(string provider, string connectionString) =>
        new CostCenterRepository(provider, connectionString);
}

/// <summary>
/// Extensions for CostCenterRepository.
/// </summary>
public static class CostCenterRepositoryExtensions
{
    public static async Task<CostCenter?> FindByIdOrNullAsync(
        this ICostCenterRepository repository,
        Guid id,
        CancellationToken ct = default)
    {
        var snapshot = await repository.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (snapshot == null)
            return null;

        var entity = new CostCenter(
            snapshot.CompanyId,
            snapshot.Code,
            snapshot.NameAr,
            snapshot.NameEn,
            snapshot.Description,
            snapshot.IsActive);
        entity.Rehydrate(snapshot.Id, snapshot.CreatedAt, snapshot.CreatedBy, snapshot.ModifiedAt, snapshot.ModifiedBy);
        return entity;
    }
}
