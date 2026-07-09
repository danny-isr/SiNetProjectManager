using SiNet.Application.WorkSurfaces;

namespace SiNet.Application.Email.Detail;

/// <summary>Input for standalone or task-driven Email Detail surfaces.</summary>
public sealed record EmailDetailInput(
    WorkSurfaceContext? TaskContext = null);
