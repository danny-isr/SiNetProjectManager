-- ============================================================================
-- MasterPlan Replica Database - Table Definitions
-- Generated from actual API responses (20260213_010939/*.ndjson dump files)
-- ============================================================================
-- IMPORTANT: These schemas match the EXACT field names returned by the API.
-- Do NOT modify column names without updating ApiEntities.cs and ApiDailySyncService.cs
-- ============================================================================
-- !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
-- !! WARNING: THIS FILE IS DOCUMENTATION ONLY - NOT EXECUTED AT RUNTIME !!
-- !! The actual schema is EMBEDDED in MonthlyBackupRestoreService.cs        !!
-- !! in the CreateReplicaSchemaAsync() method.                              !!
-- !! To change the schema: edit BOTH this file AND the C# code.             !!
-- !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
-- ============================================================================

-- ============================================================================
-- MP_Projects
-- Source: projects/ endpoint
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Projects')
CREATE TABLE MP_Projects (
    ID INT PRIMARY KEY,
    Name NVARCHAR(500),
    ProjectNum NVARCHAR(100),
    StartDate DATETIME2,
    EndDate DATETIME2,
    Description NVARCHAR(MAX),
    CustomerName NVARCHAR(500),
    CustomerID INT,
    EmployeeID INT,
    EmployeeName NVARCHAR(500),
    StatusID INT,
    StatusName NVARCHAR(200),
    ProjectTypeID INT,
    ProjectType NVARCHAR(200),
    StudioDepartmentTypeID INT,
    StudioDepartmentType NVARCHAR(200),
    IsActive BIT,
    FeeSum DECIMAL(18, 2),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Companies
-- Source: Companies/ endpoint
-- Note: "city" is lowercase in API response!
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Companies')
CREATE TABLE MP_Companies (
    ID INT PRIMARY KEY,
    Name NVARCHAR(500),
    Address NVARCHAR(500),
    City NVARCHAR(200),              -- Maps from "city" (lowercase in JSON)
    Email NVARCHAR(500),
    RegistrationNumber NVARCHAR(100),
    PhoneNum NVARCHAR(100),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Contacts
-- Source: Contact/ endpoint
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Contacts')
CREATE TABLE MP_Contacts (
    ID INT PRIMARY KEY,
    FirstName NVARCHAR(200),
    LastName NVARCHAR(200),
    CompanyName NVARCHAR(500),
    CompanyID INT,
    Address NVARCHAR(500),
    Email NVARCHAR(500),
    Phone NVARCHAR(100),
    Mobile NVARCHAR(100),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Employees
-- Source: Employee/ endpoint
-- Note: API returns minimal fields only (ID, FirstName, LastName, LastUpdated)
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Employees')
CREATE TABLE MP_Employees (
    ID INT PRIMARY KEY,
    FirstName NVARCHAR(200),
    LastName NVARCHAR(200),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Bids
-- Source: bid/ endpoint
-- Note: Uses ProposalNum (not BidNum), DateTime (not BidDate), EstimatedSum (not Amount)
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Bids')
CREATE TABLE MP_Bids (
    ID INT PRIMARY KEY,
    ProposalNum NVARCHAR(100),
    Name NVARCHAR(500),
    ActiveProposal BIT,
    [DateTime] DATETIME2,            -- Reserved word, needs brackets
    EstimatedSum DECIMAL(18, 2),
    ProbabilityID INT,
    ProbabilityName NVARCHAR(200),
    StatusID INT,
    ProposalStatus NVARCHAR(200),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Bills
-- Source: Bill/ endpoint
-- Note: Uses Sum (not Amount), Status (not StatusName), ResponsibleEmployee fields
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Bills')
CREATE TABLE MP_Bills (
    ID INT PRIMARY KEY,
    BillNum NVARCHAR(100),
    ProjectName NVARCHAR(500),
    ProjectID INT,
    BillInternalNum NVARCHAR(100),
    [Sum] DECIMAL(18, 2),            -- Reserved word, needs brackets
    SubmitDate DATETIME2,
    CollectionDate DATETIME2,
    Status NVARCHAR(200),
    StatusID INT,
    ResponsibleEmployee NVARCHAR(500),
    ResponsibleEmployeeID INT,
    StudioDepartment NVARCHAR(200),
    StudioDepartmentTypeID INT,
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Intakes
-- Source: Intake/ endpoint
-- Note: Uses Sum (not Amount), PaymentType field, Description field
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Intakes')
CREATE TABLE MP_Intakes (
    ID INT PRIMARY KEY,
    OpenDate DATETIME2,
    [Sum] DECIMAL(18, 2),            -- Reserved word, needs brackets
    CustomerID INT,
    CustomerName NVARCHAR(500),
    PaymentType NVARCHAR(200),
    PayTypeID INT,
    Description NVARCHAR(MAX),
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Tasks
-- Source: Tasks/ endpoint
-- Note: Uses TaskDescription, IsHandled, IsClosed, Sender/Receiver fields
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Tasks')
CREATE TABLE MP_Tasks (
    ID INT PRIMARY KEY,
    TaskDescription NVARCHAR(MAX),
    IsHandled BIT,
    IsClosed BIT,
    StartDate DATETIME2,
    DueDate DATETIME2,
    SenderName NVARCHAR(500),
    SenderID INT,
    ReceiverName NVARCHAR(500),
    ReceiverID INT,
    CompletionDate DATETIME2,
    Priority NVARCHAR(100),
    PriorityID INT,
    LastUpdated DATETIME2
);
GO

-- ============================================================================
-- MP_Conversations
-- Source: Conversations/ endpoint
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_Conversations')
CREATE TABLE MP_Conversations (
    ID INT PRIMARY KEY,
    ProjectID INT,
    ProjectName NVARCHAR(500),
    ContactID INT,
    ContactName NVARCHAR(500),
    EmployeeID INT,
    EmployeeName NVARCHAR(500),
    CreatedDate DATETIME2,
    DueDate DATETIME2,
    Subject NVARCHAR(500),
    Notes NVARCHAR(MAX)
);
GO

-- ============================================================================
-- MP_ProjectHours
-- Source: ProjectHours/ endpoint
-- Note: Uses ReportDate (not WorkDate)
-- Storage: All time fields stored as TIME(0), serialize as "HH:mm" for API
-- ============================================================================
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_ProjectHours')
CREATE TABLE MP_ProjectHours (
    ID INT PRIMARY KEY,
    ProjectID INT,
    ProjectName NVARCHAR(500),
    ProjectNumber NVARCHAR(100),
    EmployeeID INT,
    EmployeeName NVARCHAR(500),
    ReportDate DATE,                 -- Stored as date only
    StepName NVARCHAR(200),
    Description NVARCHAR(MAX),
    StartTime TIME(0),               -- Time of day, serialize as "HH:mm"
    EndTime TIME(0),                 -- Time of day, serialize as "HH:mm"
    TotalHours TIME(0)               -- Duration as time, serialize as "HH:mm"
);
GO

-- ============================================================================
-- Create indexes for common query patterns
-- ============================================================================

-- Projects indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Projects_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Projects_LastUpdated ON MP_Projects(LastUpdated);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Projects_CustomerID')
    CREATE NONCLUSTERED INDEX IX_MP_Projects_CustomerID ON MP_Projects(CustomerID);

-- Companies indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Companies_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Companies_LastUpdated ON MP_Companies(LastUpdated);

-- Contacts indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Contacts_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Contacts_LastUpdated ON MP_Contacts(LastUpdated);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Contacts_CompanyID')
    CREATE NONCLUSTERED INDEX IX_MP_Contacts_CompanyID ON MP_Contacts(CompanyID);

-- Bids indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Bids_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Bids_LastUpdated ON MP_Bids(LastUpdated);

-- Bills indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Bills_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Bills_LastUpdated ON MP_Bills(LastUpdated);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Bills_ProjectID')
    CREATE NONCLUSTERED INDEX IX_MP_Bills_ProjectID ON MP_Bills(ProjectID);

-- Intakes indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Intakes_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Intakes_LastUpdated ON MP_Intakes(LastUpdated);

-- Tasks indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Tasks_LastUpdated')
    CREATE NONCLUSTERED INDEX IX_MP_Tasks_LastUpdated ON MP_Tasks(LastUpdated);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Tasks_DueDate')
    CREATE NONCLUSTERED INDEX IX_MP_Tasks_DueDate ON MP_Tasks(DueDate);

-- Conversations indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Conversations_CreatedDate')
    CREATE NONCLUSTERED INDEX IX_MP_Conversations_CreatedDate ON MP_Conversations(CreatedDate);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_Conversations_ProjectID')
    CREATE NONCLUSTERED INDEX IX_MP_Conversations_ProjectID ON MP_Conversations(ProjectID);

-- ProjectHours indexes
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_ProjectHours_ReportDate')
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHours_ReportDate ON MP_ProjectHours(ReportDate);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_ProjectHours_ProjectID')
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHours_ProjectID ON MP_ProjectHours(ProjectID);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MP_ProjectHours_EmployeeID')
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHours_EmployeeID ON MP_ProjectHours(EmployeeID);

GO

PRINT 'MasterPlan Replica tables created successfully.';
PRINT 'Schema matches API dump files from 20260213_010939/';
