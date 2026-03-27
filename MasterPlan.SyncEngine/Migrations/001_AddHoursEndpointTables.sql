-- ═══════════════════════════════════════════════════════════════════════════════════════════
-- Migration: Add tables for new MasterPlan Hours API endpoints
-- Created: 2026-02-26
-- 
-- This script adds support for:
-- 1. MP_TimeHourReports   - GET /api/projecthours/GetTimeHourReports
-- 2. MP_ProjectHoursExtended - GET /api/projecthours/GetProjectHoursExtended
--
-- EXECUTION INSTRUCTIONS:
-- Run this script against the Replica database using SSMS or sqlcmd
-- ═══════════════════════════════════════════════════════════════════════════════════════════

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- ═══════════════════════════════════════════════════════════════════════════════════════════
-- Table 1: MP_TimeHourReports
-- Source: GET /api/projecthours/GetTimeHourReports
-- 
-- NOTE: API field "DateTime" is mapped to "ReportDateTime" to avoid reserved word conflicts
-- ═══════════════════════════════════════════════════════════════════════════════════════════
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_TimeHourReports')
BEGIN
    CREATE TABLE MP_TimeHourReports (
        ID INT NOT NULL PRIMARY KEY,
        EmployeeID INT NULL,
        EmployeeName NVARCHAR(200) NULL,
        ReportDateTime DATETIME2 NULL,      -- Mapped from API "DateTime" field
        StartTime TIME(0) NULL,             -- "HH:mm" format
        EndTime TIME(0) NULL,               -- "HH:mm" format
        Duration DECIMAL(10, 4) NULL,       -- Decimal hours (e.g., 0.5 = 30 min)
        
        -- Audit columns
        SyncedAt DATETIME2 DEFAULT GETUTCDATE()
    );

    -- Index for filtering by report date (used in incremental sync)
    CREATE NONCLUSTERED INDEX IX_MP_TimeHourReports_ReportDateTime 
        ON MP_TimeHourReports (ReportDateTime);

    -- Index for employee lookups
    CREATE NONCLUSTERED INDEX IX_MP_TimeHourReports_EmployeeID 
        ON MP_TimeHourReports (EmployeeID);

    PRINT 'Created table: MP_TimeHourReports';
END
ELSE
BEGIN
    PRINT 'Table MP_TimeHourReports already exists - skipping';
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════════════════
-- Table 2: MP_ProjectHoursExtended
-- Source: GET /api/projecthours/GetProjectHoursExtended
-- 
-- Extended hours data including SubContract details
-- NOTE: Has both TotalHours (TimeSpan) and Duration (decimal) for flexibility
-- NOTE: LastUpdated field supports incremental sync watermarking
-- ═══════════════════════════════════════════════════════════════════════════════════════════
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MP_ProjectHoursExtended')
BEGIN
    CREATE TABLE MP_ProjectHoursExtended (
        ID INT NOT NULL PRIMARY KEY,
        EmployeeID INT NULL,
        EmployeeName NVARCHAR(200) NULL,
        ProjectID INT NULL,
        ProjectName NVARCHAR(500) NULL,
        ProjectNumber NVARCHAR(50) NULL,
        
        -- SubContract (חוזה משנה) details
        SubContractID INT NULL,
        SubContractName NVARCHAR(500) NULL,
        SubContractStepID INT NULL,         -- Nullable per API docs
        SubContractStepName NVARCHAR(200) NULL, -- Nullable per API docs
        
        ReportDate DATETIME2 NULL,
        StepName NVARCHAR(200) NULL,
        HoursReportsStepID INT NULL,        -- Nullable per API docs
        Description NVARCHAR(MAX) NULL,
        
        StartTime TIME(0) NULL,             -- "HH:mm" format
        EndTime TIME(0) NULL,               -- "HH:mm" format
        TotalHours TIME(0) NULL,            -- "HH:mm" format
        Duration DECIMAL(10, 4) NULL,       -- Decimal hours (e.g., 0.5 = 30 min)
        
        LastUpdated DATETIME2 NULL,         -- For incremental sync watermark
        
        -- Audit columns
        SyncedAt DATETIME2 DEFAULT GETUTCDATE()
    );

    -- Index for filtering by report date (used in incremental sync via FromDate)
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHoursExtended_ReportDate 
        ON MP_ProjectHoursExtended (ReportDate);

    -- Index for LastUpdated watermarking
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHoursExtended_LastUpdated 
        ON MP_ProjectHoursExtended (LastUpdated);

    -- Index for project lookups
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHoursExtended_ProjectID 
        ON MP_ProjectHoursExtended (ProjectID);

    -- Index for employee lookups
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHoursExtended_EmployeeID 
        ON MP_ProjectHoursExtended (EmployeeID);

    -- Index for SubContract lookups
    CREATE NONCLUSTERED INDEX IX_MP_ProjectHoursExtended_SubContractID 
        ON MP_ProjectHoursExtended (SubContractID)
        WHERE SubContractID IS NOT NULL;

    PRINT 'Created table: MP_ProjectHoursExtended';
END
ELSE
BEGIN
    PRINT 'Table MP_ProjectHoursExtended already exists - skipping';
END
GO

-- ═══════════════════════════════════════════════════════════════════════════════════════════
-- Initialize Sync_State entries for new entities
-- ═══════════════════════════════════════════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM Sync_State WHERE EntityName = 'TimeHourReports')
BEGIN
    INSERT INTO Sync_State (EntityName, LastWatermark, LastSyncTime)
    VALUES ('TimeHourReports', NULL, NULL);
    PRINT 'Initialized Sync_State for TimeHourReports';
END

IF NOT EXISTS (SELECT 1 FROM Sync_State WHERE EntityName = 'ProjectHoursExtended')
BEGIN
    INSERT INTO Sync_State (EntityName, LastWatermark, LastSyncTime)
    VALUES ('ProjectHoursExtended', NULL, NULL);
    PRINT 'Initialized Sync_State for ProjectHoursExtended';
END
GO

COMMIT TRANSACTION;
PRINT '═══════════════════════════════════════════════════════════════════════════════════════════';
PRINT 'Migration completed successfully!';
PRINT '═══════════════════════════════════════════════════════════════════════════════════════════';
