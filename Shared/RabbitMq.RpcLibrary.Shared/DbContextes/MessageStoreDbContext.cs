using Microsoft.EntityFrameworkCore;
using RabbitMq.RpcLibrary.Shared.DbContextes.Models;

namespace RabbitMq.RpcLibrary.Shared.DbContextes;

public class MessageStoreDbContext(DbContextOptions<MessageStoreDbContext> options) : DbContext(options)
{
	public DbSet<MessageEnvelopeEntity> MessageEnvelopes { get; set; }

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<MessageEnvelopeEntity>(entity =>
		{
			entity.ToTable("MessageEnvelopes");
			entity.HasKey(e => e.Id);

			entity.Property(e => e.Id).IsRequired();
			entity.Property(e => e.Type).IsRequired().HasMaxLength(100);
			entity.Property(e => e.CorrelationId).IsRequired().HasMaxLength(100);
			entity.Property(e => e.ReplyQueue).IsRequired().HasMaxLength(100);
			entity.Property(e => e.Payload).IsRequired();
			entity.Property(e => e.CreatedAt).IsRequired();
			entity.Property(e => e.Status).IsRequired().HasConversion<int>();
			entity.Property(e => e.RetryCount).IsRequired().HasDefaultValue(0);
			entity.Property(e => e.LastRetryAt).IsRequired(false);
			entity.Property(e => e.ErrorMessage).IsRequired(false).HasMaxLength(2000);
			entity.Property(e => e.ResponsePayload).IsRequired(false).HasMaxLength(4000);

			entity.HasIndex(e => e.Status);
			entity.HasIndex(e => e.CreatedAt);
			entity.HasIndex(e => e.RetryCount);
			entity.HasIndex(e => new { e.Status, e.RetryCount, e.LastRetryAt });
		});

		base.OnModelCreating(modelBuilder);
	}
}