namespace SiNet.Application.Identity;

/// <summary>Hebrew-friendly labels for <see cref="AppAccUserType"/> in native admin UI.</summary>
public static class AppAccUserTypeDisplay
{
    public static string GetDisplayName(AppAccUserType value) => value switch
    {
        AppAccUserType.NoAccUser => "ללא ACC",
        AppAccUserType.Engineer => "מהנדס (Engineer)",
        AppAccUserType.Admin => "מנהל ACC (Admin)",
        _ => value.ToString(),
    };

    public static IReadOnlyList<AppAccUserType> AllValues { get; } =
        Enum.GetValues<AppAccUserType>().Cast<AppAccUserType>().ToArray();
}
