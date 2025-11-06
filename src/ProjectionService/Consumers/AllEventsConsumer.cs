using Contracts;
using Messaging;
using Microsoft.AspNetCore.SignalR;

public class AllEventsConsumer(IServiceProvider sp, IConfiguration cfg, IHubContext<TxHub> hub) : BackgroundService
{
    private readonly IHubContext<TxHub> _hub = hub;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topics = cfg.GetSection("Topics").Get<string[]>()!;
        var kcfg = cfg.GetSection("Kafka").Get<Messaging.KafkaConfig>()!;
        var consumer = new KafkaConsumer(kcfg, topics);

        var idx = sp.GetRequiredService<SearchIndex>();
        idx.EnsureIndexAsync().GetAwaiter().GetResult();

        return Task.Run(() => consumer.ConsumeLoop(Handle, stoppingToken), stoppingToken);
    }

    async Task Handle(Confluent.Kafka.ConsumeResult<string,string> cr)
    {
        using var scope = sp.CreateScope();
        var idx = scope.ServiceProvider.GetRequiredService<SearchIndex>();
        var topic = cr.Topic;

        if (topic.EndsWith("requested"))
        {
            var e = Ser.U<TransactionRequested>(cr.Message.Value)!;
            await idx.UpsertAsync(e.TransactionId, new {
                transactionId = e.TransactionId, status = "REQUESTED",
                amount = (double)e.Amount, fromAccount = e.FromAccount, toAccount = e.ToAccount,
                createdAtUtc = e.OccurredAtUtc
            });

            await _hub.Clients.All.SendAsync("txEvent", new TxEvent {
                TransactionId = e.TransactionId,
                Status = "REQUESTED",
                Amount = (double)e.Amount,
                FromAccount = e.FromAccount,
                ToAccount   = e.ToAccount,
                CreatedAtUtc = e.OccurredAtUtc
            });
        }
        else if (topic.EndsWith("scored"))
        {
            var e = Ser.U<TransactionScored>(cr.Message.Value)!;
            await idx.UpsertAsync(e.TransactionId, new { risk = e.Risk, status = "SCORED" });

            await _hub.Clients.All.SendAsync("txEvent", new TxEvent {
                TransactionId = e.TransactionId,
                Status = "SCORED",
                Risk = e.Risk,
                CreatedAtUtc = e.OccurredAtUtc
            });
        }
        else if (topic.EndsWith("decided"))
        {
            var e = Ser.U<TransactionDecided>(cr.Message.Value)!;
            await idx.UpsertAsync(e.TransactionId, new { status = $"DECIDED_{e.Decision}", risk = e.Risk });

            await _hub.Clients.All.SendAsync("txEvent", new TxEvent {
                TransactionId = e.TransactionId,
                Status = $"DECIDED_{e.Decision}",
                Decision = e.Decision,
                Risk = e.Risk,
                CreatedAtUtc = e.OccurredAtUtc
            });
        }
        else if (topic.EndsWith("ledger-applied"))
        {
            var e = Ser.U<LedgerApplied>(cr.Message.Value)!;
            await idx.UpsertAsync(e.TransactionId, new { status = "LEDGER_APPLIED" });

            await _hub.Clients.All.SendAsync("txEvent", new TxEvent {
                TransactionId = e.TransactionId,
                Status = "LEDGER_APPLIED",
                Amount = (double)e.Amount,
                FromAccount = e.FromAccount,
                ToAccount = e.ToAccount,
                CreatedAtUtc = e.OccurredAtUtc
            });
        }
        else if (topic.EndsWith("reviewed"))
        {
            var e = Ser.U<TransactionReviewed>(cr.Message.Value)!;
            await idx.UpsertAsync(e.TransactionId, new { status = $"REVIEWED_{e.Action}" });

            await _hub.Clients.All.SendAsync("txEvent", new TxEvent {
                TransactionId = e.TransactionId,
                Status = $"REVIEWED_{e.Action}",
                CreatedAtUtc = e.OccurredAtUtc
            });
        }
    }
}
