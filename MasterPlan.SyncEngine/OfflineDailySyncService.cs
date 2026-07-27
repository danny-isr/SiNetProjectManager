using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Dapper;
using MasterPlan.SyncEngine.Models;

namespace MasterPlan.SyncEngine;

/// <summary>
/// Offline Daily Sync Service - Uses dump files instead of live API.
/// Runs the full sync pipeline against local NDJSON files for testing.
/// 
/// This validates:
/// - JSON parsing
/// - Mapping to replica schema
/// - Insert/update logic
/// - Duplicate prevention
/// - Watermark handling
/// </summary>
public class OfflineDailySyncService
{
    private readonly OfflineApiSimulator _simulator;
    private readonly string _replicaConnectionString;
    private readonly ILogger<OfflineDailySyncService> _logger;
    private SqlConnection? _lockConnection;

    public OfflineDailySyncService(
        OfflineApiSimulator simulator,
        string replicaConnectionString,
        ILogger<OfflineDailySyncService> logger)
    {
        _simulator = simulator;
        _replicaConnectionString = replicaConnectionString;
        _logger = logger;
    }

    /// <summary>
    /// Run the offline sync pipeline using dump files.
    /// </summary>
    public async Task<DailySyncResult> RunOfflineSyncAsync(bool resetWatermarks = false, CancellationToken cancellationToken = default)
    {
        var result = new DailySyncResult
        {
            StartTime = DateTime.UtcNow
        };

        _logger.LogInformation("[OFFLINE] Starting Offline Sync at {StartTime}", result.StartTime);

        try
        {
            // Ensure sync state table exists
            await EnsureSyncStateTableAsync();

            // Check if we should reset watermarks for full reload
            if (resetWatermarks)
            {
                _logger.LogInformation("[OFFLINE] Resetting all watermarks for full reload");
                await ResetWatermarksAsync();
            }

            // Check if this is initial load (no watermarks)
            var isInitialLoad = await IsInitialLoadAsync();
            if (isInitialLoad)
            {
                Console.WriteLine();
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  📁 OFFLINE MODE - INITIAL LOAD (Full Dataset)                   ║");
                Console.WriteLine("║  No watermarks found - loading ALL records from dump files        ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
            }

            // Acquire lock (advisory - for consistency with live mode)
            if (!await TryAcquireLockAsync())
            {
                result.ErrorMessage = "Could not acquire sync lock - another sync may be in progress. Use --clear-lock to force clear.";
                result.EndTime = DateTime.UtcNow;
                return result;
            }

            try
            {
                // Sync all entities from dump files
                result.EntityResults["Projects"] = await SyncProjectsAsync(cancellationToken);
                result.EntityResults["Companies"] = await SyncCompaniesAsync(cancellationToken);
                result.EntityResults["Contacts"] = await SyncContactsAsync(cancellationToken);
                result.EntityResults["Employees"] = await SyncEmployeesAsync(cancellationToken);
                result.EntityResults["Bids"] = await SyncBidsAsync(cancellationToken);
                result.EntityResults["Bills"] = await SyncBillsAsync(cancellationToken);
                result.EntityResults["Intakes"] = await SyncIntakesAsync(cancellationToken);
                result.EntityResults["Tasks"] = await SyncTasksAsync(cancellationToken);
                result.EntityResults["Conversations"] = await SyncConversationsAsync(cancellationToken);
                result.EntityResults["ProjectHours"] = await SyncProjectHoursAsync(cancellationToken);

                result.Success = result.EntityResults.Values.All(r => r.ErrorMessage == null);

                // Record run history - set EndTime BEFORE recording
                result.EndTime = DateTime.UtcNow;
                await RecordRunHistoryAsync(result);
            }
            finally
            {
                await ReleaseLockAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OFFLINE] Sync failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        result.EndTime = DateTime.UtcNow;
        _logger.LogInformation("[OFFLINE] Sync completed. Success: {Success}, Duration: {Duration}s",
            result.Success, (result.EndTime - result.StartTime).TotalSeconds);

        return result;
    }

    #region Entity Sync Methods

    private async Task<EntitySyncResult> SyncProjectsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Projects" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Projects");
            var projects = await _simulator.GetProjectsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = projects.Count;

            if (projects.Count > 0)
            {
                var (inserted, updated) = await UpsertProjectsAsync(projects);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = projects.Max(p => p.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Projects", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Projects");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncCompaniesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Companies" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Companies");
            var companies = await _simulator.GetCompaniesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = companies.Count;

            if (companies.Count > 0)
            {
                var (inserted, updated) = await UpsertCompaniesAsync(companies);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = companies.Max(c => c.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Companies", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Companies");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncContactsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Contacts" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Contacts");
            var contacts = await _simulator.GetContactsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = contacts.Count;

            if (contacts.Count > 0)
            {
                var (inserted, updated) = await UpsertContactsAsync(contacts);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = contacts.Max(c => c.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Contacts", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Contacts");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncEmployeesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Employees" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Employees");
            var employees = await _simulator.GetEmployeesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = employees.Count;

            if (employees.Count > 0)
            {
                var (inserted, updated) = await UpsertEmployeesAsync(employees);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = employees.Max(e => e.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Employees", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Employees");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncBidsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Bids" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Bids");
            var bids = await _simulator.GetBidsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = bids.Count;

            if (bids.Count > 0)
            {
                var (inserted, updated) = await UpsertBidsAsync(bids);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = bids.Max(b => b.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Bids", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Bids");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncBillsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Bills" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Bills");
            var bills = await _simulator.GetBillsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = bills.Count;

            if (bills.Count > 0)
            {
                var (inserted, updated) = await UpsertBillsAsync(bills);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = bills.Max(b => b.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Bills", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Bills");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncIntakesAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Intakes" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Intakes");
            var intakes = await _simulator.GetIntakesAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = intakes.Count;

            if (intakes.Count > 0)
            {
                var (inserted, updated) = await UpsertIntakesAsync(intakes);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = intakes.Max(i => i.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Intakes", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Intakes");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncTasksAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Tasks" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Tasks");
            var tasks = await _simulator.GetTasksAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = tasks.Count;

            if (tasks.Count > 0)
            {
                var (inserted, updated) = await UpsertTasksAsync(tasks);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = tasks.Max(t => t.LastUpdated) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Tasks", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Tasks");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncConversationsAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "Conversations" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("Conversations");
            var conversations = await _simulator.GetConversationsAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = conversations.Count;

            if (conversations.Count > 0)
            {
                var (inserted, updated) = await UpsertConversationsAsync(conversations);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = conversations.Max(c => c.CreatedDate) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("Conversations", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync Conversations");
        }
        return result;
    }

    private async Task<EntitySyncResult> SyncProjectHoursAsync(CancellationToken ct)
    {
        var result = new EntitySyncResult { EntityName = "ProjectHours" };
        try
        {
            result.PreviousWatermark = await GetWatermarkAsync("ProjectHours");
            var hours = await _simulator.GetProjectHoursAsync(result.PreviousWatermark, ct);
            result.RecordsFetched = hours.Count;

            if (hours.Count > 0)
            {
                var (inserted, updated) = await UpsertProjectHoursAsync(hours);
                result.RecordsInserted = inserted;
                result.RecordsUpdated = updated;
                result.NewWatermark = hours.Max(h => h.ReportDate) ?? result.PreviousWatermark;
                await UpdateWatermarkAsync("ProjectHours", result.NewWatermark);
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[OFFLINE] Failed to sync ProjectHours");
        }
        return result;
    }

    #endregion

    #region Upsert Methods

    private async Task<(int Inserted, int Updated)> UpsertProjectsAsync(List<ProjectEntity> projects)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var project in projects)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Projects WHERE ID = @ID", new { project.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Projects SET 
                        Name = @Name, ProjectNum = @ProjectNum, StartDate = @StartDate, EndDate = @EndDate,
                        Description = @Description, CustomerName = @CustomerName, CustomerID = @CustomerID,
                        EmployeeID = @EmployeeID, EmployeeName = @EmployeeName, StatusID = @StatusID,
                        StatusName = @StatusName, ProjectTypeID = @ProjectTypeID, ProjectType = @ProjectType,
                        StudioDepartmentTypeID = @StudioDepartmentTypeID, StudioDepartmentType = @StudioDepartmentType,
                        IsActive = @IsActive, FeeSum = @FeeSum, LastUpdated = @LastUpdated
                    WHERE ID = @ID", project);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Projects (ID, Name, ProjectNum, StartDate, EndDate, Description,
                        CustomerName, CustomerID, EmployeeID, EmployeeName, StatusID, StatusName,
                        ProjectTypeID, ProjectType, StudioDepartmentTypeID, StudioDepartmentType,
                        IsActive, FeeSum, LastUpdated)
                    VALUES (@ID, @Name, @ProjectNum, @StartDate, @EndDate, @Description,
                        @CustomerName, @CustomerID, @EmployeeID, @EmployeeName, @StatusID, @StatusName,
                        @ProjectTypeID, @ProjectType, @StudioDepartmentTypeID, @StudioDepartmentType,
                        @IsActive, @FeeSum, @LastUpdated)", project);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Projects: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertCompaniesAsync(List<CompanyEntity> companies)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var company in companies)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Companies WHERE ID = @ID", new { company.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Companies SET 
                        Name = @Name, Address = @Address, City = @city, Email = @Email,
                        RegistrationNumber = @RegistrationNumber, PhoneNum = @PhoneNum, LastUpdated = @LastUpdated
                    WHERE ID = @ID", company);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Companies (ID, Name, Address, City, Email, RegistrationNumber, PhoneNum, LastUpdated)
                    VALUES (@ID, @Name, @Address, @city, @Email, @RegistrationNumber, @PhoneNum, @LastUpdated)", company);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Companies: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertContactsAsync(List<ContactEntity> contacts)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var contact in contacts)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Contacts WHERE ID = @ID", new { contact.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Contacts SET 
                        FirstName = @FirstName, LastName = @LastName, CompanyName = @CompanyName,
                        CompanyID = @CompanyID, Address = @Address, Email = @Email,
                        Phone = @Phone, Mobile = @Mobile, LastUpdated = @LastUpdated
                    WHERE ID = @ID", contact);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Contacts (ID, FirstName, LastName, CompanyName, CompanyID, Address,
                        Email, Phone, Mobile, LastUpdated)
                    VALUES (@ID, @FirstName, @LastName, @CompanyName, @CompanyID, @Address,
                        @Email, @Phone, @Mobile, @LastUpdated)", contact);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Contacts: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertEmployeesAsync(List<EmployeeEntity> employees)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var employee in employees)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Employees WHERE ID = @ID", new { employee.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Employees SET 
                        FirstName = @FirstName, LastName = @LastName, LastUpdated = @LastUpdated
                    WHERE ID = @ID", employee);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Employees (ID, FirstName, LastName, LastUpdated)
                    VALUES (@ID, @FirstName, @LastName, @LastUpdated)", employee);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Employees: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertBidsAsync(List<BidEntity> bids)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var bid in bids)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Bids WHERE ID = @ID", new { bid.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Bids SET 
                        ProposalNum = @ProposalNum, Name = @Name, ActiveProposal = @ActiveProposal,
                        [DateTime] = @DateTime, EstimatedSum = @EstimatedSum, ProbabilityID = @ProbabilityID,
                        ProbabilityName = @ProbabilityName, StatusID = @StatusID, ProposalStatus = @ProposalStatus,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", bid);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Bids (ID, ProposalNum, Name, ActiveProposal, [DateTime], EstimatedSum,
                        ProbabilityID, ProbabilityName, StatusID, ProposalStatus, LastUpdated)
                    VALUES (@ID, @ProposalNum, @Name, @ActiveProposal, @DateTime, @EstimatedSum,
                        @ProbabilityID, @ProbabilityName, @StatusID, @ProposalStatus, @LastUpdated)", bid);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Bids: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertBillsAsync(List<BillEntity> bills)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var bill in bills)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Bills WHERE ID = @ID", new { bill.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Bills SET 
                        BillNum = @BillNum, ProjectName = @ProjectName, ProjectID = @ProjectID,
                        BillInternalNum = @BillInternalNum, [Sum] = @Sum, SubmitDate = @SubmitDate,
                        CollectionDate = @CollectionDate, Status = @Status, StatusID = @StatusID,
                        ResponsibleEmployee = @ResponsibleEmployee, ResponsibleEmployeeID = @ResponsibleEmployeeID,
                        StudioDepartment = @StudioDepartment, StudioDepartmentTypeID = @StudioDepartmentTypeID,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", bill);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Bills (ID, BillNum, ProjectName, ProjectID, BillInternalNum, [Sum],
                        SubmitDate, CollectionDate, Status, StatusID, ResponsibleEmployee,
                        ResponsibleEmployeeID, StudioDepartment, StudioDepartmentTypeID, LastUpdated)
                    VALUES (@ID, @BillNum, @ProjectName, @ProjectID, @BillInternalNum, @Sum,
                        @SubmitDate, @CollectionDate, @Status, @StatusID, @ResponsibleEmployee,
                        @ResponsibleEmployeeID, @StudioDepartment, @StudioDepartmentTypeID, @LastUpdated)", bill);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Bills: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertIntakesAsync(List<IntakeEntity> intakes)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var intake in intakes)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Intakes WHERE ID = @ID", new { intake.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Intakes SET 
                        OpenDate = @OpenDate, [Sum] = @Sum, CustomerID = @CustomerID,
                        CustomerName = @CustomerName, PaymentType = @PaymentType, PayTypeID = @PayTypeID,
                        Description = @Description, LastUpdated = @LastUpdated
                    WHERE ID = @ID", intake);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Intakes (ID, OpenDate, [Sum], CustomerID, CustomerName, PaymentType,
                        PayTypeID, Description, LastUpdated)
                    VALUES (@ID, @OpenDate, @Sum, @CustomerID, @CustomerName, @PaymentType,
                        @PayTypeID, @Description, @LastUpdated)", intake);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Intakes: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertTasksAsync(List<TaskEntity> tasks)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var task in tasks)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Tasks WHERE ID = @ID", new { task.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Tasks SET 
                        TaskDescription = @TaskDescription, IsHandled = @IsHandled, IsClosed = @IsClosed,
                        StartDate = @StartDate, DueDate = @DueDate, SenderName = @SenderName,
                        SenderID = @SenderID, ReceiverName = @ReceiverName, ReceiverID = @ReceiverID,
                        CompletionDate = @CompletionDate, Priority = @Priority, PriorityID = @PriorityID,
                        LastUpdated = @LastUpdated
                    WHERE ID = @ID", task);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Tasks (ID, TaskDescription, IsHandled, IsClosed, StartDate, DueDate,
                        SenderName, SenderID, ReceiverName, ReceiverID, CompletionDate, Priority,
                        PriorityID, LastUpdated)
                    VALUES (@ID, @TaskDescription, @IsHandled, @IsClosed, @StartDate, @DueDate,
                        @SenderName, @SenderID, @ReceiverName, @ReceiverID, @CompletionDate, @Priority,
                        @PriorityID, @LastUpdated)", task);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Tasks: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertConversationsAsync(List<ConversationEntity> conversations)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var conv in conversations)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_Conversations WHERE ID = @ID", new { conv.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_Conversations SET 
                        ProjectID = @ProjectID, ProjectName = @ProjectName, ContactID = @ContactID,
                        ContactName = @ContactName, EmployeeID = @EmployeeID, EmployeeName = @EmployeeName,
                        CreatedDate = @CreatedDate, DueDate = @DueDate, Subject = @Subject, Notes = @Notes
                    WHERE ID = @ID", conv);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_Conversations (ID, ProjectID, ProjectName, ContactID, ContactName,
                        EmployeeID, EmployeeName, CreatedDate, DueDate, Subject, Notes)
                    VALUES (@ID, @ProjectID, @ProjectName, @ContactID, @ContactName,
                        @EmployeeID, @EmployeeName, @CreatedDate, @DueDate, @Subject, @Notes)", conv);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] Conversations: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    private async Task<(int Inserted, int Updated)> UpsertProjectHoursAsync(List<ProjectHoursEntity> hours)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        int inserted = 0, updated = 0;

        foreach (var hour in hours)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM MP_ProjectHours WHERE ID = @ID", new { hour.ID });

            if (exists > 0)
            {
                await connection.ExecuteAsync(@"
                    UPDATE MP_ProjectHours SET 
                        ProjectID = @ProjectID, ProjectName = @ProjectName, ProjectNumber = @ProjectNumber,
                        EmployeeID = @EmployeeID, EmployeeName = @EmployeeName, ReportDate = @ReportDate,
                        StepName = @StepName, Description = @Description, StartTime = @StartTime,
                        EndTime = @EndTime, TotalHours = @TotalHours
                    WHERE ID = @ID", hour);
                updated++;
            }
            else
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO MP_ProjectHours (ID, ProjectID, ProjectName, ProjectNumber, EmployeeID,
                        EmployeeName, ReportDate, StepName, Description, StartTime, EndTime, TotalHours)
                    VALUES (@ID, @ProjectID, @ProjectName, @ProjectNumber, @EmployeeID,
                        @EmployeeName, @ReportDate, @StepName, @Description, @StartTime, @EndTime, @TotalHours)", hour);
                inserted++;
            }
        }

        _logger.LogInformation("[OFFLINE] ProjectHours: {Inserted} inserted, {Updated} updated", inserted, updated);
        return (inserted, updated);
    }

