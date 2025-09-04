// RabbitMqConsumerLibrary/RmqTypedConsumerService.cs
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMqConsumerLibrary.Attributes;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text;
using RabbitMq.RpcLibrary.Consumer.Dto.Attributes;
using RabbitMq.RpcLibrary.Consumer.Dto.Configurations;
using RabbitMq.RpcLibrary.Consumer.Interfaces;

namespace RabbitMq.RpcLibrary.Consumer.Services;

public class RmqTypedConsumerService : IDisposable
{
	private readonly IServiceProvider _serviceProvider;
	private readonly RabbitMqConsumerConfiguration _config;
	private readonly ILogger<RmqTypedConsumerService> _logger;
	private readonly IMessageStore _store;
	private readonly ConcurrentDictionary<string, (MethodInfo Method, Type ParameterType, object Instance)> _receivers = new();
	private IConnection? _connection;
	private IChannel? _channel;
	private List<AmqpTcpEndpoint>? _endpoints;
	private ConnectionFactory? _factory;
	private AsyncEventingBasicConsumer? _consumer;
	private bool _disposed = false;

	public event Action<int>? OnReceived;

	public RmqTypedConsumerService(IServiceProvider serviceProvider, RabbitMqConsumerConfiguration config, IMessageStore store, ILogger<RmqTypedConsumerService> logger)
	{
		_serviceProvider = serviceProvider;
		_config = config;
		_store = store;
		_logger = logger;
		OnReceived += count => _logger.LogInformation($"Received {count} messages.");
	}

	public async Task<(bool isSuccess, string errorMessage)> Initialize(CancellationToken ct = default)
	{
		try
		{
			RegisterReceivers();

			_endpoints = _config.RabbitMqConnections.Select(x =>
			{
				var parts = x.Split(":");
				return new AmqpTcpEndpoint(parts[0], int.Parse(parts[1]));
			}).ToList();

			_factory = new ConnectionFactory
			{
				UserName = _config.UserName,
				Password = _config.Password,
				ClientProvidedName = _config.ConnectionName
			};

			_connection = await _factory.CreateConnectionAsync(_endpoints, ct);
			_channel = await _connection.CreateChannelAsync(new CreateChannelOptions(consumerDispatchConcurrency: 200), ct);

			await _channel.QueueDeclareAsync(_config.QueueName, true, false, false, null, ct);

			await _channel.BasicQosAsync(0, _config.PrefetchCount, false, ct);

			// Load pending messages
			var pendingMessages = await _store.LoadPendingMessagesAsync(ct);
			foreach (var msg in pendingMessages)
				await _store.SaveAsync(msg, ct); // Ensure in-memory for consistency

			return (true, "");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Consumer init failed");
			return (false, ex.Message);
		}
	}

	public async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var consumingTask = StartConsumingAsync(stoppingToken);
		var monitorTask = MonitorConnectionAsync(stoppingToken);

		await Task.WhenAll(consumingTask, monitorTask);
	}

	private void RegisterReceivers()
	{
		var assemblies = AppDomain.CurrentDomain.GetAssemblies();
		var types = assemblies.SelectMany(a => a.GetTypes())
			.Where(t => typeof(IRabbitMqReceiver).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
			.ToList();

		foreach (var type in types)
		{
			var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type);

			var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
				.Where(m => m.GetCustomAttribute<RabbitMqReceiverAttribute>() != null)
				.ToList();

			foreach (var method in methods)
			{
				var attr = method.GetCustomAttribute<RabbitMqReceiverAttribute>()!;
				var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;
				if (paramType == null) continue;

				_receivers[attr.ReceiverType] = (method, paramType, instance);
			}
		}
	}

	private async Task StartConsumingAsync(CancellationToken stoppingToken)
	{
		_consumer = new AsyncEventingBasicConsumer(_channel!);

		_consumer.ReceivedAsync += async (sender, ea) =>
		{
			int count = 0;
			try
			{
				var body = ea.Body.ToArray();
				var jsonString = Encoding.UTF8.GetString(body);
				var headers = ea.BasicProperties.Headers;

				if (headers == null || !headers.TryGetValue("cap-msg-type", out var messageTypeObj))
				{
					_logger.LogWarning("Missing cap-msg-type header");
					await _channel!.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
					return;
				}

				var messageType = Encoding.UTF8.GetString((byte[])messageTypeObj);

				if (!_receivers.TryGetValue(messageType, out var receiverInfo))
				{
					_logger.LogWarning("No receiver found for type {MessageType}", messageType);
					await _channel!.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
					return;
				}

				var entity = JsonConvert.DeserializeObject(jsonString, receiverInfo.ParameterType);

				if (receiverInfo.Method.Invoke(receiverInfo.Instance, new[] { entity }) is Task task)
					await task;

				await _channel!.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);

				count++;
				OnReceived?.Invoke(count);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Message processing failed");
				await _channel!.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
			}
		};

		await _channel!.BasicConsumeAsync(_config.QueueName, false, _consumer, stoppingToken);
	}

	private async Task MonitorConnectionAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (_connection == null || !_connection.IsOpen)
				{
					_logger.LogWarning("Connection closed, reconnecting...");
					_connection = await _factory!.CreateConnectionAsync(_endpoints, ct);
				}

				if (_channel == null || !_channel.IsOpen)
				{
					_logger.LogWarning("Channel closed, reconnecting...");
					_channel = await _connection.CreateChannelAsync(new CreateChannelOptions(consumerDispatchConcurrency: 200), ct);
					await _channel.BasicQosAsync(0, _config.PrefetchCount, false, ct);
					await StartConsumingAsync(ct);
				}

				await Task.Delay(60000, ct); // Check every 60s
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Reconnection failed");
				await Task.Delay(5000, ct); // Retry after 5s
			}
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_channel?.Dispose();
		_connection?.Dispose();
		_disposed = true;
	}
}