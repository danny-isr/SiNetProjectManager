namespace SiNet.Application.Tasks;

/// <summary>
/// Personal work-queue bucket values for <see cref="ProjectAssignment.WorkQueueBucket"/>.
/// </summary>
public static class WorkQueueBucketCodes
{
    public const int Quick = 1;
    public const int Medium = 2;
    public const int Long = 3;

    public static bool IsValid(int bucket) => bucket is Quick or Medium or Long;

    public static string ToCode(int bucket) => bucket switch
    {
        Quick => nameof(Quick),
        Medium => nameof(Medium),
        Long => nameof(Long),
        _ => nameof(Medium),
    };

    public static string ToDisplayName(int bucket) => bucket switch
    {
        Quick => "Quick / קצר",
        Medium => "Medium / בינוני",
        Long => "Long / ארוך",
        _ => "Medium / בינוני",
    };
}
