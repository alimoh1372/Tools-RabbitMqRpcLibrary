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
using Microsoft.Extensions.Logging;

namespace RabbitMq.RpcLibrary.Publisher.Publishers;

public class PublisherService : IDisposable
{
	private readonly RabbitMqPublisherConfiguration _config;
	private readonly IMessageStore _store;
	private readonly ILogger<PublisherService> _logger;
	private IConnection? _connection;
	private IChannel? _channel;
	private IChannel? _replyChannel;
	private List<AmqpTcpEndpoint>? _endpoints;
	private ConnectionFactory? _factory;
	private readonly System.Timers.Timer _retryTimer;
	private readonly System.Timers.Timer _timeoutTimer;
	private readonly ConcurrentDictionary<ulong, Guid> _pendingConfirms = new();
	private bool _disposed = false;

	public event Action<int>? OnPublished;

	public PublisherService(RabbitMqPublisherConfiguration config, IMessageStore store, ILogger<PublisherService> logger)
	{
		_config = config;
		_store = store;
		_logger = logger;
		_retryTimer = new System.Timers.Timer(TimeSpan.FromMinutes(2).TotalMilliseconds);
		_retryTimer.Elapsed += OnRetryTimerElapsed;
		_retryTimer.AutoReset = true;

		_timeoutTimer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
		_timeoutTimer.Elapsed += OnTimeoutTimerElapsed;
		_timeoutTimer.AutoReset = true;

		OnPublished += count => _logger.LogInformation($"Published {count} messages.");
	}

	public async Task<(bool isSuccess, string errorMessage)> Initialize(CancellationToken ct = default)
	{
		try
		{
			_endpoints = _config.RabbitMqConnections
				.Select(x =>
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
			_channel = await _connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirms: true), ct);

			_channel.BasicAcksAsync += OnConfirmReceivedAsync;
			_channel.BasicNacksAsync += OnFailedReceivedAsync;

			await _channel.QueueDeclareAsync(_config.PublishQueueName, true, false, false, null, ct);

			_replyChannel = await _connection.CreateChannelAsync(ct);
			await _replyChannel.QueueDeclareAsync(_config.ReplyQueueName, true, false, false, null, ct);

			var replyConsumer = new AsyncEventingBasicConsumer(_replyChannel);
			replyConsumer.ReceivedAsync += OnReplyReceivedAsync;
			await _replyChannel.BasicConsumeAsync(_config.ReplyQueueName, false, replyConsumer, ct);

			// Load pending messages
			var pendingMessages = await _store.LoadPendingMessagesAsync(ct);
			foreach (var msg in pendingMessages)
				await _store.SaveAsync(msg, ct); // Ensure in-memory for retries

			_retryTimer.Start();
			_timeoutTimer.Start();

			return (true, "");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Publisher init failed");
			return (false, ex.Message);
		}
	}

	public async Task PublishAsync(MessageEnvelope envelope, CancellationToken ct = default)
	{
		try
		{
			envelope.CorrelationId = envelope.Id.ToString();
			envelope.ReplyQueue = _config.ReplyQueueName;
			await _store.SaveAsync(envelope, ct);
			await InternalPublishAsync(envelope, ct);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Publish failed");
			await _store.UpdateStatusAsync(envelope.Id, "Failed", ex.Message, ct);
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

		await _store.UpdateStatusAsync(envelope.Id, "Publishing", ct);

		var sequenceNumber = _channel!.NextPublishSeqNo;
		_pendingConfirms[sequenceNumber] = envelope.Id;

		await _channel.BasicPublishAsync("", _config.PublishQueueName, envelope.Payload, props, true, ct);
	}

	private async Task OnConfirmReceivedAsync(object? sender, BasicAckEventArgs eventArgs)
	{
		if (eventArgs.Multiple)
		{
			var confirmed = _pendingConfirms.Where(p => p.Key <= eventArgs.DeliveryTag);
			foreach (var (seq, id) in confirmed.ToList())
			{
				if (_pendingConfirms.TryRemove(seq, out _))
				{
					await _store.UpdateStatusAsync(id, "ResponsePending");
				}
			}
		}
		else
		{
			if (_pendingConfirms.TryRemove(eventArgs.DeliveryTag, out var id))
			{
				await _store.UpdateStatusAsync(id, "ResponsePending");
			}
		}
	}

	private async Task OnFailedReceivedAsync(object? sender, BasicNackEventArgs eventArgs)
	{
		if (eventArgs.Multiple)
		{
			var failed = _pendingConfirms.Where(p => p.Key <= eventArgs.DeliveryTag);
			foreach (var (seq, id) in failed.ToList())
			{
				if (_pendingConfirms.TryRemove(seq, out _))
				{
					await _store.UpdateStatusAsync(id, "Failed", "Publish nack");
				}
			}
		}
		else
		{
			if (_pendingConfirms.TryRemove(eventArgs.DeliveryTag, out var id))
			{
				await _store.UpdateStatusAsync(id, "Failed", "Publish nack");
			}
		}
	}

	private async Task OnReplyReceivedAsync(object? sender, BasicDeliverEventArgs ea)
	{
		try
		{
			var correlationId = ea.BasicProperties.CorrelationId;
			var message = await _store.GetAsync(Guid.Parse(correlationId));
			if (message == null)
			{
				await _replyChannel!.BasicNackAsync(ea.DeliveryTag, false, false);
				return;
			}

			var responseJson = Encoding.UTF8.GetString(ea.Body.ToArray());
			var response = JsonConvert.DeserializeObject<MessageEnvelope>(responseJson);

			await _store.UpdateStatusAsync(message.Id, response.Status, response.ErrorMessage);

			await _replyChannel.BasicAckAsync(ea.DeliveryTag, false);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Reply processing failed");
			await _replyChannel!.BasicNackAsync(ea.DeliveryTag, false, true);
		}
	}

	private async void OnRetryTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		try
		{
			var retryable = await _store.GetRetryableAsync();
			int count = 0;
			foreach (var msg in retryable)
			{
				try
				{
					await _store.IncrementRetryCountAsync(msg.Id);
					await _store.UpdateStatusAsync(msg.Id, "Retrying");
					await InternalPublishAsync(msg);
					count++;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Retry failed for {MessageId}", msg.Id);
					if (msg.RetryCount >= _config.MaxRetryCount)
					{
						await _store.UpdateStatusAsync(msg.Id, "Failed", $"Max retries exceeded: {ex.Message}");
					}
					else
					{
						await _store.UpdateStatusAsync(msg.Id, "TryingToPublish", ex.Message);
					}
				}
			}
			OnPublished?.Invoke(count);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Retry timer failed");
		}
	}

	private async void OnTimeoutTimerElapsed(object? sender, ElapsedEventArgs e)
	{
		try
		{
			var oldPending = await _store.GetOldPendingMessagesAsync(_config.TimeoutDuration);
			foreach (var msg in oldPending)
			{
				await _store.UpdateStatusAsync(msg.Id, "TimedOut", "Timeout");
				_logger.LogWarning("Message {MessageId} timed out", msg.Id);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Timeout timer failed");
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_retryTimer.Stop();
		_retryTimer.Dispose();
		_timeoutTimer.Stop();
		_timeoutTimer.Dispose();
		_channel?.Dispose();
		_replyChannel?.Dispose();
		_connection?.Dispose();
		_disposed = true;
	}
}