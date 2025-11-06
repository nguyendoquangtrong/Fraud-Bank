using Contracts;
using Messaging;
using Microsoft.EntityFrameworkCore;
using TransactionService.Domain;

namespace TransactionService.Infrastructure;

public class OutboxPublisher(IServiceProvider sp, IConfiguration cfg) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topic = cfg["Topics:Requested"]!;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var prod = scope.ServiceProvider.GetRequiredService<KafkaProducer>();

            var batch = await db.Outbox
                .Where(x => x.PublishedAtUtc == null)
                .OrderBy(x => x.CreatedAtUtc)
                .Take(200)
                .ToListAsync(stoppingToken);

            foreach (var msg in batch)
            {
                if (msg.Type != nameof(TransactionRequested))
                    continue;
                var evt = System.Text.Json.JsonSerializer.Deserialize<TransactionRequested>(msg.Payload)!;
                var headers = new Confluent.Kafka.Headers()
                    .WithCommon(
                        Guid.NewGuid().ToString(),
                        evt.TransactionId,
                        evt.TransactionId);
                await prod.ProduceAsync(topic, key: evt.FromAccount, evt, headers);
                msg.PublishedAtUtc = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(300, stoppingToken);
        }
    }
}
