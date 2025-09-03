namespace RabbitMq.RpcLibrary.Consumer.Dto.Configurations;

public class RabbitMqConsumerConfiguration
{
	public const string SectionName = "RabbitMqConsumer";

	public List<string> RabbitMqConnections { get; set; } = new();
	public string UserName { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string ConnectionName { get; set; } = string.Empty;
	public string QueueName { get; set; } = string.Empty;
}