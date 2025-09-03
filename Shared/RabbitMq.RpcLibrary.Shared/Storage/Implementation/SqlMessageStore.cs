using RabbitMq.RpcLibrary.Shared.DbContext;
using RabbitMq.RpcLibrary.Shared.DbContextes;
using RabbitMq.RpcLibrary.Shared.DbContextes.Models;
using RabbitMq.RpcLibrary.Shared.Models;

namespace RabbitMq.RpcLibrary.Shared.Storage.Implementation;

public class SqlMessageStore : IMessageStore, IDisposable
{
	private readonly MessageStoreDbContext _context;
	private bool _disposed = false;

	public SqlMessageStore(MessageStoreDbContext context)
	{
		_context = context;
	}

	public async Task SaveAsync(MessageEnvelope message, CancellationToken ct = default)
	{
		var entity = ToEntity(message);
		await _context.MessageEnvelopes.AddAsync(entity, ct);
		await _context.SaveChangesAsync(ct);
	}

	public async Task UpdateStatusAsync(Guid id, MessageStatus status, CancellationToken ct = default)
	{
		await _context.MessageEnvelopes
			.Where(m => m.Id == id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Status, status), ct);
	}

	public async Task UpdateStatusAsync(Guid id, MessageStatus status, string? errorMessage, CancellationToken ct = default)
	{
		await _context.MessageEnvelopes
			.Where(m => m.Id == id)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(m => m.Status, status)
				.SetProperty(m => m.ErrorMessage, errorMessage), ct);
	}

	public async Task UpdateResponseAsync(Guid id, string responsePayload, CancellationToken ct = default)
	{
		await _context.MessageEnvelopes
			.Where(m => m.Id == id)
			.ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ResponsePayload, responsePayload), ct);
	}

	public async Task<MessageEnvelope?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var entity = await _context.MessageEnvelopes.FirstOrDefaultAsync(m => m.Id == id, ct);
		return entity != null ? ToModel(entity) : null;
	}

	public async Task<IEnumerable<MessageEnvelope>> GetPendingAsync(CancellationToken ct = default)
	{
		var entities = await _context.MessageEnvelopes.Where(m => m.Status == MessageStatus.ResponsePending).ToListAsync(ct);
		return entities.Select(ToModel);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetFailedAsync(CancellationToken ct = default)
	{
		var entities = await _context.MessageEnvelopes.Where(m => m.Status == MessageStatus.Failed).ToListAsync(ct);
		return entities.Select(ToModel);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetRetryableAsync(CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.AddMinutes(-5);
		var entities = await _context.MessageEnvelopes
			.Where(m => (m.Status == MessageStatus.Failed || m.Status == MessageStatus.TryingToPublish) &&
						m.RetryCount < 3 &&
						(m.LastRetryAt == null || m.LastRetryAt < cutoff))
			.ToListAsync(ct);
		return entities.Select(ToModel);
	}

	public async Task IncrementRetryCountAsync(Guid id, CancellationToken ct = default)
	{
		var now = DateTime.UtcNow;
		await _context.MessageEnvelopes
			.Where(m => m.Id == id)
			.ExecuteUpdateAsync(setters => setters
				.SetProperty(m => m.RetryCount, m => m.RetryCount + 1)
				.SetProperty(m => m.LastRetryAt, now), ct);
	}

	public async Task CleanupOldMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		await _context.MessageEnvelopes
			.Where(m => m.CreatedAt < cutoff &&
						(m.Status == MessageStatus.Completed || m.Status == MessageStatus.Failed || m.Status == MessageStatus.TimedOut))
			.ExecuteDeleteAsync(ct);
	}

	public async Task<Dictionary<MessageStatus, int>> GetMessageCountsByStatusAsync(CancellationToken ct = default)
	{
		return await _context.MessageEnvelopes
			.GroupBy(m => m.Status)
			.Select(g => new { Status = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.Status, x => x.Count, ct);
	}

	public async Task<IEnumerable<MessageEnvelope>> GetOldPendingMessagesAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var cutoff = DateTime.UtcNow.Subtract(maxAge);
		var entities = await _context.MessageEnvelopes
			.Where(m => m.Status == MessageStatus.ResponsePending && m.CreatedAt < cutoff)
			.ToListAsync(ct);
		return entities.Select(ToModel);
	}

	private static MessageEnvelopeEntity ToEntity(MessageEnvelope model)
	{
		return new MessageEnvelopeEntity
		{
			Id = model.Id,
			Type = model.Type,
			CorrelationId = model.CorrelationId,
			ReplyQueue = model.ReplyQueue,
			Payload = model.Payload,
			CreatedAt = model.CreatedAt,
			Status = model.Status,
			RetryCount = model.RetryCount,
			LastRetryAt = model.LastRetryAt,
			ErrorMessage = model.ErrorMessage,
			ResponsePayload = model.ResponsePayload
		};
	}

	private static MessageEnvelope ToModel(MessageEnvelopeEntity entity)
	{
		return new MessageEnvelope
		{
			Id = entity.Id,
			Type = entity.Type,
			CorrelationId = entity.CorrelationId,
			ReplyQueue = entity.ReplyQueue,
			Payload = entity.Payload,
			CreatedAt = entity.CreatedAt,
			Status = entity.Status,
			RetryCount = entity.RetryCount,
			LastRetryAt = entity.LastRetryAt,
			ErrorMessage = entity.ErrorMessage,
			ResponsePayload = entity.ResponsePayload
		};
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_context?.Dispose();
			_disposed = true;
		}
	}
}