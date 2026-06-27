using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class Property
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public float? Color { get; set; }

    public string? Linetype { get; set; }

    public float? Lineweight { get; set; }

    public bool? Plottable { get; set; }

    public string? PlotStyleName { get; set; }

    public bool? ViewportDefault { get; set; }

    public string? Description { get; set; }

    public string? DescriptionX0020He { get; set; }

    public int? MifratId { get; set; }

    public int? LayersId { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Layer? Layers { get; set; }

    public virtual Mifrat? Mifrat { get; set; }
}
