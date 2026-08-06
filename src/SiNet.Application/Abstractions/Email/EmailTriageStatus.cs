namespace SiNet.Application.Abstractions.Email;

/// <summary>Non-project Gmail triage labels applied from the email list context menu.</summary>
public enum EmailTriageStatus
{
    Pending,
    Personal,
    Irrelevant,
    /// <summary>Stage-2 FYI — filed project mail that needs no further workflow.</summary>
    Fyi,
}
