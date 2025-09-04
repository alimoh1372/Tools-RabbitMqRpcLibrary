namespace RabbitMq.RpcLibrary.Extensions;

public class DependencyInjection
{
	public static IServiceCollection AddRabbitMqRpc(this IServiceCollection services, IConfiguration configuration)
	{
		services.Configure<RabbitMqPublisherConfiguration>(configuration.GetSection("RabbitMqPublisher"));
		services.Configure<RabbitMqConsumerConfiguration>(configuration.GetSection("RabbitMqConsumer"));

		var storageType = configuration.GetValue<string>("MessageStore:Type", "InMemory");
		var connectionString = configuration.GetValue<string>("MessageStore:ConnectionString", "Data Source=messages.db");

		switch (storageType)
		{
			case "Sql":
				services.AddDbContext<MessageDbContext>(options => options.UseSqlite(connectionString));
				services.AddSingleton<IMessageStore, SqlMessageStore>();
				break;
			case "Redis":
				services.AddSingleton<IMessageStore>(sp => new RedisStackStorage(connectionString));
				break;
			default:
				services.AddSingleton<IMessageStore, InMemoryMessageStore>();
				break;
		}

		services.AddSingleton<PublisherService>();
		services.AddSingleton<RmqTypedConsumerService>();

		return services;
	}

	public static IServiceCollection AddRabbitMqRpc(this IServiceCollection services, Action<RabbitMqRpcOptions> configure)
	{
		var options = new RabbitMqRpcOptions(services);
		configure(options);
		options.Build();
		return services;
	}





	//Configuration file

	/*
	{
		"RabbitMqPublisher": {
			"RabbitMqConnections": ["localhost:5672"],
			"UserName": "guest",
			"Password": "guest",
			"PublishQueueName": "PublishQueue",
			"ReplyQueueName": "ReplyQueue",
			"TimeoutDuration": "00:10:00",
			"MaxRetryCount": 5
		},
		"RabbitMqConsumer": {
			"RabbitMqConnections": ["localhost:5672"],
			"QueueName": "PublishQueue",
			"UserName": "guest",
			"Password": "guest",
			"PrefetchCount": 20
		},
		"MessageStore": {
			"Type": "Sql",
			"ConnectionString": "Data Source=messages.db"
		}
	}
	*/
}
