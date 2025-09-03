using RabbitMq.RpcLibrary.Shared.Models;

namespace RabbitMq.RpcLibrary.Shared.DbContextes.Models;

public class MessageEnvelopeEntity
{
	public Guid Id { get; set; }
	public string Type { get; set; } = string.Empty;
	public string CorrelationId { get; set; } = string.Empty;
	public string ReplyQueue { get; set; } = string.Empty;
	public byte[] Payload { get; set; } = Array.Empty<byte>();
	public DateTime CreatedAt { get; set; }
	public MessageStatus Status { get; set; }
	public int RetryCount { get; set; }
	public DateTime? LastRetryAt { get; set; }
	public string? ErrorMessage { get; set; }
	public string? ResponsePayload { get; set; }
}