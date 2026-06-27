using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class TabaDatum
{
    public int Id { get; set; }

    public string? Title { get; set; }

    public string? Seyf { get; set; }

    public string? Grop { get; set; }

    public float? Code { get; set; }

    public float? LayerColorA { get; set; }

    public float? LayerColorB { get; set; }

    public float? LayerColorBb { get; set; }

    public float? LayerColorBa { get; set; }

    public float? LayerColorAa { get; set; }

    public float? TavnitType { get; set; }

    public float? TavnitType2 { get; set; }

    public bool? ToRemove { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }
}
