namespace DistributedRag.Shared.Models;

/// <summary>
/// Message payload sent to Azure Service Bus to trigger URL processing.
/// Published by the API, consumed by the Azure Functions Service Bus trigger.
/// </summary>
public class ProcessUrlMessage
{
    /// <summary>
    /// The task ID to track this processing job.
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// The URL to scrape and process.
    /// </summary>
    public string Url { get; set; } = string.Empty;
}
