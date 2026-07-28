namespace SiNet.Application.MasterPlan;

public sealed record MpCompanyOptionDto(
    int Id,
    string Name,
    string? Email,
    string? Phone,
    string? RegistrationNumber,
    string? Address,
    string? City);

public sealed record MpContactOptionDto(
    int Id,
    string FirstName,
    string FullName,
    string? CompanyName,
    int? CompanyId,
    string? Email,
    string? Phone,
    string? Mobile,
    string? Address);

public sealed record MasterPlanCompanyMappingDto(
    int SiNetId,
    string SiNetTitle,
    string? SiNetEmail,
    string? SiNetPhone,
    int ProjectCount,
    int ContactCount,
    bool IsActive,
    int? MasterPlanCompanyId,
    string? MatchStatus,
    bool IsAutoMatch);

public sealed record MasterPlanContactMappingDto(
    int SiNetId,
    string SiNetFullName,
    int? SiNetCompanyId,
    string? SiNetCompanyTitle,
    string? SiNetEmail,
    string? SiNetPhone,
    int ProjectCount,
    bool IsActive,
    int? MasterPlanContactId,
    string? MatchStatus,
    bool IsAutoMatch);

public sealed record MasterPlanMappingLoadResult(
    IReadOnlyList<MasterPlanCompanyMappingDto> Companies,
    IReadOnlyList<MasterPlanContactMappingDto> Contacts,
    IReadOnlyList<MpCompanyOptionDto> MpCompanies,
    IReadOnlyList<MpContactOptionDto> MpContacts,
    string? Warning);

public sealed record MasterPlanCompanyMappingChange(
    int SiNetId,
    int? MasterPlanCompanyId,
    bool IsActive);

public sealed record MasterPlanContactMappingChange(
    int SiNetId,
    int? MasterPlanContactId,
    bool IsActive);

public sealed record MasterPlanMappingApplyCommand(
    IReadOnlyList<MasterPlanCompanyMappingChange> Companies,
    IReadOnlyList<MasterPlanContactMappingChange> Contacts);

public sealed record MasterPlanMappingApplyResult(bool Succeeded, string Message, int CompaniesUpdated, int ContactsUpdated);

public sealed record MasterPlanCompleteMissingResult(bool Succeeded, string Message, int CompaniesCreated, int ContactsCreated);

public sealed record MasterPlanEnableFullSyncResult(bool Succeeded, string Message, int CompaniesUpdated, int ContactsUpdated);

public sealed record MasterPlanMappingPair(int SiNetId, int MasterPlanId);

public sealed record MasterPlanMappingExportDto(
    DateTime ExportDateUtc,
    IReadOnlyList<MasterPlanMappingPair> Companies,
    IReadOnlyList<MasterPlanMappingPair> Contacts);
