namespace RabbitMq.RpcLibrary.Shared.Models;
public enum MessageStatus
{
	TryingToPublish,
	Publishing,
	ResponsePending,
	Failed,
	Completed,
	Retrying,
	Received,
	TimedOut
}