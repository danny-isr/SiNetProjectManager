using System;
using System.Collections.Generic;

namespace SiNetSQL.Models;

public partial  class WorkHour
{
    public int Id { get; set; }

    public int? ProjectId { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? Description { get; set; }

    public string? Location { get; set; }

    public string? FRecurrence { get; set; }

    public string? WorkspaceLink { get; set; }

    public string? Title { get; set; }

    public bool? FAllDayEvent { get; set; }

    public string? ParticipantsPicker { get; set; }

    public string? Category { get; set; }

    public string? Facilities { get; set; }

    public string? FreeBusy { get; set; }

    public string? Overbook { get; set; }

    public bool? PayByHomer { get; set; }

    public bool? Payd { get; set; }

    public bool? SendToPay { get; set; }

    public DateTime? Modified { get; set; }

    public DateTime? Created { get; set; }

    public int? AuthorId { get; set; }

    public int? EditorId { get; set; }

    public virtual Siuser? Author { get; set; }

    public virtual Siuser? Editor { get; set; }

    public virtual Project? Project { get; set; }
}
