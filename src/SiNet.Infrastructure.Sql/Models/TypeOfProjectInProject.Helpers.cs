namespace SiNetSQL.Models;

public partial class TypeOfProjectInProject
{
    // Returns the AdminWorker's name if SIUser is not null; otherwise, returns an empty string.
    public string AdminWorkerName() => AdminWorker?.Name ?? "";

    // Returns the JobType's title if JobType is not null; otherwise, returns an empty string.
    public string TypeName() => this.ProjectType?.Title ?? "";
}
