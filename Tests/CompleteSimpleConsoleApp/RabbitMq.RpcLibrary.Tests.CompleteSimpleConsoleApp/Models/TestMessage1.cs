namespace RabbitMq.RpcLibrary.Tests.CompleteSimpleConsoleApp.Models;

public class TestMessage1
{
	public string Text { get; set; } = string.Empty;
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}