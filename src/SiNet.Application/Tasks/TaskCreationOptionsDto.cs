namespace SiNet.Application.Tasks;

public sealed record TaskLookupItemDto(int Id, string DisplayName, int? DefaultWorkQueueBucket = null);

public sealed record TaskCreationOptionsDto(
    IReadOnlyList<TaskLookupItemDto> Projects,
    IReadOnlyList<TaskLookupItemDto> Users,
    IReadOnlyList<TaskLookupItemDto> TaskTypes,
    IReadOnlyList<TaskLookupItemDto> Statuses,
    IReadOnlyList<TaskLookupItemDto> Buckets);
