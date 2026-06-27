namespace SiNetSQL.Models;

public partial class TypeOfProjectInProject
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? ProjectTypeId { get; set; }

    public int? ProjectId { get; set; }

    public int? AdminWorkerId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? AdminWorker { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Project? Project { get; set; }

    public virtual JobType? ProjectType { get; set; }
}
