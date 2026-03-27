namespace MasterPlan.SyncEngine;

/// <summary>
/// Custom exception for MasterPlan API errors
/// </summary>
public class MasterPlanApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseContent { get; }

    public MasterPlanApiException(string message) 
        : base(message)
    {
        StatusCode = 0;
    }

    public MasterPlanApiException(string message, int statusCode) 
        : base(message)
    {
        StatusCode = statusCode;
    }

    public MasterPlanApiException(string message, int statusCode, string? responseContent) 
        : base(message)
    {
        StatusCode = statusCode;
        ResponseContent = responseContent;
    }

    public MasterPlanApiException(string message, Exception innerException) 
        : base(message, innerException)
    {
        StatusCode = 0;
    }

    public MasterPlanApiException(string message, int statusCode, Exception innerException) 
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
