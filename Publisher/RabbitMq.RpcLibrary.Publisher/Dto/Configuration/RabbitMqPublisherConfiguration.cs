namespace RabbitMq.RpcLibrary.Publisher.Dto.Configuration;
public class RabbitMqPublisherConfiguration
{
	public List<string> RabbitMqConnections { get; set; } = new();
	public string UserName { get; set; } = "guest";
	public string Password { get; set; } = "guest";
	public string ConnectionName { get; set; } = "RabbitMqPublisher";
	public string PublishQueueName { get; set; } = "RabbitMqPublishQueue";
	public string ReplyQueueName { get; set; } = "RabbitMqReplyQueue";
	public TimeSpan TimeoutDuration { get; set; } = TimeSpan.FromMinutes(5);
	public int MaxRetryCount { get; set; } = 3;
}