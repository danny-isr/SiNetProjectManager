namespace SiNet.Application.Abstractions.Email;

/// <summary>Filter for project-link state on mailbox list queries.</summary>
public enum EmailProjectLinkFilter
{
    All = 0,
    Linked = 1,
    Unlinked = 2,
}
