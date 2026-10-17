using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Npgsql;
using ShouTech.Domain.Entities;
using ShouTech.Domain.Interfaces;

namespace ShouTech.Infrastructure;

/// <summary>
/// Provider-neutral customer persistence for SQLite and PostgreSQL.
/// </summary>
public sealed class CustomerRepository : ICustomerRepository
{
    private readonly string _provider;
    private readonly string _connectionString;

    public CustomerRepository(string provider, string connectionString)
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

    public async Task<Guid> SaveAsync(Customer customer, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            // Check for duplicate code
            await using var checkCmd = CreateCommand(connection, transaction,
                "SELECT COUNT(*) FROM customers WHERE company_id=@company AND customer_code=@code AND is_deleted=" +
                (_provider == "PostgreSQL" ? "false" : "0"));
            Add(checkCmd, "@company", FormatGuid(customer.CompanyId));
            Add(checkCmd, "@code", customer.Code);

            if (Convert.ToInt64(await checkCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException($"Customer code '{customer.Code}' already exists for this company.");

            // Insert customer
            await using var insertCmd = CreateCommand(connection, transaction, @"
                INSERT INTO customers (
                    customer_id, company_id, customer_code, name_ar, name_en, type,
                    tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                    phone, mobile, email, address, is_active, is_deleted,
                    created_at, created_by, modified_at, modified_by
                ) VALUES (
                    @id, @company, @code, @nameAr, @nameEn, @type,
                    @taxNo, @crNo, @creditLimit, @creditDays, @priceTierId, @glAccountId,
                    @phone, @mobile, @email, @address, @isActive, @isDeleted,
                    @createdAt, @createdBy, @modifiedAt, @modifiedBy
                )");

            Add(insertCmd, "@id", FormatGuid(customer.Id));
            Add(insertCmd, "@company", FormatGuid(customer.CompanyId));
            Add(insertCmd, "@code", customer.Code);
            Add(insertCmd, "@nameAr", customer.NameAr);
            Add(insertCmd, "@nameEn", customer.NameEn);
            Add(insertCmd, "@type", (int)customer.Type);
            Add(insertCmd, "@taxNo", customer.TaxNo);
            Add(insertCmd, "@crNo", customer.CrNo);
            Add(insertCmd, "@creditLimit", customer.CreditLimit);
            Add(insertCmd, "@creditDays", customer.CreditDays);
            Add(insertCmd, "@priceTierId", customer.PriceTierId.HasValue ? FormatGuid(customer.PriceTierId.Value) : DBNull.Value);
            Add(insertCmd, "@glAccountId", customer.GlAccountId.HasValue ? FormatGuid(customer.GlAccountId.Value) : DBNull.Value);
            Add(insertCmd, "@phone", customer.Phone);
            Add(insertCmd, "@mobile", customer.Mobile);
            Add(insertCmd, "@email", customer.Email);
            Add(insertCmd, "@address", customer.Address);
            Add(insertCmd, "@isActive", customer.IsActive);
            Add(insertCmd, "@isDeleted", false);
            Add(insertCmd, "@createdAt", customer.CreatedAt);
            Add(insertCmd, "@createdBy", userId);
            Add(insertCmd, "@modifiedAt", customer.ModifiedAt);
            Add(insertCmd, "@modifiedBy", userId);

            await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return customer.Id;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<CustomerSnapshot?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT customer_id, company_id, customer_code, name_ar, name_en, type,
                   tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                   phone, mobile, email, address, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM customers
            WHERE customer_id = @id";

        Add(command, "@id", FormatGuid(id));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapCustomerSnapshot(reader);
    }

    public async Task<CustomerSnapshot?> FindByCodeAsync(Guid companyId, string code, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT customer_id, company_id, customer_code, name_ar, name_en, type,
                   tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                   phone, mobile, email, address, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM customers
            WHERE company_id = @company AND customer_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return null;

        return MapCustomerSnapshot(reader);
    }

    public async Task<IReadOnlyList<CustomerSnapshot>> GetAllAsync(Guid companyId, CancellationToken ct = default)
    {
        var results = new List<CustomerSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT customer_id, company_id, customer_code, name_ar, name_en, type,
                   tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                   phone, mobile, email, address, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM customers
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            " ORDER BY name_ar";

        Add(command, "@company", FormatGuid(companyId));
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCustomerSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<bool> UpdateAsync(Customer customer, string userId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var updateCmd = CreateCommand(connection, transaction, @"
                UPDATE customers SET
                    name_ar = @nameAr,
                    name_en = @nameEn,
                    type = @type,
                    tax_no = @taxNo,
                    cr_no = @crNo,
                    credit_limit = @creditLimit,
                    credit_days = @creditDays,
                    price_tier_id = @priceTierId,
                    gl_account_id = @glAccountId,
                    phone = @phone,
                    mobile = @mobile,
                    email = @email,
                    address = @address,
                    is_active = @isActive,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE customer_id = @id");

            Add(updateCmd, "@id", FormatGuid(customer.Id));
            Add(updateCmd, "@nameAr", customer.NameAr);
            Add(updateCmd, "@nameEn", customer.NameEn);
            Add(updateCmd, "@type", (int)customer.Type);
            Add(updateCmd, "@taxNo", customer.TaxNo);
            Add(updateCmd, "@crNo", customer.CrNo);
            Add(updateCmd, "@creditLimit", customer.CreditLimit);
            Add(updateCmd, "@creditDays", customer.CreditDays);
            Add(updateCmd, "@priceTierId", customer.PriceTierId.HasValue ? FormatGuid(customer.PriceTierId.Value) : DBNull.Value);
            Add(updateCmd, "@glAccountId", customer.GlAccountId.HasValue ? FormatGuid(customer.GlAccountId.Value) : DBNull.Value);
            Add(updateCmd, "@phone", customer.Phone);
            Add(updateCmd, "@mobile", customer.Mobile);
            Add(updateCmd, "@email", customer.Email);
            Add(updateCmd, "@address", customer.Address);
            Add(updateCmd, "@isActive", customer.IsActive);
            Add(updateCmd, "@modifiedAt", customer.ModifiedAt);
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
                UPDATE customers SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy
                WHERE customer_id = @id AND is_deleted = " +
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
                UPDATE customers SET
                    is_deleted = @isDeleted,
                    deleted_at = @deletedAt,
                    deleted_by = @deletedBy,
                    modified_at = @modifiedAt,
                    modified_by = @modifiedBy
                WHERE customer_id = @id AND is_deleted = " +
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
            FROM customers
            WHERE company_id = @company AND customer_code = @code AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");

        if (excludeId != null)
            sql += " AND customer_id != @excludeId";

        command.CommandText = sql;
        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@code", code);
        if (excludeId != null)
            Add(command, "@excludeId", FormatGuid(excludeId.Value));

        var count = Convert.ToInt64(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        return count > 0;
    }

    public async Task<CustomerQueryResult> GetByFilterAsync(
        Guid companyId,
        string? code = null,
        string? name = null,
        CustomerType? type = null,
        bool? isActive = null,
        bool includeDeleted = false,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var results = new List<CustomerSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var whereClauses = new List<string>();
        if (!includeDeleted)
            whereClauses.Add("is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));
        if (code != null)
            whereClauses.Add("customer_code LIKE @code");
        if (name != null)
            whereClauses.Add("(name_ar LIKE @name OR name_en LIKE @name)");
        if (type != null)
            whereClauses.Add("type = @type");
        if (isActive != null)
            whereClauses.Add("is_active = @isActive");

        // Count query
        await using var countCmd = connection.CreateCommand();
        var countSql = @"SELECT COUNT(*) FROM customers WHERE company_id = @company";
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
            SELECT customer_id, company_id, customer_code, name_ar, name_en, type,
                   tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                   phone, mobile, email, address, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM customers
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
        if (type != null) Add(dataCmd, "@type", (int)type);
        if (isActive != null) Add(dataCmd, "@isActive", isActive);
        Add(dataCmd, "@offset", (pageNumber - 1) * pageSize);
        Add(dataCmd, "@limit", pageSize);

        await using var reader = await dataCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCustomerSnapshot(reader));

        return new CustomerQueryResult(results.AsReadOnly(), totalCount);
    }

    public async Task<IReadOnlyList<CustomerSnapshot>> SearchAsync(
        Guid companyId,
        string searchTerm,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        var results = new List<CustomerSnapshot>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT customer_id, company_id, customer_code, name_ar, name_en, type,
                   tax_no, cr_no, credit_limit, credit_days, price_tier_id, gl_account_id,
                   phone, mobile, email, address, is_active, is_deleted,
                   created_at, created_by, modified_at, modified_by
            FROM customers
            WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0") +
            @" AND (customer_code LIKE @search OR name_ar LIKE @search OR name_en LIKE @search OR phone LIKE @search OR mobile LIKE @search)
            ORDER BY name_ar
            LIMIT @limit";

        Add(command, "@company", FormatGuid(companyId));
        Add(command, "@search", $"%{searchTerm}%");
        Add(command, "@limit", maxResults);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(MapCustomerSnapshot(reader));

        return results.AsReadOnly();
    }

    public async Task<int> CountAsync(Guid companyId, bool includeDeleted = false, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = @"
            SELECT COUNT(*)
            FROM customers
            WHERE company_id = @company" +
            (includeDeleted ? "" : " AND is_deleted = " + (_provider == "PostgreSQL" ? "false" : "0"));

        Add(command, "@company", FormatGuid(companyId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    public async Task<CustomerSummary> GetSummaryAsync(Guid companyId, CancellationToken ct = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // Total customers
        await using var totalCmd = connection.CreateCommand();
        totalCmd.CommandText = @"SELECT COUNT(*) FROM customers WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(totalCmd, "@company", FormatGuid(companyId));
        var totalCustomers = Convert.ToInt32(await totalCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Active customers
        await using var activeCmd = connection.CreateCommand();
        activeCmd.CommandText = @"SELECT COUNT(*) FROM customers WHERE company_id = @company AND is_active = " +
            (_provider == "PostgreSQL" ? "true" : "1") + " AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(activeCmd, "@company", FormatGuid(companyId));
        var activeCustomers = Convert.ToInt32(await activeCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Suppliers
        await using var supplierCmd = connection.CreateCommand();
        supplierCmd.CommandText = @"SELECT COUNT(*) FROM customers WHERE company_id = @company AND type = 2 AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(supplierCmd, "@company", FormatGuid(companyId));
        var suppliers = Convert.ToInt32(await supplierCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Both (Customer and Supplier)
        await using var bothCmd = connection.CreateCommand();
        bothCmd.CommandText = @"SELECT COUNT(*) FROM customers WHERE company_id = @company AND type = 3 AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(bothCmd, "@company", FormatGuid(companyId));
        var both = Convert.ToInt32(await bothCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        // Total credit limit
        await using var creditCmd = connection.CreateCommand();
        creditCmd.CommandText = @"SELECT COALESCE(SUM(credit_limit), 0) FROM customers WHERE company_id = @company AND is_deleted = " +
            (_provider == "PostgreSQL" ? "false" : "0");
        Add(creditCmd, "@company", FormatGuid(companyId));
        var totalCreditLimit = Convert.ToDecimal(await creditCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture);

        return new CustomerSummary(totalCustomers, activeCustomers, suppliers, both, totalCreditLimit);
    }

    private CustomerSnapshot MapCustomerSnapshot(DbDataReader reader)
    {
        var customerId = Guid.Parse(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        var companyId = Guid.Parse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!);
        var code = reader.GetString(2);
        var nameAr = reader.GetString(3);
        var nameEn = reader.GetString(4);
        var type = (CustomerType)reader.GetInt32(5);
        var taxNo = reader.GetString(6);
        var crNo = reader.GetString(7);
        var creditLimit = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture);
        var creditDays = reader.GetInt32(9);
        Guid? priceTierId = reader.IsDBNull(10) ? null : Guid.Parse(Convert.ToString(reader.GetValue(10), CultureInfo.InvariantCulture)!);
        Guid? glAccountId = reader.IsDBNull(11) ? null : Guid.Parse(Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture)!);
        var phone = reader.GetString(12);
        var mobile = reader.GetString(13);
        var email = reader.GetString(14);
        var address = reader.GetString(15);
        var isActive = reader.GetBoolean(16);
        var isDeleted = reader.GetBoolean(17);
        var createdAt = _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(18)
            : DateTime.Parse(Convert.ToString(reader.GetValue(18), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        Guid? createdBy = reader.IsDBNull(19) ? null : Guid.Parse(Convert.ToString(reader.GetValue(19), CultureInfo.InvariantCulture)!);
        DateTime? modifiedAt = reader.IsDBNull(20) ? null : _provider == "PostgreSQL"
            ? reader.GetFieldValue<DateTime>(20)
            : DateTime.Parse(Convert.ToString(reader.GetValue(20), CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        Guid? modifiedBy = reader.IsDBNull(21) ? null : Guid.Parse(Convert.ToString(reader.GetValue(21), CultureInfo.InvariantCulture)!);

        return new CustomerSnapshot(
            customerId,
            companyId,
            code,
            nameAr,
            nameEn,
            type,
            taxNo,
            crNo,
            creditLimit,
            creditDays,
            priceTierId,
            glAccountId,
            phone,
            mobile,
            email,
            address,
            isActive,
            isDeleted,
            createdAt,
            createdBy,
            modifiedAt,
            modifiedBy);
    }
}

/// <summary>
/// Factory for creating CustomerRepository instances.
/// </summary>
public static class CustomerRepositoryFactory
{
    public static CustomerRepository Create(string provider, string connectionString) =>
        new CustomerRepository(provider, connectionString);
}

/// <summary>
/// Extensions for CustomerRepository.
/// </summary>
public static class CustomerRepositoryExtensions
{
    public static async Task<Customer?> FindByIdOrNullAsync(
        this ICustomerRepository repository,
        Guid id,
        CancellationToken ct = default)
    {
        var snapshot = await repository.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (snapshot == null)
            return null;

        return Customer.Rehydrate(snapshot);
    }
}
