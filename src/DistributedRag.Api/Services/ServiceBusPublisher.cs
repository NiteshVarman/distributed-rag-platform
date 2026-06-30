using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DistributedRag.Shared.Configuration;
using DistributedRag.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistributedRag.Api.Services;

/// <summary>
/// Publishes messages to Azure Service Bus queue.
/// Implements IAsyncDisposable to properly clean up the ServiceBusClient.
/// 
/// Registered as a singleton — ServiceBusClient is thread-safe and manages its own connection pool.
/// </summary>
public class ServiceBusPublisher : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusPublisher> _logger;

    public ServiceBusPublisher(IOptions<ServiceBusSettings> settings, ILogger<ServiceBusPublisher> logger)
    {
        _logger = logger;

        var sbSettings = settings.Value;

        if (string.IsNullOrWhiteSpace(sbSettings.ConnectionString))
            throw new InvalidOperationException(
                "Azure Service Bus connection string is not configured. " +
                "Set 'ServiceBus:ConnectionString' in appsettings.json or environment variables.");

        _client = new ServiceBusClient(sbSettings.ConnectionString);
        _sender = _client.CreateSender(sbSettings.QueueName);

        _logger.LogInformation("ServiceBusPublisher initialized — Queue: {Queue}", sbSettings.QueueName);
    }

    /// <summary>
    /// Publishes a ProcessUrlMessage to the Service Bus queue.
    /// This message will be consumed by the Azure Functions Service Bus trigger,
    /// which starts the Durable Orchestrator.
    /// </summary>
    public async Task PublishProcessUrlMessageAsync(ProcessUrlMessage message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var sbMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            MessageId = message.TaskId, // Use taskId as messageId for idempotency
            Subject = "process-url"
        };

        await _sender.SendMessageAsync(sbMessage);
        _logger.LogInformation("Published message to Service Bus — TaskId: {TaskId}, URL: {Url}",
            message.TaskId, message.Url);
    }

    /// <summary>
    /// Properly dispose the Service Bus client and sender.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
        _logger.LogInformation("ServiceBusPublisher disposed");
        GC.SuppressFinalize(this);
    }
}
