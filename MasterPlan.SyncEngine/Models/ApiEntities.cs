using System.Text.Json.Serialization;

namespace MasterPlan.SyncEngine.Models;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// API ENTITY MODELS - Exact schema from 20260213_010939 dump files
// ═══════════════════════════════════════════════════════════════════════════════════════════
// These models MUST match the JSON field names EXACTLY (case-insensitive deserialization).
// Source of truth: MasterPlan.SyncEngine\20260213_010939\*.ndjson files
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Project entity from MasterPlan API
/// Source: Projects.ndjson
/// </summary>
public class ProjectEntity
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public string? ProjectNum { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Description { get; set; }
    public string? CustomerName { get; set; }
    public int? CustomerID { get; set; }
    public int? EmployeeID { get; set; }
    public string? EmployeeName { get; set; }
    public int? StatusID { get; set; }
    public string? StatusName { get; set; }
    public int? ProjectTypeID { get; set; }
    public string? ProjectType { get; set; }
    public int? StudioDepartmentTypeID { get; set; }
    public string? StudioDepartmentType { get; set; }
    public bool IsActive { get; set; }
    public decimal? FeeSum { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Company entity from MasterPlan API
/// Source: Companies.ndjson
/// Fields: ID, Name, Address, city, Email, RegistrationNumber, PhoneNum, LastUpdated
/// </summary>
public class CompanyEntity
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? city { get; set; }  // Note: lowercase in API
    public string? Email { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? PhoneNum { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Contact entity from MasterPlan API
/// Source: Contacts.ndjson
/// Fields: ID, FirstName, LastName, CompanyName, CompanyID, Address, Email, Phone, Mobile, LastUpdated
/// </summary>
public class ContactEntity
{
    public int ID { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? CompanyName { get; set; }
    public int? CompanyID { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Employee entity from MasterPlan API
/// Source: Employees.ndjson
/// Fields: ID, FirstName, LastName, LastUpdated (minimal schema)
/// </summary>
public class EmployeeEntity
{
    public int ID { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Bid (Proposal) entity from MasterPlan API
/// Source: Bids.ndjson
/// Fields: ID, ProposalNum, Name, ActiveProposal, DateTime, EstimatedSum, ProbabilityID, 
///         ProbabilityName, StatusID, ProposalStatus, LastUpdated
/// </summary>
public class BidEntity
{
    public int ID { get; set; }
    public string? ProposalNum { get; set; }
    public string? Name { get; set; }
    public bool ActiveProposal { get; set; }
    public DateTime? DateTime { get; set; }
    public decimal? EstimatedSum { get; set; }
    public int? ProbabilityID { get; set; }
    public string? ProbabilityName { get; set; }
    public int? StatusID { get; set; }
    public string? ProposalStatus { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Bill (Invoice) entity from MasterPlan API
/// Source: Bills.ndjson
/// Fields: ID, BillNum, ProjectName, ProjectID, BillInternalNum, Sum, SubmitDate, CollectionDate,
///         Status, StatusID, ResponsibleEmployee, ResponsibleEmployeeID, StudioDepartment, 
///         StudioDepartmentTypeID, LastUpdated
/// </summary>
public class BillEntity
{
    public int ID { get; set; }
    public string? BillNum { get; set; }
    public string? ProjectName { get; set; }
    public int? ProjectID { get; set; }
    public string? BillInternalNum { get; set; }
    public decimal? Sum { get; set; }
    public DateTime? SubmitDate { get; set; }
    public DateTime? CollectionDate { get; set; }
    public string? Status { get; set; }
    public int? StatusID { get; set; }
    public string? ResponsibleEmployee { get; set; }
    public int? ResponsibleEmployeeID { get; set; }
    public string? StudioDepartment { get; set; }
    public int? StudioDepartmentTypeID { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Intake (Payment Receipt) entity from MasterPlan API
/// Source: Intakes.ndjson
/// Fields: ID, OpenDate, Sum, CustomerID, CustomerName, PaymentType, PayTypeID, Description, LastUpdated
/// </summary>
public class IntakeEntity
{
    public int ID { get; set; }
    public DateTime? OpenDate { get; set; }
    public decimal? Sum { get; set; }
    public int? CustomerID { get; set; }
    public string? CustomerName { get; set; }
    public string? PaymentType { get; set; }
    public int? PayTypeID { get; set; }
    public string? Description { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Task entity from MasterPlan API
/// Source: Tasks.ndjson
/// Fields: ID, TaskDescription, IsHandled, IsClosed, StartDate, DueDate, SenderName, SenderID,
///         ReceiverName, ReceiverID, CompletionDate, Priority, PriorityID, LastUpdated
/// </summary>
public class TaskEntity
{
    public int ID { get; set; }
    public string? TaskDescription { get; set; }
    public bool IsHandled { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? SenderName { get; set; }
    public int? SenderID { get; set; }
    public string? ReceiverName { get; set; }
    public int? ReceiverID { get; set; }
    public DateTime? CompletionDate { get; set; }
    public string? Priority { get; set; }
    public int? PriorityID { get; set; }
    public DateTime? LastUpdated { get; set; }
}

/// <summary>
/// Conversation (Call Log) entity from MasterPlan API
/// Source: Conversations.ndjson - NOTE: API returned 0 records, schema unknown
/// Using placeholder fields based on API guide
/// </summary>
public class ConversationEntity
{
    public int ID { get; set; }
    public int? ProjectID { get; set; }
    public string? ProjectName { get; set; }
    public int? ContactID { get; set; }
    public string? ContactName { get; set; }
    public int? EmployeeID { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Subject { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Project Hours entity from MasterPlan API
/// Source: ProjectHours.ndjson
/// Time fields stored as TIME(0) in DB, serialize as "HH:mm" for API
/// </summary>
public class ProjectHoursEntity
{
    public int ID { get; set; }
    public int? ProjectID { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectNumber { get; set; }
    public int? EmployeeID { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? ReportDate { get; set; }
    public string? StepName { get; set; }
    public string? Description { get; set; }

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? StartTime { get; set; }   // TIME(0), serialize as "HH:mm"

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? EndTime { get; set; }     // TIME(0), serialize as "HH:mm"

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? TotalHours { get; set; }  // Duration as TIME(0), serialize as "HH:mm"
}

/// <summary>
/// Time Hour Report entity from MasterPlan API
/// Endpoint: GET /api/projecthours/GetTimeHourReports
/// Filter: FromDate (filter by report date)
/// 
/// NOTE: This endpoint uses "DateTime" as the report date field name,
/// unlike ProjectHoursExtended which uses "ReportDate".
/// </summary>
public class TimeHourReportEntity
{
    public int ID { get; set; }
    public int? EmployeeID { get; set; }
    public string? EmployeeName { get; set; }

    /// <summary>
    /// Report date - named "DateTime" in the API response
    /// </summary>
    [JsonPropertyName("DateTime")]
    public DateTime? ReportDateTime { get; set; }

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? StartTime { get; set; }

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? EndTime { get; set; }

    /// <summary>
    /// Duration in decimal hours (e.g., 0.5 = 30 minutes)
    /// </summary>
    public decimal? Duration { get; set; }
}

/// <summary>
/// Extended Project Hours entity from MasterPlan API
/// Endpoint: GET /api/projecthours/GetProjectHoursExtended
/// 
/// Returns extended hours data including SubContract details.
/// Response is wrapped: { "data": [...] }
/// 
/// Nullable fields: SubContractStepID, SubContractStepName, HoursReportsStepID, LastUpdated
/// </summary>
public class ProjectHoursExtendedEntity
{
    public int ID { get; set; }
    public int? EmployeeID { get; set; }
    public string? EmployeeName { get; set; }
    public int? ProjectID { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectNumber { get; set; }

    /// <summary>
    /// SubContract (חוזה משנה) identifier
    /// </summary>
    public int? SubContractID { get; set; }
    public string? SubContractName { get; set; }

    /// <summary>
    /// SubContract step identifier - nullable
    /// </summary>
    public int? SubContractStepID { get; set; }
    public string? SubContractStepName { get; set; }

    /// <summary>
    /// Report date
    /// </summary>
    public DateTime? ReportDate { get; set; }

    public string? StepName { get; set; }

    /// <summary>
    /// Hours report step identifier - nullable
    /// </summary>
    public int? HoursReportsStepID { get; set; }

    public string? Description { get; set; }

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? StartTime { get; set; }

    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? EndTime { get; set; }

    /// <summary>
    /// Total hours in "HH:mm" format string
    /// </summary>
    [JsonConverter(typeof(TimeSpanHHmmConverter))]
    public TimeSpan? TotalHours { get; set; }

    /// <summary>
    /// Duration in decimal hours (e.g., 0.5 = 30 minutes)
    /// Provides numeric alternative to TotalHours string
    /// </summary>
    public decimal? Duration { get; set; }

    /// <summary>
    /// Last updated timestamp - nullable
    /// Used for incremental sync watermarking
    /// </summary>
    public DateTime? LastUpdated { get; set; }
}
