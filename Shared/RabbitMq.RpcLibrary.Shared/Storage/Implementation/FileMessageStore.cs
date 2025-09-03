using RabbitMq.RpcLibrary.Shared.Models;
using Newtonsoft.Json;
using Formatting = System.Xml.Formatting;

namespace RabbitMq.RpcLibrary.Shared.Storage.Implementation;

public class FileMessageStore : IMessageStore
{
	private readonly string _filePath;
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	public FileMessageStore(string filePath)
	{
		_filePath = filePath;
		Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? "");
	}

	public async Task SaveAsync(MessageEnvelope message, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			messages[message.Id] = message;
			await SaveAllMessages(messages);
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task UpdateStatusAsync(Guid id, MessageStatus status, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			if (messages.TryGetValue(id, out var message))
			{
				message.Status = status;
				await SaveAllMessages(messages);
			}
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task UpdateStatusAsync(Guid id, MessageStatus status, string? errorMessage, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			if (messages.TryGetValue(id, out var message))
			{
				message.Status = status;
				message.ErrorMessage = errorMessage;
				await SaveAllMessages(messages);
			}
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task UpdateResponseAsync(Guid id, string responsePayload, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			if (messages.TryGetValue(id, out var message))
			{
				message.ResponsePayload = responsePayload;
				await SaveAllMessages(messages);
			}
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task<MessageEnvelope?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		return messages.TryGetValue(id, out var message) ? message : null;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetPendingAsync(CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		return messages.Values.Where(m => m.Status == MessageStatus.ResponsePending);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetFailedAsync(CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		return messages.Values.Where(m => m.Status == MessageStatus.Failed);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetRetryableAsync(CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		var cutoff = DateTime.UtcNow.AddMinutes(-5);
		return messages.Values.Where(m =>
			(m.Status == MessageStatus.Failed || m.Status == MessageStatus.TryingToPublish) &&
			m.RetryCount < 3 &&
			(m.LastRetryAt == null || m.LastRetryAt < cutoff));
	}

	public async Task IncrementRetryCountAsync(Guid id, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			if (messages.TryGetValue(id, out var message))
			{
				message.RetryCount++;
				message.LastRetryAt = DateTime.UtcNow;
				await SaveAllMessages(messages);
			}
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task CleanupOldMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		await _semaphore.WaitAsync(ct);
		try
		{
			var messages = await LoadAllMessages();
			var cutoff = DateTime.UtcNow.Subtract(maxAge);
			var toRemove = messages.Values.Where(m => m.CreatedAt < cutoff &&
				(m.Status == MessageStatus.Completed || m.Status == MessageStatus.Failed || m.Status == MessageStatus.TimedOut)).ToList();
			foreach (var msg in toRemove)
				messages.Remove(msg.Id);
			await SaveAllMessages(messages);
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public async Task<Dictionary<MessageStatus, int>> GetMessageCountsByStatusAsync(CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		var counts = messages.Values.GroupBy(m => m.Status).ToDictionary(g => g.Key, g => g.Count());
		return counts;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetOldPendingMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var messages = await LoadAllMessages();
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		return messages.Values.Where(m => m.Status == MessageStatus.ResponsePending && m.CreatedAt < cutoff);
	}

	private async Task<Dictionary<Guid, MessageEnvelope>> LoadAllMessages()
	{
		if (!File.Exists(_filePath))
			return new Dictionary<Guid, MessageEnvelope>();

		var json = await File.ReadAllTextAsync(_filePath);
		var messages = JsonConvert.DeserializeObject<Dictionary<Guid, MessageEnvelope>>(json);
		return messages ?? new Dictionary<Guid, MessageEnvelope>();
	}

	private async Task SaveAllMessages(Dictionary<Guid, MessageEnvelope> messages)
	{
		var json = JsonConvert.SerializeObject(messages,Newtonsoft.Json.Formatting.Indented);

		await File.WriteAllTextAsync(_filePath, json);
	}
}