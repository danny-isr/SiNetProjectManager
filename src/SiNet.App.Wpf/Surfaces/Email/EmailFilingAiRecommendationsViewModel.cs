using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SiNet.App.Wpf.Inspection;
using SiNet.App.Wpf.Shell;
using SiNet.Application.Email;
using SiNet.Application.Projects;

namespace SiNet.App.Wpf.Surfaces.Email;

/// <summary>Filing-picker suggestion chips. Click selects a real project DTO; never files.</summary>
internal sealed class EmailFilingAiRecommendationsViewModel : ObservableObject
{
    private bool _isBusy;
    private string? _statusMessageHe;

    public EmailFilingAiRecommendationsViewModel()
    {
        Items = new ObservableCollection<EmailFilingAiRecommendationItem>();
        ChooseCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is EmailFilingAiRecommendationItem item)
                {
                    ProjectChosen?.Invoke(item.Project);
                }
            });
    }

    public ObservableCollection<EmailFilingAiRecommendationItem> Items { get; }

    public ICommand ChooseCommand { get; }

    public event Action<ProjectSummaryDto>? ProjectChosen;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string? StatusMessageHe
    {
        get => _statusMessageHe;
        private set => SetField(ref _statusMessageHe, value);
    }

    public bool HasItems => Items.Count > 0;

    /// <summary>Kept for the unused live-AI path. The picker no longer calls this.</summary>
    public void BeginLoading()
    {
        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
        IsBusy = true;
        StatusMessageHe = "מחפש פרויקטים מתאימים...";
    }

    public void ApplyLocal(IReadOnlyList<EmailProjectSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        Items.Clear();
        foreach (var row in suggestions)
        {
            Items.Add(new EmailFilingAiRecommendationItem(row.Project, 0, row.ExplanationHe));
        }

        OnPropertyChanged(nameof(HasItems));
        IsBusy = false;
        StatusMessageHe = null;
    }

    public void Apply(EmailProjectRecommendationResult result, int generation, EmailProjectPickerAiSession session)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsCurrent(generation))
        {
            return;
        }

        Items.Clear();
        foreach (var row in result.Recommendations)
        {
            Items.Add(new EmailFilingAiRecommendationItem(row.Project, row.Confidence, row.Reason));
        }

        OnPropertyChanged(nameof(HasItems));
        IsBusy = false;
        StatusMessageHe = Items.Count == 0
            ? (string.IsNullOrWhiteSpace(result.StatusMessageHe) ? "לא נמצאה המלצת AI" : result.StatusMessageHe)
            : null;
    }

    public void ApplyUnavailable(int generation, EmailProjectPickerAiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsCurrent(generation))
        {
            return;
        }

        Items.Clear();
        OnPropertyChanged(nameof(HasItems));
        IsBusy = false;
        StatusMessageHe = "המלצת AI אינה זמינה כרגע";
    }
}

internal sealed record EmailFilingAiRecommendationItem(
    ProjectSummaryDto Project,
    double Confidence,
    string? Reason)
{
    public string Caption =>
        string.IsNullOrWhiteSpace(Project.ProjectName)
            ? Project.ProjectNumber
            : $"{Project.ProjectNumber} — {Project.ProjectName}";

    public string? SecondaryText => string.IsNullOrWhiteSpace(Reason) ? null : Reason;

    public string ConfidenceText => Confidence.ToString("P0", CultureInfo.CurrentCulture);
}
