using SiNet.App.Wpf.Admin.SystemStatus;
using SiNet.Application.Runtime;
using Xunit;

namespace SiNet.App.Wpf.Tests.Runtime;

public sealed class SystemStatusGuidanceCatalogTests
{
    [Fact]
    public void Resolve_acc_service_ssl_summary_returns_tls_guidance()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "acc-service",
            SubsystemRuntimeState.Degraded,
            "לא זמין — SSL connection cannot be established");

        Assert.NotNull(guidance);
        Assert.Contains("thumbprint", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("הגדרות", guidance, StringComparison.Ordinal);
        Assert.Contains("הפעל מחדש", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_workflow_assignees_returns_groups_guidance()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "workflow-assignees",
            SubsystemRuntimeState.Degraded,
            "22 שלבים ללא assignee ניתן לפתרון · 3 קבוצות");

        Assert.NotNull(guidance);
        Assert.Contains("הקצאות", guidance, StringComparison.Ordinal);
        Assert.Contains("OfficeManagement", guidance, StringComparison.Ordinal);
        Assert.Contains("Seed", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_autodesk_two_legged_returns_oauth_guidance()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "autodesk-acc",
            SubsystemRuntimeState.Degraded,
            "2-legged בלבד — פעולות Admin ידרשו התחברות");

        Assert.NotNull(guidance);
        Assert.Contains("3-legged", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_acc_service_401_returns_api_key_guidance_not_tls()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "acc-service",
            SubsystemRuntimeState.Degraded,
            "זמין — HTTP 401, המפתח נדחה");

        Assert.NotNull(guidance);
        Assert.Contains("ייבוא מפתחות תחנה", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("thumbprint", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pinned Certificate", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_google_timeout_returns_timeout_guidance_not_empty_mailbox()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "google",
            SubsystemRuntimeState.Degraded,
            "הבדיקה חרגה מ-10 שניות");

        Assert.NotNull(guidance);
        Assert.Contains("רענון", guidance, StringComparison.Ordinal);
        Assert.Contains("לא אומר שאין הודעות", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_idle_healthy_returns_null()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "acc-service",
            SubsystemRuntimeState.Idle,
            "זמין — https://localhost:8443");

        Assert.Null(guidance);
    }

    [Fact]
    public void WithGuidance_preserves_existing_guidance()
    {
        var status = new SubsystemRuntimeStatus(
            "acc-service",
            "SiOffice.AccService (פנימי)",
            SubsystemRuntimeState.Degraded,
            null,
            "לא זמין — SSL",
            DateTimeOffset.UtcNow,
            GuidanceHe: "הנחיה מותאמת");

        var enriched = SystemStatusGuidanceCatalog.WithGuidance(status);

        Assert.Equal("הנחיה מותאמת", enriched.GuidanceHe);
    }

    [Fact]
    public void Resolve_gmail_disconnected_returns_guidance()
    {
        var guidance = SystemStatusGuidanceCatalog.Resolve(
            "gmail",
            SubsystemRuntimeState.Stopped,
            "לא מחובר");

        Assert.NotNull(guidance);
        Assert.Contains("Gmail", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemStatusRowViewModel_From_fills_guidance_for_ssl_row()
    {
        var status = new SubsystemRuntimeStatus(
            "acc-service",
            "SiOffice.AccService (פנימי)",
            SubsystemRuntimeState.Degraded,
            null,
            "לא זמין — SSL connection cannot be established",
            DateTimeOffset.UtcNow);

        var row = SystemStatusRowViewModel.From(status);

        Assert.True(row.HasGuidance);
        Assert.Contains("thumbprint", row.Guidance, StringComparison.OrdinalIgnoreCase);
    }
}
