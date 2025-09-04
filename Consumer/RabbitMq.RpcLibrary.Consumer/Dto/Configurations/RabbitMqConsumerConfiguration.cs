namespace RabbitMq.RpcLibrary.Consumer.Dto.Configurations;

public class RabbitMqConsumerConfiguration
{
	public List<string> RabbitMqConnections { get; set; } = new();
	public string QueueName { get; set; } = "RabbitMqPublishQueue"; // Same as publish for RPC
	public string UserName { get; set; } = "guest";
	public string Password { get; set; } = "guest";
	public string ConnectionName { get; set; } = "RabbitMqConsumer";
	public ushort PrefetchCount { get; set; } = 10;
}