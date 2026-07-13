namespace SiNet.Application.Projects;

public interface IPlaceCatalogService
{
    Task<IReadOnlyList<PlaceDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<PlaceDto> SaveAsync(PlaceDto place, CancellationToken cancellationToken = default);
}

public interface ICompanyCatalogService
{
    Task<IReadOnlyList<CompanyDto>> ListCompaniesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactDto>> ListContactsAsync(
        int companyId,
        CancellationToken cancellationToken = default);

    Task<CompanyDto> AddCompanyAsync(string title, CancellationToken cancellationToken = default);

    Task<ContactDto> AddContactAsync(
        int companyId,
        string displayName,
        CancellationToken cancellationToken = default);
}

public interface IJobTypeQueryService
{
    Task<IReadOnlyList<JobTypeDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Default job type id for new projects ("חומר כללי"), or null if missing.</summary>
    Task<int?> ResolveDefaultJobTypeIdAsync(CancellationToken cancellationToken = default);
}
