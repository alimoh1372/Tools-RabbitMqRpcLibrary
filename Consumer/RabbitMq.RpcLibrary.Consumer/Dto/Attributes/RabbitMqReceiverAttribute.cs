namespace RabbitMq.RpcLibrary.Consumer.Dto.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class RabbitMqReceiverAttribute(string receiverType) : Attribute
{
	public string ReceiverType { get; } = receiverType;
}