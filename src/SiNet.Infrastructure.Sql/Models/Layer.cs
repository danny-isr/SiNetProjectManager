using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Layer
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? LayerName { get; set; }

    public int? LayerObjectTypeId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual ICollection<DrawingTypeAndLayersTable> DrawingTypeAndLayersTables { get; set; } = new List<DrawingTypeAndLayersTable>();

    public virtual Siuser? Editor { get; set; }

    public virtual LayerObjectType? LayerObjectType { get; set; }

    public virtual ICollection<Property> Properties { get; set; } = new List<Property>();
}
