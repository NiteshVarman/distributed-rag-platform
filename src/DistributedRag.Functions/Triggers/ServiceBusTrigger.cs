using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DistributedRag.Shared.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace DistributedRag.Functions.Triggers;

/// <summary>
/// Azure Service Bus trigger — listens for messages on the 'process-url-queue'.
/// When a message arrives, it deserializes the ProcessUrlMessage and starts
/// a new Durable Functions orchestrator instance.
///
/// Message settlement is MANUAL (AutoCompleteMessages = false):
///   - Unparseable / invalid messages are explicitly DEAD-LETTERED (poison messages
///     don't loop forever and aren't silently dropped).
///   - A message is only COMPLETED once the orchestration has been scheduled.
///   - Redelivery is idempotent: if an orchestration already exists for the taskId,
///     the message is completed without starting a duplicate.
///
/// This is the entry point that bridges the API → Service Bus → Durable Functions pipeline.
/// </summary>
public class ServiceBusTrigger
{
    private readonly ILogger<ServiceBusTrigger> _logger;

    public ServiceBusTrigger(ILogger<ServiceBusTrigger> logger)
    {
        _logger = logger;
    }

    [Function("ServiceBusTrigger")]
    public async Task Run(
        [ServiceBusTrigger("process-url-queue", Connection = "ServiceBusConnection", AutoCompleteMessages = false)]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        [DurableClient] DurableTaskClient durableClient)
    {
        var messageBody = message.Body.ToString();
        _logger.LogInformation("Service Bus message received: {MessageBody}", messageBody);

        // ─── Deserialize ───
        ProcessUrlMessage? processMessage;
        try
        {
            processMessage = JsonSerializer.Deserialize<ProcessUrlMessage>(messageBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize Service Bus message — dead-lettering: {Body}", messageBody);
            await messageActions.DeadLetterMessageAsync(message,
                deadLetterReason: "DeserializationError",
                deadLetterErrorDescription: ex.Message);
            return;
        }

        if (processMessage is null ||
            string.IsNullOrWhiteSpace(processMessage.TaskId) ||
            string.IsNullOrWhiteSpace(processMessage.Url))
        {
            _logger.LogError("Invalid message received — missing TaskId or Url. Dead-lettering.");
            await messageActions.DeadLetterMessageAsync(message,
                deadLetterReason: "InvalidMessage",
                deadLetterErrorDescription: "Message is missing TaskId or Url.");
            return;
        }

        // ─── Idempotency: don't start a duplicate orchestration on redelivery ───
        var existing = await durableClient.GetInstanceAsync(processMessage.TaskId);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Orchestration already exists for TaskId {TaskId} (status {Status}) — completing message without restart.",
                processMessage.TaskId, existing.RuntimeStatus);
            await messageActions.CompleteMessageAsync(message);
            return;
        }

        // ─── Start the Durable Orchestrator (taskId == instanceId for correlation) ───
        try
        {
            var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
                "IngestionOrchestrator",
                processMessage,
                new StartOrchestrationOptions(processMessage.TaskId));

            _logger.LogInformation(
                "Started orchestrator — InstanceId: {InstanceId}, TaskId: {TaskId}, URL: {Url}",
                instanceId, processMessage.TaskId, processMessage.Url);

            // Only settle the message once the orchestration is durably scheduled.
            await messageActions.CompleteMessageAsync(message);
        }
        catch (Exception ex)
        {
            // Abandon so the message is retried (and eventually dead-lettered after max delivery count).
            _logger.LogError(ex, "Failed to schedule orchestration for TaskId {TaskId} — abandoning for retry.",
                processMessage.TaskId);
            await messageActions.AbandonMessageAsync(message);
        }
    }
}
