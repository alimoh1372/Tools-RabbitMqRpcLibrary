using RabbitMq.RpcLibrary.Publisher.Dto.Configuration;
using System.Collections.Concurrent;
using System.Text;
using System.Timers;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMq.RpcLibrary.Publisher.Dto;
using RabbitMq.RpcLibrary.Shared.Models;
using RabbitMq.RpcLibrary.Shared.Storage;

namespace RabbitMq.RpcLibrary.Publisher.Publishers;

public class PublisherService : IDisposable
{
	private readonly RabbitMqPublisherConfiguration _config;
	private readonly IMessageStore _store;
	private IConnection? _connection;
	private IChannel? _channel;
	private IChannel? _replyChannel; // Separate channel for reply consumer
	private List<AmqpTcpEndpoint>? _endpoints;
	private ConnectionFactory? _factory;
	private readonly System.Timers.Timer _retryTimer;
	private readonly System.Timers.Timer _timeoutTimer; // Timer for checking timeouts
	private readonly ConcurrentDictionary<ulong, Guid> _pendingConfirms = new(); // For publisher confirms
	private bool _disposed = false;

	public PublisherService(RabbitMqPublisherConfiguration config, IMessageStore store)
	{
		_config = config;
		_store = store;
		_retryTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2).TotalMilliseconds);
		_retryTimer.Elapsed += OnRetryTimerElapsed;
		_retryTimer.AutoReset = true;

		_timeoutTimer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds); // Check every 1 min
		_timeoutTimer.Elapsed += OnTimeoutTimerElapsed;
		_timeoutTimer.AutoReset = true;
	}

	public async Task<(bool isSuccess, string errorMessage)> Initialize(CancellationToken ct = default)
	{
		try
		{
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

			_channel = await _connection.CreateChannelAsync(
				new CreateChannelOptions(
					publisherConfirmationsEnabled: true,
					publisherConfirmationTrackingEnabled: true
					),
				ct); // Explicit confirms

			_channel.BasicAcksAsync += async (sender, eventArgs) =>
			{
				await OnConfirmReceived(sender, eventArgs);
			};

			await _channel.BasicAckAsync+= async (OnConfirmReceived, ct); // Handle confirms

			await _channel.QueueDeclareAsync(_config.QueueName, true, false, false, null, ct);

			// Declare reply queue (persistent for reliability)
			_replyChannel = await _connection.CreateChannelAsync(ct);

			await _replyChannel.QueueDeclareAsync("response-queue", true, false, false, null, ct); // Assuming fixed reply queue

			// Setup consumer for reply queue
			var replyConsumer = new AsyncEventingBasicConsumer(_replyChannel);
			replyConsumer.ReceivedAsync += OnReplyReceived;
			await _replyChannel.BasicConsumeAsync("response-queue", false, replyConsumer, ct);

			_retryTimer.Start();
			_timeoutTimer.Start();

			return (true, "");
		}
		catch (Exception ex)
		{
			return (false, $"Publisher init failed: {ex.Message}");
		}
	}

	public async Task PublishAsync(MessageEnvelope envelope, CancellationToken ct = default)
	{
		try
		{
			await _store.SaveAsync(envelope, ct);
			await InternalPublishAsync(envelope, ct);
		}
		catch (Exception ex)
		{
			await _store.UpdateStatusAsync(envelope.Id, MessageStatus.Failed, ex.Message, ct);
			throw;
		}
	}

	private async Task InternalPublishAsync(MessageEnvelope envelope, CancellationToken ct = default)
	{
		var props = new BasicProperties
		{
			Persistent = true,
			CorrelationId = envelope.CorrelationId,
			Headers = new Dictionary<string, object>
			{
				["cap-msg-type"] = Encoding.UTF8.GetBytes(envelope.Type),
				["reply-queue"] = Encoding.UTF8.GetBytes(envelope.ReplyQueue)
			}
		};

		await _store.UpdateStatusAsync(envelope.Id, MessageStatus.Publishing, ct);

		var sequenceNumber = await _channel!.NextPublishSeqNoAsync(ct); // Get sequence for confirm
		_pendingConfirms[sequenceNumber] = envelope.Id; // Track pending

		await _channel.BasicPublishAsync("", _config.QueueName, envelope.Payload, props, true, ct);

		// No immediate status change; wait for confirm in OnConfirmReceived
	}

	private async Task OnConfirmReceived(object sender,BasicAckEventArgs eventArgs)
	{
		
		if (_pendingConfirms.TryRemove(eventArgs.DeliveryTag, out var messageId))
		{
				await _store.UpdateStatusAsync(messageId, MessageStatus.ResponsePending);
		//}
		//	else
		//	{
		//		_store.UpdateStatusAsync(messageId, MessageStatus.Failed, "Publish confirm nack");
		//	}
		}
	}

	private async Task OnReplyReceived(object? sender, BasicDeliverEventArgs ea)
	{
		try
		{
			var correlationId = ea.BasicProperties.CorrelationId;
			var message = await _store.GetAsync(Guid.Parse(correlationId)); // Assuming CorrelationId = Id.ToString()
			if (message == null)
			{
				await _replyChannel!.BasicNackAsync(ea.DeliveryTag, false, false);
				return;
			}

			var responseJson = Encoding.UTF8.GetString(ea.Body.ToArray());
			var response = JsonConvert.DeserializeObject<ResponseMessage>(responseJson);

			if (response.Status == "Received")
			{
				await _store.UpdateStatusAsync(message.Id, MessageStatus.Received);
			}
			else if (response.Status == "Success")
			{
				await _store.UpdateStatusAsync(message.Id, MessageStatus.Completed);
				await _store.UpdateResponseAsync(message.Id, response.Payload ?? "");
			}
			else if (response.Status == "Failure")
			{
				await _store.UpdateStatusAsync(message.Id, MessageStatus.Failed, response.ErrorMessage);
			}

			await _replyChannel.BasicAckAsync(ea.DeliveryTag, false);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error processing reply: {ex.Message}");
			await _replyChannel!.BasicNackAsync(ea.DeliveryTag, false, true); // Requeue
		}
	}

	private async void OnRetryTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		try
		{
			var retryableMessages = await _store.GetRetryableAsync();
			foreach (var message in retryableMessages)
			{
				try
				{
					await _store.IncrementRetryCountAsync(message.Id);
					await _store.UpdateStatusAsync(message.Id, MessageStatus.Retrying);
					await InternalPublishAsync(message);
				}
				catch (Exception ex)
				{
					if (message.RetryCount >= 3)
					{
						await _store.UpdateStatusAsync(message.Id, MessageStatus.Failed, $"Max retries exceeded: {ex.Message}");
					}
					else
					{
						await _store.UpdateStatusAsync(message.Id, MessageStatus.TryingToPublish, ex.Message);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Retry timer error: {ex.Message}");
		}
	}

	private async void OnTimeoutTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		try
		{
			var oldPending = await _store.GetOldPendingMessagesAsync(_config.TimeoutDuration);
			foreach (var message in oldPending)
			{
				await _store.UpdateStatusAsync(message.Id, MessageStatus.TimedOut, "Response timeout");
				Console.WriteLine($"Warning: Message {message.Id} timed out. Notify user.");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Timeout timer error: {ex.Message}");
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_retryTimer?.Stop();
			_retryTimer?.Dispose();
			_timeoutTimer?.Stop();
			_timeoutTimer?.Dispose();
			_channel?.Dispose();
			_replyChannel?.Dispose();
			_connection?.Dispose();
			_disposed = true;
		}
	}
}