#nullable enable

using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.App;
using ShouTech.App.Localization;

namespace ShouTech.App.Services;

public sealed record CostCenterCardRecord
{
    public string Code { get; init; } = string.Empty;
    public string ParentCode { get; init; } = string.Empty;
    public int Level { get; init; } = 1;
    public string NameAr { get; init; } = string.Empty;
    public string NameEn { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
}

public sealed class CostCenterCardException : InvalidOperationException
{
    public string ErrorCode { get; }

    public CostCenterCardException(string errorCode, string message) : base(message)
        => ErrorCode = errorCode;
}

/// <summary>
/// Provider-neutral, company-scoped cost-center card persistence. Hierarchy changes are validated
/// transactionally, and removal is logical so previously posted accounting references remain intact.
/// </summary>
public static class CostCenterCardSvc
{
    private static bool IsPostgreSql => DatabaseRuntime.IsPostgreSql();
    private static string ActiveCompanyCode => AppState.CurrentCompanyCode?.Trim() ?? string.Empty;
    private static string FalseLiteral => IsPostgreSql ? "FALSE" : "0";
    private static string TrueLiteral => IsPostgreSql ? "TRUE" : "1";

    public static async Task<CostCenterCardRecord?> LoadByCodeAsync(
        string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var companyId = await ResolveCompanyIdAsync(connection, null, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT cost_center_code, parent_code, level_no, name_ar, name_en, description
            FROM cost_centers
            WHERE company_id=@company AND cost_center_code=@code AND is_deleted={FalseLiteral};
            """;
        Add(command, "@company", companyId);
        Add(command, "@code", code.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new CostCenterCardRecord
        {
            Code = ReadString(reader, 0),
            ParentCode = ReadString(reader, 1),
            Level = ReadInt32(reader, 2, 1),
            NameAr = ReadString(reader, 3),
            NameEn = ReadString(reader, 4),
            Notes = ReadString(reader, 5)
        };
    }

    public static async Task SaveAsync(
        CostCenterCardRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            object? existingId = null;
            bool isDeleted = false;

            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT cost_center_id,is_deleted FROM cost_centers WHERE company_id=@company AND cost_center_code=@code;";
                Add(find, "@company", companyId);
                Add(find, "@code", record.Code.Trim());
                await using var reader = await find.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existingId = reader.GetValue(0);
                    isDeleted = Convert.ToBoolean(reader.GetValue(1), CultureInfo.InvariantCulture);
                }
            }

            if (isDeleted)
                throw new CostCenterCardException(ErrorCodes.CostCenterCodeRequiresRestore,
                    "This cost-center code belongs to a deleted record. Restore it from the Recycle Bin before reusing the code.");

            await ValidateHierarchyAsync(connection, transaction, companyId, record, cancellationToken).ConfigureAwait(false);

            var now = TimestampValue();
            if (existingId is null)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cost_centers
                    (cost_center_id, company_id, cost_center_code, parent_code, level_no,
                     name_ar, name_en, description, is_active, is_deleted,
                     created_at, created_by, modified_at, modified_by)
                    VALUES
                    (@id, @company, @code, @parentCode, @level,
                     @nameAr, @nameEn, @description, @isActive, @isDeleted,
                     @createdAt, @createdBy, NULL, NULL);
                    """;
                Add(insert, "@id", FormatId(Guid.NewGuid()));
                Add(insert, "@company", companyId);
                BindRecord(insert, record);
                Add(insert, "@isActive", true);
                Add(insert, "@isDeleted", false);
                Add(insert, "@createdAt", now);
                Add(insert, "@createdBy", ActorValue());
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $"""
                    UPDATE cost_centers SET
                        parent_code=@parentCode, level_no=@level,
                        name_ar=@nameAr, name_en=@nameEn, description=@description,
                        modified_at=@modifiedAt, modified_by=@modifiedBy
                    WHERE company_id=@company AND cost_center_id=@id AND is_deleted={FalseLiteral};
                    """;
                Add(update, "@company", companyId);
                Add(update, "@id", existingId);
                BindRecord(update, record, includeCode: false);
                Add(update, "@modifiedAt", now);
                Add(update, "@modifiedBy", ActorValue());
                if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The cost-center record changed during the save operation. Reload it and try again.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("CostCenterCard", "Save",
                $"Company={ActiveCompanyCode}; Code={record.Code.Trim()}; Outcome=Committed");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<bool> DeleteAsync(string code, CancellationToken cancellationToken = default)
    {
        ValidateCode(code);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var companyId = await ResolveCompanyIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE cost_centers SET is_deleted={TrueLiteral}, deleted_at=@deletedAt, deleted_by=@deletedBy,
                    modified_at=@modifiedAt, modified_by=@modifiedBy
                WHERE company_id=@company AND cost_center_code=@code AND is_deleted={FalseLiteral}
                  AND NOT EXISTS (
                    SELECT 1 FROM cost_centers child
                    WHERE child.company_id=@company AND child.parent_code=@code AND child.is_deleted={FalseLiteral});
                """;
            Add(command, "@company", companyId);
            Add(command, "@code", code.Trim());
            Add(command, "@deletedAt", TimestampValue());
            Add(command, "@deletedBy", ActorValue());
            Add(command, "@modifiedAt", TimestampValue());
            Add(command, "@modifiedBy", ActorValue());
            var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            AppHost.Logging?.LogOperation("CostCenterCard", "SoftDelete",
                $"Company={ActiveCompanyCode}; Code={code.Trim()}; Outcome=Committed");
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ValidateHierarchyAsync(
        DbConnection connection, DbTransaction transaction, object companyId,
        CostCenterCardRecord record, CancellationToken cancellationToken)
    {
        var parentCode = record.ParentCode.Trim();
        if (parentCode.Length == 0)
        {
            if (record.Level != 1)
                throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                    "A top-level cost center must have level 1.");
            return;
        }

        if (string.Equals(parentCode, record.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "A cost center cannot be its own parent.");

        int? parentLevel;
        await using (var parent = connection.CreateCommand())
        {
            parent.Transaction = transaction;
            parent.CommandText = $"""
                SELECT level_no FROM cost_centers
                WHERE company_id=@company AND cost_center_code=@parentCode AND is_deleted={FalseLiteral}
                LIMIT 1;
                """;
            Add(parent, "@company", companyId);
            Add(parent, "@parentCode", parentCode);
            var value = await parent.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            parentLevel = value is null or DBNull
                ? null
                : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (parentLevel is null)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "The selected parent cost center was not found in the active company.");
        if (record.Level != parentLevel.Value + 1)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                $"The cost-center level must be {parentLevel.Value + 1} for the selected parent.");

        await using var cycle = connection.CreateCommand();
        cycle.Transaction = transaction;
        cycle.CommandText = $"""
            WITH RECURSIVE ancestors(cost_center_code, parent_code) AS (
                SELECT cost_center_code, parent_code
                FROM cost_centers
                WHERE company_id=@company AND cost_center_code=@parentCode AND is_deleted={FalseLiteral}
                UNION
                SELECT parent.cost_center_code, parent.parent_code
                FROM cost_centers parent
                INNER JOIN ancestors child ON parent.cost_center_code=child.parent_code
                WHERE parent.company_id=@company AND parent.is_deleted={FalseLiteral}
            )
            SELECT COUNT(*) FROM ancestors WHERE cost_center_code=@code;
            """;
        Add(cycle, "@company", companyId);
        Add(cycle, "@parentCode", parentCode);
        Add(cycle, "@code", record.Code.Trim());
        var cycleCount = Convert.ToInt32(await cycle.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
        if (cycleCount > 0)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "The parent change would create a cost-center hierarchy cycle.");
    }

    private static void Validate(CostCenterCardRecord record)
    {
        ValidateCode(record.Code);
        if (string.IsNullOrWhiteSpace(record.NameAr) || record.NameAr.Trim().Length > 200)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "Enter an Arabic cost-center name with no more than 200 characters.");
        if (record.NameEn.Trim().Length > 200)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "The English cost-center name cannot exceed 200 characters.");
        if (record.ParentCode.Trim().Length > 50 || record.Level is < 1 or > 20)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "The parent code or hierarchy level is invalid.");
        if (record.Notes.Trim().Length > 500)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "Cost-center notes cannot exceed 500 characters.");
    }

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50)
            throw new CostCenterCardException(ErrorCodes.CostCenterValidationFailed,
                "Enter a cost-center code with no more than 50 characters.");
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new CostCenterCardException(ErrorCodes.CostCenterContextFailed,
                "Select an active company before using the cost-center card.");
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

    private static async Task<object> ResolveCompanyIdAsync(
        DbConnection connection, DbTransaction? transaction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActiveCompanyCode))
            throw new CostCenterCardException(ErrorCodes.CostCenterContextFailed,
                "Select an active company before using the cost-center card.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT company_id FROM companies
            WHERE company_code=@code AND is_active={TrueLiteral} AND is_deleted={FalseLiteral}
            LIMIT 1;
            """;
        Add(command, "@code", ActiveCompanyCode);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull)
            throw new CostCenterCardException(ErrorCodes.CostCenterContextFailed,
                "The active company is not registered in the connected database.");

        if (IsPostgreSql)
        {
            if (value is Guid guid) return guid;
            if (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)) return parsed;
            throw new CostCenterCardException(ErrorCodes.CostCenterContextFailed, "The active company identifier is invalid.");
        }

        var sqliteCompanyId = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(sqliteCompanyId)) return sqliteCompanyId;
        throw new CostCenterCardException(ErrorCodes.CostCenterContextFailed, "The active company identifier is invalid.");
    }

