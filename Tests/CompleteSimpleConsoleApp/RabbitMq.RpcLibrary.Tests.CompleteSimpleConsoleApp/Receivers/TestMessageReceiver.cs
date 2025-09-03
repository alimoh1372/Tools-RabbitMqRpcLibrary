using RabbitMq.RpcLibrary.Consumer.Dto.Attributes;
using RabbitMq.RpcLibrary.Consumer.Interfaces;
using RabbitMq.RpcLibrary.Tests.CompleteSimpleConsoleApp.Models;

namespace RabbitMq.RpcLibrary.Tests.CompleteSimpleConsoleApp.Receivers;

public class TestMessageReceiver : IRabbitMqReceiver
{
	private readonly ILogger<TestMessageReceiver> _logger;

	public TestMessageReceiver(ILogger<TestMessageReceiver> logger)
	{
		_logger = logger;
	}

	[RabbitMqReceiver("type1")]
	public async Task HandleType1Message(TestMessage1 message)
	{
		_logger.LogInformation($"Received Type1 message: {message.Text} at {message.Timestamp}");
		await Task.Delay(100);
		_logger.LogInformation($"Processed Type1 message: {message.Text}");
	}

	[RabbitMqReceiver("type2")]
	public async Task HandleType2Message(TestMessage2 message)
	{
		_logger.LogInformation($"Received Type2 message: Number={message.Number}, Description={message.Description}");
		await Task.Delay(200);

		if (message.Number % 10 == 0)
		{
			throw new InvalidOperationException($"Simulated failure for number {message.Number}");
		}

		_logger.LogInformation($"Processed Type2 message: {message.Number}");
	}
}