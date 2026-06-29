namespace SiNet.App.Wpf.Inspection;

/// <summary>
/// Placeholder view model for the Inspection report area. Report generation and sent/locked
/// actions remain explicitly out of scope and stay in the legacy window; the foundation only
/// provides a header + placeholder.
/// </summary>
public sealed class InspectionReportViewModel : ObservableObject
{
    public string Title => "Report";

    public string Placeholder { get; } = "Report generation — remains in legacy window.";
}
