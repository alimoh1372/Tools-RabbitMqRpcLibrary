// RabbitMqConsumerLibrary/RmqTypedConsumer.cs
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

public class RmqTypedConsumer : IDisposable
{
	private readonly IServiceProvider _provider;
	private readonly RabbitMqConsumerConfiguration _config;
	private readonly ConcurrentDictionary<string, (MethodInfo Method, Type ParamType, Type ServiceType)> _receivers = new();
	private IConnection? _connection;
	private IChannel? _channel;
	private List<AmqpTcpEndpoint>? _endpoints;
	private ConnectionFactory? _factory;
	private bool _disposed = false;

	public RmqTypedConsumer(IServiceProvider provider, RabbitMqConsumerConfiguration config)
	{
		_provider = provider;
		_config = config;
	}

	public async Task<(bool ok, string err)> Initialize(CancellationToken ct)
	{
		try
		{
			RegisterReceivers();

			_endpoints = _config.RabbitMqConnections
				.Select(x => {
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
			_channel = await _connection.CreateChannelAsync(new CreateChannelOptions(), ct);
			await _channel.QueueDeclareAsync(_config.QueueName, true, false, false, null, ct);

			// Set prefetch for performance
			await _channel.BasicQosAsync(0, 10, false, ct);

			var consumer = new AsyncEventingBasicConsumer(_channel);
			consumer.ReceivedAsync += async (_, ea) =>
			{
				string? responseStatus = "Success";
				string? responsePayload = null;
				string? errorMessage = null;

				try
				{
					var typeHeader = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["cap-msg-type"]);
					var replyQueue = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["reply-queue"]);

					if (!_receivers.TryGetValue(typeHeader, out var receiver))
					{
						await _channel.BasicNackAsync(ea.DeliveryTag, false, false, ct);
						return;
					}

					// Send "Received" response immediately
					await SendResponse(ea.BasicProperties.CorrelationId, replyQueue, "Received", null, null, ct);

					using var scope = _provider.CreateScope();
					var instance = scope.ServiceProvider.GetRequiredService(receiver.ServiceType) as IRabbitMqReceiver;

					var json = Encoding.UTF8.GetString(ea.Body.ToArray());
					var obj = JsonConvert.DeserializeObject(json, receiver.ParamType);

					var task = receiver.Method.Invoke(instance, new[] { obj }) as Task;
					if (task != null) await task;

					await _channel.BasicAckAsync(ea.DeliveryTag, false, ct);

					// Send final response
					await SendResponse(ea.BasicProperties.CorrelationId, replyQueue, responseStatus, responsePayload, errorMessage, ct);
				}
				catch (Exception ex)
				{
					responseStatus = "Failure";
					errorMessage = ex.Message;
					Console.WriteLine($"Error processing message: {ex.Message}");
					await _channel.BasicNackAsync(ea.DeliveryTag, false, true, ct); // Requeue on error

					// Send failure response
					var replyQueueFromError = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["reply-queue"]);
					await SendResponse(ea.BasicProperties.CorrelationId, replyQueueFromError, responseStatus, responsePayload, errorMessage, ct);
				}
			};

			await _channel.BasicConsumeAsync(_config.QueueName, false, consumer, ct);
			return (true, "");
		}
		catch (Exception ex)
		{
			return (false, ex.Message);
		}
	}

	private async Task SendResponse(string correlationId, string replyQueue, string status, string? payload, string? errorMessage, CancellationToken ct)
	{
		var response = new ResponseMessage { Status = status, Payload = payload, ErrorMessage = errorMessage };
		var responseJson = JsonConvert.SerializeObject(response);
		var responseBytes = Encoding.UTF8.GetBytes(responseJson);

		var props = new BasicProperties
		{
			CorrelationId = correlationId,
			Persistent = true
		};

		await _channel!.BasicPublishAsync("", replyQueue, responseBytes, props, false, ct);
	}

	private void RegisterReceivers()
	{
		var types = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(x => x.GetTypes())
			.Where(t => typeof(IRabbitMqReceiver).IsAssignableFrom(t) && !t.IsAbstract);

		foreach (var serviceType in types)
		{
			var methods = serviceType.GetMethods()
				.Where(m => m.GetCustomAttributes<RabbitMqReceiverAttribute>().Any());

			foreach (var method in methods)
			{
				var attr = method.GetCustomAttribute<RabbitMqReceiverAttribute>()!;
				var paramType = method.GetParameters().FirstOrDefault()?.ParameterType;

				if (paramType != null)
				{
					_receivers[attr.ReceiverType] = (method, paramType, serviceType);
				}
			}
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_channel?.Dispose();
			_connection?.Dispose();
			_disposed = true;
		}
	}
}