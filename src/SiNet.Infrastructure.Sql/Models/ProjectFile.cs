using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class ProjectFile
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public float? Number { get; set; }

    public string? Des { get; set; }

    public int? Folderid { get; set; }

    public string? Typefile { get; set; }

    public int? TypeProjId { get; set; }

    public string? TemplateLocation { get; set; }

    public bool? LookAtDes { get; set; }

    public bool? OutSidData { get; set; }

    /// <summary>
    /// When true, this catalog slot is mandatory for completion of tasks that gate on required files
    /// (e.g. PrepareQuoteCalculation / תחשיב). Missing physical versions are shown as orange in ProjectWork.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default storage destination for files of this type.
    /// FileServer = network share (legacy), Acc = Autodesk Construction Cloud.
    /// </summary>
    public FileStorageDestination StorageDestination { get; set; } = FileStorageDestination.FileServer;

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual ProjectFolder? Folder { get; set; }

    public virtual ICollection<ProjectFileRef> ProjectFileRefFiles { get; set; } = new List<ProjectFileRef>();

    public virtual ICollection<ProjectFileRef> ProjectFileRefXrefs { get; set; } = new List<ProjectFileRef>();

    public virtual JobType? TypeProj { get; set; }
}