    private static void BindRecord(DbCommand command, CostCenterCardRecord record, bool includeCode = true)
    {
        if (includeCode) Add(command, "@code", record.Code.Trim());
        Add(command, "@parentCode", record.ParentCode.Trim());
        Add(command, "@level", record.Level);
        Add(command, "@nameAr", record.NameAr.Trim());
        Add(command, "@nameEn", record.NameEn.Trim());
        Add(command, "@description", record.Notes.Trim());
    }

    private static object FormatId(Guid value) =>
        IsPostgreSql ? value : value.ToString("D", CultureInfo.InvariantCulture);

    private static object TimestampValue() =>
        IsPostgreSql ? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

    private static object ActorValue()
    {
        if (!IsPostgreSql)
            return (object?)(AppState.CurrentUser?.Username) ?? DBNull.Value;
        return Guid.TryParse(AppState.CurrentUser?.Id, out var userId) && userId != Guid.Empty
            ? userId : DBNull.Value;
    }

    private static string ReadString(DbDataReader reader, int ordinal, string fallback = "") =>
        reader.IsDBNull(ordinal) ? fallback :
        Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? fallback;

    private static int ReadInt32(DbDataReader reader, int ordinal, int fallback) =>
        reader.IsDBNull(ordinal) ? fallback :
        Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
