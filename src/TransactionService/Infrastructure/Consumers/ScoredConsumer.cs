// src/TransactionService/Infrastructure/Consumers/ScoredConsumer.cs (patch)
using Contracts;
using Messaging;
using Microsoft.EntityFrameworkCore;
using TransactionService.Infrastructure;

public class ScoredConsumer(IServiceProvider sp, IConfiguration cfg) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topics = new[] { cfg["Topics:Scored"]! };
        var kcfg = cfg.GetSection("Kafka").Get<Messaging.KafkaConfig>()! with { GroupId = "transaction-policy" };
        var consumer = new KafkaConsumer(kcfg, topics);
        return Task.Run(() => consumer.ConsumeLoop(Handle, stoppingToken), stoppingToken);
    }

    async Task Handle(Confluent.Kafka.ConsumeResult<string,string> cr)
    {
        var scored = Ser.U<TransactionScored>(cr.Message.Value)!;

        using var scope = sp.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var prod = scope.ServiceProvider.GetRequiredService<KafkaProducer>();

        var allowTh = cfg.GetValue<double>("Policy:AllowThreshold");
        var blockTh = cfg.GetValue<double>("Policy:BlockThreshold");

        string decision;
        string? reason = null;

        if (scored.Risk < allowTh) decision = "ALLOW";        
        else if (scored.Risk >= blockTh) decision = "BLOCK";
        else { decision = "REVIEW"; reason = "Between thresholds"; }

        var decisionAt = DateTime.UtcNow;
        var decided = new TransactionDecided(scored.TransactionId, decision, scored.Risk, allowTh, blockTh, reason, decisionAt);
        await prod.ProduceAsync(cfg["Topics:Decided"]!, scored.TransactionId, decided);

        var tracked = await db.Transactions.FirstOrDefaultAsync(x => x.TxId == scored.TransactionId);
        if (tracked is null) return;

        if (tracked.Status == "FAILED")
        {
            return; 
        }

        if (decision != "ALLOW")
        {
            var newStatus = $"DECIDED_{decision}";
            if (!string.Equals(tracked.Status, newStatus, StringComparison.OrdinalIgnoreCase))
            {
                tracked.Status = newStatus;
                await db.SaveChangesAsync();
            }
            return;
        }

        // === ÁP SỔ AN TOÀN & IDEMPOTENT ===
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var t = await db.Transactions
            .FirstOrDefaultAsync(x => x.TxId == scored.TransactionId);

        if (t is null) { await tx.RollbackAsync(); return; }
        if (t.Status is "LEDGER_APPLIED" or "REVIEWED_APPROVE") { await tx.CommitAsync(); return; }

        var from = await db.Accounts.SingleAsync(a => a.AccountNo == t.FromAccount);
        var to   = await db.Accounts.SingleAsync(a => a.AccountNo == t.ToAccount);

        if (from.Balance < t.Amount)
        {
            t.Status = "DECIDED_REVIEW";
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return;
        }

        from.Balance -= t.Amount;
        from.UpdatedAtUtc = DateTime.UtcNow;
        to.Balance += t.Amount;
        to.UpdatedAtUtc = DateTime.UtcNow;

        t.NewBalanceOrig = from.Balance;
        t.NewBalanceDest = to.Balance;
        t.Status = "LEDGER_APPLIED";

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var ledger = new LedgerApplied(
            t.TxId, t.FromAccount, t.ToAccount, t.Amount,
            t.NewBalanceOrig, t.NewBalanceDest, DateTime.UtcNow);

        await prod.ProduceAsync(cfg["Topics:LedgerApplied"]!, t.TxId, ledger);
    }
}
