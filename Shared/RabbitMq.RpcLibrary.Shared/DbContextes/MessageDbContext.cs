using Microsoft.EntityFrameworkCore;
using RabbitMq.RpcLibrary.Shared.Models;

namespace RabbitMq.RpcLibrary.Shared.DbContextes;

public class MessageDbContext(DbContextOptions<MessageDbContext> options) : DbContext(options)
{
	public DbSet<MessageEnvelope> Messages { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<MessageEnvelope>()
			.HasKey(m => m.Id);
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.Type)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.CorrelationId)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.ReplyQueue)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.Payload)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.CreatedAt)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.Status)
			.IsRequired();
		modelBuilder.Entity<MessageEnvelope>()
			.Property(m => m.RetryCount)
			.IsRequired();
	}
}