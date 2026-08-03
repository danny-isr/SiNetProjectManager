using System.Text;
using System.Text.RegularExpressions;

namespace SiNet.Application.MasterPlan;

/// <summary>
/// In-memory AutoMatch for MasterPlan mapping (ported scoring from legacy VM).
/// Threshold ≥ 6, and a match is accepted only when it has identity evidence
/// (name and/or registration number) — email/phone alone are not enough.
/// See docs/MASTER_PLAN_MIGRATION.md §S2 AutoMatch rules.
/// </summary>
public static class MasterPlanAutoMatchEngine
{
    private const int MinScore = 6;
    private static readonly Regex NineDigitRegistration = new(@"\d{9}", RegexOptions.Compiled);

    public static MasterPlanMappingLoadResult Apply(
        MasterPlanMappingLoadResult source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var usedCompanyIds = new HashSet<int>();
        var usedContactIds = new HashSet<int>();

        foreach (var row in source.Companies)
        {
            if (row.MasterPlanCompanyId is int existing)
            {
                usedCompanyIds.Add(existing);
            }
        }

        foreach (var row in source.Contacts)
        {
            if (row.MasterPlanContactId is int existing)
            {
                usedContactIds.Add(existing);
            }
        }

        var companies = source.Companies
            .OrderByDescending(c => c.ProjectCount + c.ContactCount)
            .Select(c => MatchCompany(c, source.MpCompanies, usedCompanyIds))
            .OrderBy(c => c.SiNetTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Company map for contact company-id filter after company matches applied.
        var companyMpBySiNet = companies
            .Where(c => c.MasterPlanCompanyId is not null)
            .ToDictionary(c => c.SiNetId, c => c.MasterPlanCompanyId!.Value);

        var contacts = source.Contacts
            .OrderByDescending(c => c.ProjectCount)
            .ThenByDescending(c => c.SiNetCompanyId is int id && companyMpBySiNet.ContainsKey(id))
            .Select(c => MatchContact(c, source.MpContacts, usedContactIds, companyMpBySiNet))
            .OrderBy(c => c.SiNetFullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return source with { Companies = companies, Contacts = contacts };
    }

    private static MasterPlanCompanyMappingDto MatchCompany(
        MasterPlanCompanyMappingDto row,
        IReadOnlyList<MpCompanyOptionDto> options,
        HashSet<int> used)
    {
        if (row.MasterPlanCompanyId is not null)
        {
            return row;
        }

        var bestScore = 0;
        var bestHasIdentity = false;
        MpCompanyOptionDto? best = null;
        var name = NormalizeCompanyName(row.SiNetTitle);
        var email = NormalizeEmail(row.SiNetEmail);
        var phone = NormalizePhone(row.SiNetPhone);
        var registrationCandidates = ExtractRegistrationNumbers(row.SiNetTitle);

        foreach (var option in options)
        {
            if (used.Contains(option.Id))
            {
                continue;
            }

            var score = 0;
            var hasIdentity = false;
            var mpName = NormalizeCompanyName(option.Name);
            if (!string.IsNullOrEmpty(name) && name == mpName)
            {
                score += 10;
                hasIdentity = true;
            }
            else if (!string.IsNullOrEmpty(name) && name.Length >= 3 && mpName.Length >= 3
                     && (name.Contains(mpName, StringComparison.Ordinal) || mpName.Contains(name, StringComparison.Ordinal)))
            {
                score += 6;
                hasIdentity = true;
            }

            var mpReg = NormalizeRegistration(option.RegistrationNumber);
            if (registrationCandidates.Count > 0
                && !string.IsNullOrEmpty(mpReg)
                && registrationCandidates.Contains(mpReg))
            {
                score += 10;
                hasIdentity = true;
            }

            if (!string.IsNullOrEmpty(email) && email == NormalizeEmail(option.Email))
            {
                score += 8;
            }

            if (!string.IsNullOrEmpty(phone) && phone == NormalizePhone(option.Phone))
            {
                score += 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHasIdentity = hasIdentity;
                best = option;
            }
        }

        if (best is null || bestScore < MinScore || !bestHasIdentity)
        {
            return row;
        }

        used.Add(best.Id);
        return row with
        {
            MasterPlanCompanyId = best.Id,
            MatchStatus = $"אוטומטי ({bestScore})",
            IsAutoMatch = true,
        };
    }

    private static MasterPlanContactMappingDto MatchContact(
        MasterPlanContactMappingDto row,
        IReadOnlyList<MpContactOptionDto> options,
        HashSet<int> used,
        IReadOnlyDictionary<int, int> companyMpBySiNet)
    {
        if (row.MasterPlanContactId is not null)
        {
            return row;
        }

        int? requiredMpCompanyId = null;
        if (row.SiNetCompanyId is int siNetCompanyId
            && companyMpBySiNet.TryGetValue(siNetCompanyId, out var mappedCompany))
        {
            requiredMpCompanyId = mappedCompany;
        }

        var bestScore = 0;
        var bestHasIdentity = false;
        MpContactOptionDto? best = null;
        var name = NormalizeContactName(row.SiNetFullName);
        var email = NormalizeEmail(row.SiNetEmail);
        var phone = NormalizePhone(row.SiNetPhone);

        foreach (var option in options)
        {
            if (used.Contains(option.Id))
            {
                continue;
            }

            if (requiredMpCompanyId is int required)
            {
                if (option.CompanyId is null || option.CompanyId.Value != required)
                {
                    continue;
                }
            }

            var score = 0;
            var hasIdentity = false;
            var mpName = NormalizeContactName(option.FullName);
            if (!string.IsNullOrEmpty(name) && name == mpName)
            {
                score += 10;
                hasIdentity = true;
            }
            else if (!string.IsNullOrEmpty(name) && name.Length >= 3 && mpName.Length >= 3
                     && (name.Contains(mpName, StringComparison.Ordinal) || mpName.Contains(name, StringComparison.Ordinal)))
            {
                score += 6;
                hasIdentity = true;
            }

            if (!string.IsNullOrEmpty(email) && email == NormalizeEmail(option.Email))
            {
                score += 8;
            }

            var mpPhone = NormalizePhone(option.Phone);
            var mpMobile = NormalizePhone(option.Mobile);
            if (!string.IsNullOrEmpty(phone) && (phone == mpPhone || phone == mpMobile))
            {
                score += 4;
            }

            if (requiredMpCompanyId is not null
                && option.CompanyId == requiredMpCompanyId)
            {
                score += 2;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHasIdentity = hasIdentity;
                best = option;
            }
        }

        if (best is null || bestScore < MinScore || !bestHasIdentity)
        {
            return row;
        }

        used.Add(best.Id);
        return row with
        {
            MasterPlanContactId = best.Id,
            MatchStatus = $"אוטומטי ({bestScore})",
            IsAutoMatch = true,
        };
    }

    private static HashSet<string> ExtractRegistrationNumbers(string? title)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(title))
        {
            return result;
        }

        foreach (Match match in NineDigitRegistration.Matches(title))
        {
            result.Add(match.Value);
        }

        return result;
    }

    private static string NormalizeRegistration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = NormalizePhone(value);
        return digits.Length == 9 ? digits : string.Empty;
    }

    private static string NormalizeCompanyName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim().ToLowerInvariant()
            .Replace('״', '"')
            .Replace('׳', '\'');
        s = Regex.Replace(s, @"בע""מ|בע""ח|co\.ltd|co\.il|ltd\.|inc\.|llc\.|corp\.", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s]", " ");
        s = Regex.Replace(s, @"\b(בעמ|בעח|ltd|inc|llc|corp)\b", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static string NormalizeContactName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^\p{L}\p{Nd}\s]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string NormalizeEmail(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsDigit(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
