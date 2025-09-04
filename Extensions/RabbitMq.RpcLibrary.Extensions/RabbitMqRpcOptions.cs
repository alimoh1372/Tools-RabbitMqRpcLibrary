namespace RabbitMq.RpcLibrary.Extensions;

public class RabbitMqRpcOptions
{
	private readonly IServiceCollection _services;
	private RabbitMqPublisherConfiguration _publisherConfig = new();
	private RabbitMqConsumerConfiguration _consumerConfig = new();
	private bool _storeConfigured = false;

	public RabbitMqRpcOptions(IServiceCollection services)
	{
		_services = services;
	}

	public RabbitMqRpcOptions ConfigRabbitMqPublisher(Action<RabbitMqPublisherConfiguration> configure)
	{
		configure(_publisherConfig);
		_

		System: services.AddSingleton(_publisherConfig);
		return this;
	}

	public RabbitMqRpcOptions ConfigRabbitMqConsumer(Action<RabbitMqConsumerConfiguration> configure)
	{
		configure(_consumerConfig);
		_services.AddSingleton(_consumerConfig);
		return this;
	}

	public RabbitMqRpcOptions AddMessageStore(Action<MessageStoreOptions> configure)
	{
		var options = new MessageStoreOptions(_services);
		configure(options);
		_storeConfigured = true;
		return this;
	}

	public void Build()
	{
		if (!_storeConfigured)
		{
			_services.AddSingleton<IMessageStore, InMemoryMessageStore>();
		}
		_services.AddSingleton<PublisherService>();
		_services.AddSingleton<RmqTypedConsumer>();
	}
}