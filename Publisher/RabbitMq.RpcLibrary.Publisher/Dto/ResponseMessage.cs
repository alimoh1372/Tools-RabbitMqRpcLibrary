namespace RabbitMq.RpcLibrary.Publisher.Dto;

public class ResponseMessage
{
	public string Status { get; set; } = string.Empty; // "Received", "Success", "Failure"
	public string? Payload { get; set; }
	public string? ErrorMessage { get; set; }
}