    #endregion

    #region Sync State Management

    private async Task EnsureSyncStateTableAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_State')
            BEGIN
                CREATE TABLE Sync_State (
                    EntityName NVARCHAR(100) PRIMARY KEY,
                    LastWatermark DATETIME2,
                    LastSyncTime DATETIME2,
                    UpdatedAt DATETIME2 DEFAULT GETUTCDATE()
                )
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_RunHistory')
            BEGIN
                CREATE TABLE Sync_RunHistory (
                    ID INT IDENTITY(1,1) PRIMARY KEY,
                    StartTime DATETIME2 NOT NULL,
                    EndTime DATETIME2 NOT NULL,
                    Success BIT NOT NULL,
                    ErrorMessage NVARCHAR(MAX),
                    RecordsSynced INT,
                    Details NVARCHAR(MAX)
                )
            END

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sync_Lock')
            BEGIN
                CREATE TABLE Sync_Lock (
                    LockName NVARCHAR(100) PRIMARY KEY,
                    AcquiredAt DATETIME2,
                    AcquiredBy NVARCHAR(200)
                )
            END

            -- Ensure the DailySync lock row exists (idempotent)
            IF NOT EXISTS (SELECT 1 FROM Sync_Lock WHERE LockName = 'DailySync')
            BEGIN
                INSERT INTO Sync_Lock (LockName) VALUES ('DailySync')
            END
        ");
    }

    private async Task<bool> IsInitialLoadAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Sync_State WHERE LastWatermark IS NOT NULL");
        return count == 0;
    }

    private async Task ResetWatermarksAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.ExecuteAsync("DELETE FROM Sync_State");
        _logger.LogInformation("[OFFLINE] All watermarks reset");
    }

    private async Task<DateTime?> GetWatermarkAsync(string entityName)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        return await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT LastWatermark FROM Sync_State WHERE EntityName = @EntityName",
            new { EntityName = entityName });
    }

    private async Task UpdateWatermarkAsync(string entityName, DateTime? watermark)
    {
        if (!watermark.HasValue) return;

        await using var connection = new SqlConnection(_replicaConnectionString);
        await connection.ExecuteAsync(@"
            MERGE INTO Sync_State AS target
            USING (SELECT @EntityName AS EntityName) AS source
            ON target.EntityName = source.EntityName
            WHEN MATCHED THEN
                UPDATE SET LastWatermark = @Watermark, LastSyncTime = GETUTCDATE(), UpdatedAt = GETUTCDATE()
            WHEN NOT MATCHED THEN
                INSERT (EntityName, LastWatermark, LastSyncTime, UpdatedAt)
                VALUES (@EntityName, @Watermark, GETUTCDATE(), GETUTCDATE());",
            new { EntityName = entityName, Watermark = watermark.Value });
    }

    private async Task<bool> TryAcquireLockAsync()
    {
        if (_lockConnection is not null)
            throw new InvalidOperationException("The daily-sync application lock is already held by this service instance.");

        var connection = new SqlConnection(_replicaConnectionString);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
            var result = await connection.ExecuteScalarAsync<int>(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = 'SiNetDailySync',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Session',
                    @LockTimeout = 0;
                SELECT @result;
                """).ConfigureAwait(false);

            if (result < 0)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return false;
            }

            _lockConnection = connection;
            return true;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task ReleaseLockAsync()
    {
        var connection = Interlocked.Exchange(ref _lockConnection, null);
        if (connection is null)
            return;

        try
        {
            await connection.ExecuteAsync(
                """
                EXEC sp_releaseapplock
                    @Resource = 'SiNetDailySync',
                    @LockOwner = 'Session';
                """).ConfigureAwait(false);
        }
        finally
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Legacy monitoring-table maintenance. Application locking is session-scoped and releases
    /// automatically when the database connection closes, so this does not affect mutual exclusion.
    /// </summary>
    public async Task ForceClearLockAsync()
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        var result = await connection.ExecuteAsync(@"
            UPDATE Sync_Lock SET AcquiredAt = NULL, AcquiredBy = NULL WHERE LockName = 'DailySync'");
        _logger.LogInformation("[OFFLINE] Force cleared sync lock. Rows affected: {Rows}", result);
    }

    private async Task RecordRunHistoryAsync(DailySyncResult result)
    {
        await using var connection = new SqlConnection(_replicaConnectionString);
        var totalRecords = result.EntityResults.Values.Sum(r => r.RecordsInserted + r.RecordsUpdated);
        var details = System.Text.Json.JsonSerializer.Serialize(result.EntityResults);

        await connection.ExecuteAsync(@"
            INSERT INTO Sync_RunHistory (StartTime, EndTime, Success, ErrorMessage, RecordsSynced, Details)
            VALUES (@StartTime, @EndTime, @Success, @ErrorMessage, @RecordsSynced, @Details)",
            new
            {
                result.StartTime,
                result.EndTime,
                result.Success,
                result.ErrorMessage,
                RecordsSynced = totalRecords,
                Details = details
            });
    }

    #endregion
}
