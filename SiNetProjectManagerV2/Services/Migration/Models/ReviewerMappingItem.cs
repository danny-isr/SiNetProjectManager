using System.Collections.Generic;

namespace SiNetProjectManagerV2.Services.Migration.Models;

public class ReviewerMappingItem
{
    public string SheetReviewerName { get; set; } = string.Empty;
    public int? SelectedUserId { get; set; }
    public string? SelectedUserDisplayName { get; set; }
    public string MappingStatus { get; set; } = string.Empty;
    public string WarningMessage { get; set; } = string.Empty;
    
    // UI specific properties
    public List<SystemUserLookupItem> AvailableUsers { get; set; } = new();
}

public class SystemUserLookupItem
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
