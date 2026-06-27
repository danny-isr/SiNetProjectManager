using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class DrawingTypeAndLayersTable
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public int? GropNameId { get; set; }

    public int? ObjectsNameId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual DrawingType? GropName { get; set; }

    public virtual Layer? ObjectsName { get; set; }
}
