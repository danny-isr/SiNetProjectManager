using SiNet.Application.Settings;
using SiNet.Domain.Files;

namespace SiNet.Application.ProjectWork;

/// <summary>
/// Caches <see cref="ProjectWorkScanExclusions"/> parsed from
/// <see cref="SystemSettingKeys.ProjectWorkScanExclusionRules"/>.
/// </summary>
public sealed class SettingsBackedProjectWorkScanExclusionPolicy : IProjectWorkScanExclusionPolicy
{
    private readonly ISystemSettingsQueryService? _settings;
    private readonly object _gate = new();
    private ParsedProjectWorkScanExclusions _rules = ProjectWorkScanExclusions.Default;

    public SettingsBackedProjectWorkScanExclusionPolicy(ISystemSettingsQueryService? settings = null)
    {
        _settings = settings;
    }

    /// <inheritdoc />
    public ParsedProjectWorkScanExclusions CurrentRules
    {
        get
        {
            lock (_gate)
            {
                return _rules;
            }
        }
    }

    /// <inheritdoc />
    public bool ShouldExclude(string? fullPathOrName) =>
        ProjectWorkScanExclusions.Matches(fullPathOrName, CurrentRules);

    /// <inheritdoc />
    public void ReplaceRules(string? rulesCsv)
    {
        var parsed = ProjectWorkScanExclusions.Parse(rulesCsv);
        lock (_gate)
        {
            _rules = parsed;
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_settings is null)
        {
            ReplaceRules(ProjectWorkScanExclusions.DefaultRulesCsv);
            return;
        }

        var dto = await _settings.GetSystemSettingsAsync(cancellationToken).ConfigureAwait(false);
        ReplaceRules(dto.ProjectWork.ScanExclusionRules);
    }
}
