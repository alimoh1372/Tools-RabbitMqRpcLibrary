namespace RabbitMq.RpcLibrary.Shared.Models;

public enum MessageStatus
{
	TryingToPublish,
	Publishing,
	ResponsePending,
	Failed,
	Completed,
	Retrying,
	Received, // When consumer receives the message
	TimedOut // For old pending messages
}