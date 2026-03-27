using Microsoft.Data.SqlClient;
using Dapper;
using SiOffice.GoogleConnector.Reports.Models;

namespace SiOffice.GoogleConnector.Reports.Data;

/// <summary>
/// Repository for R01 report data from the MasterPlan database (monthly backup).
/// This is used as fallback when Replica coverage is insufficient.
/// IMPORTANT: MasterPlan's HoursReports table has [DateTime] (datetime), NOT ReportDate.
/// </summary>
public class MasterPlanR01Repository : IR01Repository
{
    private readonly string _connectionString;

    public string DataSourceName => "MasterPlan";

    public MasterPlanR01Repository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<List<R01DataRow>> GetHoursDataAsync(R01ReportRequest request, CancellationToken cancellationToken = default)
    {
        // MasterPlan HoursReports has [DateTime] (datetime), NOT ReportDate.
        // Build SQL dynamically to handle optional filters without STRING_SPLIT.
        var sql = @"
            SELECT 
                hr.ID AS ReportID,
                hr.ProjectID,
                ISNULL(p.ProjectNum, '') AS ProjectNum,
                ISNULL(p.Name, '') AS ProjectName,
                p.CustomerID,
                c.Name AS CustomerName,
                hr.EmployeeID,
                ISNULL(e.FirstName + ' ' + e.LastName, '') AS EmployeeName,
                CAST(hr.[DateTime] AS date) AS WorkDate,
                ISNULL(hr.TotalHours, 0) AS Hours,
                hr.StepName,
                hr.Description,
                'MasterPlan' AS DataSource
            FROM HoursReports hr
            LEFT JOIN Projects p ON hr.ProjectID = p.ID
            LEFT JOIN Companies c ON p.CustomerID = c.ID
            LEFT JOIN Employees e ON hr.EmployeeID = e.ID
            WHERE CAST(hr.[DateTime] AS date) >= @DateFrom 
              AND CAST(hr.[DateTime] AS date) <= @DateTo";

        // Add optional filters using Dapper's list expansion
        if (request.ProjectIds.Count > 0)
            sql += " AND hr.ProjectID IN @ProjectIds";
        
        if (request.EmployeeIds.Count > 0)
            sql += " AND hr.EmployeeID IN @EmployeeIds";
        
        if (request.CustomerId.HasValue)
            sql += " AND p.CustomerID = @CustomerId";

        sql += " ORDER BY CAST(hr.[DateTime] AS date), hr.ProjectID, hr.EmployeeID";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<R01DataRow>(
            new CommandDefinition(sql, new
            {
                request.DateFrom,
                request.DateTo,
                ProjectIds = request.ProjectIds,
                EmployeeIds = request.EmployeeIds,
                request.CustomerId
            }, cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<int> GetDistinctDatesCountAsync(DateTime dateFrom, DateTime dateTo, List<int>? projectIds, CancellationToken cancellationToken = default)
    {
        // Coverage calculation using dynamic SQL for optional project filter
        var sql = @"
            SELECT COUNT(DISTINCT CAST([DateTime] AS date))
            FROM HoursReports
            WHERE CAST([DateTime] AS date) >= @DateFrom 
              AND CAST([DateTime] AS date) <= @DateTo";

        if (projectIds?.Count > 0)
            sql += " AND ProjectID IN @ProjectIds";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                ProjectIds = projectIds
            }, cancellationToken: cancellationToken));
    }

    public async Task<DateTime?> GetLastSyncDateAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT MAX(ModifiedDate) 
            FROM (
                SELECT MAX(LastUpdated) AS ModifiedDate FROM Projects
                UNION ALL
                SELECT MAX(CAST([DateTime] AS date)) FROM HoursReports
            ) AS Dates";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<DateTime?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    public async Task<bool> HasSubContractDataAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SubContracts')
                SELECT CASE WHEN EXISTS (SELECT 1 FROM SubContracts) THEN 1 ELSE 0 END
            ELSE
                SELECT 0";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return result == 1;
    }

    public async Task<List<ProjectInfo>> GetProjectsAsync(int? customerId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT 
                p.ID AS Id,
                ISNULL(p.ProjectNum, '') AS ProjectNum,
                ISNULL(p.Name, '') AS Name,
                c.Name AS CustomerName,
                p.CustomerID AS CustomerId
            FROM Projects p
            LEFT JOIN Companies c ON p.CustomerID = c.ID
            WHERE (@CustomerId IS NULL OR p.CustomerID = @CustomerId)
              AND (@ActiveOnly = 0 OR p.IsActive = 1)
            ORDER BY p.ProjectNum";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<ProjectInfo>(
            new CommandDefinition(sql, new
            {
                CustomerId = customerId,
                ActiveOnly = activeOnly ? 1 : 0
            }, cancellationToken: cancellationToken));

        return results.ToList();
    }

    public async Task<List<CustomerInfo>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                ID AS Id,
                Name
            FROM Companies
            WHERE Name IS NOT NULL
            ORDER BY Name";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var results = await connection.QueryAsync<CustomerInfo>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        return results.ToList();
    }
}