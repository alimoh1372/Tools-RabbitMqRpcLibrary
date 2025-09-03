namespace RabbitMq.RpcLibrary.Shared.Models;

public class MessageEnvelope
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Type { get; set; } = string.Empty;
	public string CorrelationId { get; set; } = string.Empty;
	public string ReplyQueue { get; set; } = string.Empty;
	public byte[] Payload { get; set; } = Array.Empty<byte>();
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public MessageStatus Status { get; set; } = MessageStatus.TryingToPublish;
	public int RetryCount { get; set; } = 0;
	public DateTime? LastRetryAt { get; set; }
	public string? ErrorMessage { get; set; }
	public string? ResponsePayload { get; set; } = null; // For storing response data if any
}