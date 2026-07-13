using SiNet.Application.Projects;
using SiNetSQL.Services;

namespace SiNetProjectManagerV2.Services;

/// <summary>Host adapter that creates on-disk project folders via the legacy folder creator.</summary>
internal sealed class LegacyProjectFolderBootstrapper : IProjectFolderBootstrapper
{
    public void CreateFolders(int projectId) =>
        new ProjectFolderCreator().CreateProjectFolders(projectId);
}
