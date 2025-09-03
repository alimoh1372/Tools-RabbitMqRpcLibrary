namespace RabbitMq.RpcLibrary.Publisher.Dto.Configuration;

public class RabbitMqPublisherConfiguration
{
	public const string SectionName = "RabbitMqPublisher";

	public List<string> RabbitMqConnections { get; set; } = new();
	public string UserName { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string ConnectionName { get; set; } = string.Empty;
	public string QueueName { get; set; } = string.Empty;
	public TimeSpan TimeoutDuration { get; set; } = TimeSpan.FromMinutes(5); // Default timeout for pending messages
}