using RabbitMq.RpcLibrary.Shared.Models;

namespace RabbitMq.RpcLibrary.Shared.Storage.Implementation;

public class RedisStackStorage : IMessageStore
{
	private readonly IConnectionMultiplexer _redis;
	private readonly string _prefix = "rabbitmqrpc:msg:";

	public RedisStackStorage(string connectionString)
	{
		_redis = ConnectionMultiplexer.Connect(connectionString);
	}

	public async Task SaveAsync(MessageEnvelope message, CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var hashEntries = new[]
		{
			new HashEntry("Type", message.Type),
			new HashEntry("CorrelationId", message.CorrelationId),
			new HashEntry("ReplyQueue", message.ReplyQueue),
			new HashEntry("Payload", message.Payload),
			new HashEntry("CreatedAt", message.CreatedAt.ToString("o")),
			new HashEntry("Status", message.Status),
			new HashEntry("RetryCount", message.RetryCount),
			new HashEntry("LastRetryAt", message.LastRetryAt?.ToString("o") ?? (RedisValue)DBNull.Value),
			new HashEntry("ErrorMessage", message.ErrorMessage ?? (RedisValue)DBNull.Value)
		};
		await db.HashSetAsync($"{_prefix}{message.Id}", hashEntries);
	}

	public async Task UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		await db.HashSetAsync($"{_prefix}{id}", "Status", status);
	}

	public async Task UpdateStatusAsync(Guid id, string status, string? errorMessage, CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var hashEntries = new[]
		{
			new HashEntry("Status", status),
			new HashEntry("ErrorMessage", errorMessage ?? (RedisValue)DBNull.Value)
		};
		await db.HashSetAsync($"{_prefix}{id}", hashEntries);
	}

	public async Task<MessageEnvelope?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var entries = await db.HashGetAllAsync($"{_prefix}{id}");
		if (entries.Length == 0) return null;
		return ReadMessage(entries);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetPendingAsync(CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		var messages = new List<MessageEnvelope>();
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if (msg.Status == "ResponsePending")
				messages.Add(msg);
		}
		return messages;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetFailedAsync(CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		var messages = new List<MessageEnvelope>();
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if (msg.Status == "Failed")
				messages.Add(msg);
		}
		return messages;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetRetryableAsync(CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.AddMinutes(-5);
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		var messages = new List<MessageEnvelope>();
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if ((msg.Status == "Failed" || msg.Status == "TryingToPublish") &&
				msg.RetryCount < 3 &&
				(msg.LastRetryAt == null || msg.LastRetryAt < cutoff))
			{
				messages.Add(msg);
			}
		}
		return messages;
	}

	public async Task IncrementRetryCountAsync(Guid id, CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var key = $"{_prefix}{id}";
		await db.HashIncrementAsync(key, "RetryCount", 1);
		await db.HashSetAsync(key, "LastRetryAt", DateTime.UtcNow.ToString("o"));
	}

	public async Task CleanupOldMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if (msg.CreatedAt < cutoff && (msg.Status == "Completed" || msg.Status == "Failed" || msg.Status == "TimedOut"))
				await db.KeyDeleteAsync(key);
		}
	}

	public async Task<Dictionary<string, int>> GetMessageCountsByStatusAsync(CancellationToken ct = default)
	{
		var counts = new Dictionary<string, int>();
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			counts[msg.Status] = counts.GetValueOrDefault(msg.Status, 0) + 1;
		}
		return counts;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetOldPendingMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		var messages = new List<MessageEnvelope>();
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if (msg.Status == "ResponsePending" && msg.CreatedAt < cutoff)
				messages.Add(msg);
		}
		return messages;
	}

	public async Task<IEnumerable<MessageEnvelope>> LoadPendingMessagesAsync(CancellationToken ct = default)
	{
		var db = _redis.GetDatabase();
		var keys = _redis.GetServer(_redis.GetEndPoints().First()).Keys(pattern: $"{_prefix}*");
		var messages = new List<MessageEnvelope>();
		foreach (var key in keys)
		{
			var entries = await db.HashGetAllAsync(key);
			var msg = ReadMessage(entries);
			if (msg.Status != "Completed" && msg.Status != "TimedOut")
				messages.Add(msg);
		}
		return messages;
	}

	private MessageEnvelope ReadMessage(HashEntry[] entries)
	{
		var dict = entries.ToDictionary(e => e.Name.ToString(), e => e.Value);
		return new MessageEnvelope
		{
			Id = Guid.Parse(dict["Id"].ToString()),
			Type = dict["Type"].ToString(),
			CorrelationId = dict["CorrelationId"].ToString(),
			ReplyQueue = dict["ReplyQueue"].ToString(),
			Payload = (byte[])dict["Payload"],
			CreatedAt = DateTime.Parse(dict["CreatedAt"].ToString()),
			Status = dict["Status"].ToString(),
			RetryCount = (int)dict["RetryCount"],
			LastRetryAt = dict["LastRetryAt"].HasValue ? DateTime.Parse(dict["LastRetryAt"].ToString()) : null,
			ErrorMessage = dict["ErrorMessage"].HasValue ? dict["ErrorMessage"].ToString() : null
		};
	}
}