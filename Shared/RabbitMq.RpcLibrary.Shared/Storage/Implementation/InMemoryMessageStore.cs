using RabbitMq.RpcLibrary.Shared.Models;
using System.Collections.Concurrent;

namespace RabbitMq.RpcLibrary.Shared.Storage.Implementation;

public class InMemoryMessageStore : IMessageStore
{
	private readonly ConcurrentDictionary<Guid, MessageEnvelope> _messages = new();

	public Task SaveAsync(MessageEnvelope message, CancellationToken ct = default)
	{
		_messages[message.Id] = message;
		return Task.CompletedTask;
	}

	public Task UpdateStatusAsync(Guid id, MessageStatus status, CancellationToken ct = default)
	{
		if (_messages.TryGetValue(id, out var msg))
			msg.Status = status;
		return Task.CompletedTask;
	}

	public Task UpdateStatusAsync(Guid id, MessageStatus status, string? errorMessage, CancellationToken ct = default)
	{
		if (_messages.TryGetValue(id, out var msg))
		{
			msg.Status = status;
			msg.ErrorMessage = errorMessage;
		}
		return Task.CompletedTask;
	}

	public Task UpdateResponseAsync(Guid id, string responsePayload, CancellationToken ct = default)
	{
		if (_messages.TryGetValue(id, out var msg))
			msg.ResponsePayload = responsePayload;
		return Task.CompletedTask;
	}

	public Task<MessageEnvelope?> GetAsync(Guid id, CancellationToken ct = default)
		=> Task.FromResult(_messages.TryGetValue(id, out var msg) ? msg : null);

	public Task<IEnumerable<MessageEnvelope>> GetPendingAsync(CancellationToken ct = default)
		=> Task.FromResult(_messages.Values.Where(m => m.Status == MessageStatus.ResponsePending).AsEnumerable());

	public Task<IEnumerable<MessageEnvelope>> GetFailedAsync(CancellationToken ct = default)
		=> Task.FromResult(_messages.Values.Where(m => m.Status == MessageStatus.Failed).AsEnumerable());

	public Task<IEnumerable<MessageEnvelope>> GetRetryableAsync(CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.AddMinutes(-5);
		return Task.FromResult(_messages.Values.Where(m =>
			(m.Status == MessageStatus.Failed || m.Status == MessageStatus.TryingToPublish) &&
			m.RetryCount < 3 &&
			(m.LastRetryAt == null || m.LastRetryAt < cutoff)).AsEnumerable());
	}

	public Task IncrementRetryCountAsync(Guid id, CancellationToken ct = default)
	{
		if (_messages.TryGetValue(id, out var msg))
		{
			msg.RetryCount++;
			msg.LastRetryAt = DateTime.UtcNow;
		}
		return Task.CompletedTask;
	}

	public Task CleanupOldMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		var toRemove = _messages.Values.Where(m => m.CreatedAt < cutoff &&
			(m.Status == MessageStatus.Completed || m.Status == MessageStatus.Failed || m.Status == MessageStatus.TimedOut)).ToList();
		foreach (var msg in toRemove)
			_messages.TryRemove(msg.Id, out _);
		return Task.CompletedTask;
	}

	public Task<Dictionary<MessageStatus, int>> GetMessageCountsByStatusAsync(CancellationToken ct = default)
	{
		var counts = _messages.Values.GroupBy(m => m.Status).ToDictionary(g => g.Key, g => g.Count());
		return Task.FromResult(counts);
	}

	public Task<IEnumerable<MessageEnvelope>> GetOldPendingMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		return Task.FromResult(_messages.Values.Where(m => m.Status == MessageStatus.ResponsePending && m.CreatedAt < cutoff).AsEnumerable());
	}
}