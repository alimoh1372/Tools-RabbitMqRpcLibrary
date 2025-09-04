namespace RabbitMq.RpcLibrary.Shared.Models;
public class MessageEnvelope
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Type { get; set; } = string.Empty;
	public string CorrelationId { get; set; } = string.Empty;
	public string ReplyQueue { get; set; } = string.Empty;
	public byte[] Payload { get; set; } = Array.Empty<byte>();
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public string Status { get; set; } = "TryingToPublish";
	public int RetryCount { get; set; } = 0;
	public DateTime? LastRetryAt { get; set; }
	public string? ErrorMessage { get; set; }
}