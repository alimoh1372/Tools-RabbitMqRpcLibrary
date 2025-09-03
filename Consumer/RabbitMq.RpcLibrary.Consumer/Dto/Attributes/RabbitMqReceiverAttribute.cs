namespace RabbitMq.RpcLibrary.Consumer.Dto.Attributes;



[AttributeUsage(AttributeTargets.Method)]
public class RabbitMqReceiverAttribute(string type) : Attribute
{
	public string ReceiverType { get; } = type;
}