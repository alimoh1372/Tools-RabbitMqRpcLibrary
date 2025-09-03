using RabbitMq.RpcLibrary.Shared.Models;

namespace RabbitMq.RpcLibrary.Shared.Storage;

public interface IMessageStore
{
	Task SaveAsync(MessageEnvelope message, CancellationToken ct = default);
	Task UpdateStatusAsync(Guid id, MessageStatus status, CancellationToken ct = default);
	Task UpdateStatusAsync(Guid id, MessageStatus status, string? errorMessage, CancellationToken ct = default);
	Task UpdateResponseAsync(Guid id, string responsePayload, CancellationToken ct = default);
	Task<MessageEnvelope?> GetAsync(Guid id, CancellationToken ct = default);
	Task<IEnumerable<MessageEnvelope>> GetPendingAsync(CancellationToken ct = default);
	Task<IEnumerable<MessageEnvelope>> GetFailedAsync(CancellationToken ct = default);
	Task<IEnumerable<MessageEnvelope>> GetRetryableAsync(CancellationToken ct = default);
	Task IncrementRetryCountAsync(Guid id, CancellationToken ct = default);
	Task CleanupOldMessagesAsync(TimeSpan maxAge, CancellationToken ct = default);
	Task<Dictionary<MessageStatus, int>> GetMessageCountsByStatusAsync(CancellationToken ct = default);
	Task<IEnumerable<MessageEnvelope>> GetOldPendingMessagesAsync(TimeSpan maxAge, CancellationToken ct = default);
}