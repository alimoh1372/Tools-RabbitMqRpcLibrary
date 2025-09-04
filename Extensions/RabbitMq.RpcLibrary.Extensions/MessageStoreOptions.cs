namespace RabbitMq.RpcLibrary.Extensions;

public class MessageStoreOptions
{
	private readonly IServiceCollection _services;

	public MessageStoreOptions(IServiceCollection services)
	{
		_services = services;
	}

	public MessageStoreOptions UseInMemory()
	{
		_services.AddSingleton<IMessageStore, InMemoryMessageStore>();
		return this;
	}

	public MessageStoreOptions UseSql(string connectionString)
	{
		_services.AddDbContext<MessageDbContext>(options => options.UseSqlite(connectionString));
		_services.AddSingleton<IMessageStore, SqlMessageStore>();
		return this;
	}

	public MessageStoreOptions UseRedis(string connectionString)
	{
		_services.AddSingleton<IMessageStore>(sp => new RedisStackStorage(connectionString));
		return this;
	}
}