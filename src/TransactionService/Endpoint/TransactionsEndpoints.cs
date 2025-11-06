// src/TransactionService/Endpoints/TransactionsEndpoints.cs
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Messaging;
using System.Text.Json;
using System.Data;
using TransactionService.Domain;
using TransactionService.Infrastructure;

namespace TransactionService.Endpoints;

public static class TransactionsEndpoints
{
    public static IEndpointRouteBuilder MapTransactionsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").WithTags("Transactions");

        // Rút gọn input
        group.MapPost("/transfer", TransferSimpleAsync)
             .Produces(StatusCodes.Status202Accepted)
             .WithSummary("Transfer money with minimal input: fromAccount, toAccount, amount");

        group.MapPost("/{transactionId}/review", ReviewAsync)
             .Produces(StatusCodes.Status200OK)
             .Produces(StatusCodes.Status400BadRequest)
             .Produces(StatusCodes.Status404NotFound)
             .WithSummary("Duyệt thủ công giao dịch bị BLOCK/REVIEW (approve/reject)");

        return app;
    }

    public record TransferSimpleRequest(string FromAccount, string ToAccount, decimal Amount);
    public record ReviewRequest(string Action, string ReviewedBy, string? Note);

private static async Task<IResult> TransferSimpleAsync(
    AppDbContext db,
    KafkaProducer producer,
    TransferSimpleRequest dto,
    CancellationToken ct)
{
    if (dto.Amount <= 0) return Results.BadRequest(new { error = "Amount must be > 0" });
    if (string.Equals(dto.FromAccount, dto.ToAccount, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "FromAccount and ToAccount must be different" });

    // Bắt transaction DB chỉ để đọc snapshot + ghi aggregate + outbox (KHÔNG update Accounts)
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

    var accs = await db.Accounts
        .Where(a => a.AccountNo == dto.FromAccount || a.AccountNo == dto.ToAccount)
        .ToListAsync(ct);

    var from = accs.FirstOrDefault(a => a.AccountNo == dto.FromAccount);
    var to   = accs.FirstOrDefault(a => a.AccountNo == dto.ToAccount);

    if (from is null || to is null)
        return Results.NotFound(new { error = "Account not found", missing = new {
            fromMissing = from is null ? dto.FromAccount : null,
            toMissing   = to   is null ? dto.ToAccount   : null
        }});

    if (from.Balance < dto.Amount)
        return Results.BadRequest(new { error = "Insufficient funds" });

    var txId = Guid.NewGuid().ToString("N");

    // snapshot (KHÔNG ghi vào Accounts)
    var oldOrg  = from.Balance;
    var oldDest = to.Balance;
    var newOrg  = oldOrg  - dto.Amount;   // dự kiến
    var newDest = oldDest + dto.Amount;   // dự kiến

    var ag = new Transaction
    {
        TxId = txId,
        FromAccount = from.AccountNo,
        ToAccount = to.AccountNo,
        Amount = dto.Amount,
        Type = "TRANSFER",
        OldBalanceOrg = oldOrg,
        NewBalanceOrig = newOrg,       // snapshot dự kiến
        OldBalanceDest = oldDest,
        NewBalanceDest = newDest,      // snapshot dự kiến
        Status = "REQUESTED",
        CreatedAtUtc = DateTime.UtcNow
    };
    db.Transactions.Add(ag);

    var evt = new TransactionRequested(
        ag.TxId, ag.FromAccount, ag.ToAccount, ag.Amount, ag.Type,
        ag.OldBalanceOrg, ag.NewBalanceOrig, ag.OldBalanceDest, ag.NewBalanceDest,
        DateTime.UtcNow);

    db.Outbox.Add(new OutboxMessage {
        AggregateId = ag.TxId,
        Type = nameof(TransactionRequested),
        Payload = JsonSerializer.SerializeToElement(evt)
    });

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return Results.Accepted($"/api/transactions/{txId}", new { transactionId = txId });
}

private static readonly string[] ReviewableStatuses = new[] { "DECIDED_BLOCK", "DECIDED_REVIEW", "REQUESTED" };

private static bool IsReviewableStatus(string? status) =>
    status != null && ReviewableStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);

private static async Task<IResult> ReviewAsync(
    string transactionId,
    ReviewRequest dto,
    AppDbContext db,
    KafkaProducer producer,
    IConfiguration cfg,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(transactionId))
        return Results.BadRequest(new { error = "transactionId is required" });

    if (string.IsNullOrWhiteSpace(dto.Action))
        return Results.BadRequest(new { error = "action is required" });
    if (string.IsNullOrWhiteSpace(dto.ReviewedBy))
        return Results.BadRequest(new { error = "reviewedBy is required" });

    var action = dto.Action.Trim().ToUpperInvariant();
    if (action is not ("APPROVE" or "REJECT"))
        return Results.BadRequest(new { error = "Invalid action. Use APPROVE hoặc REJECT" });
    var reviewer = dto.ReviewedBy.Trim();
    var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

    if (action == "REJECT")
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(x => x.TxId == transactionId, ct);
        if (tx is null)
            return Results.NotFound(new { error = "Transaction not found" });

        if (!IsReviewableStatus(tx.Status) && !string.Equals(tx.Status, "REVIEWED_REJECT", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = $"Transaction status '{tx.Status}' không thể reject" });

        var changed = !string.Equals(tx.Status, "REVIEWED_REJECT", StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            tx.Status = "REVIEWED_REJECT";
            await db.SaveChangesAsync(ct);
        }

        if (changed)
        {
            var reviewedEvent = new TransactionReviewed(transactionId, "REJECT", note, reviewer, DateTime.UtcNow);
            await producer.ProduceAsync(cfg["Topics:Reviewed"]!, transactionId, reviewedEvent);
        }

        return Results.Ok(new { transactionId, status = tx.Status });
    }

    // === APPROVE FLOW: Áp sổ trong transaction SERIALIZABLE ===
    await using var dbTx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    var agg = await db.Transactions.FirstOrDefaultAsync(x => x.TxId == transactionId, ct);
    if (agg is null)
    {
        await dbTx.RollbackAsync(ct);
        return Results.NotFound(new { error = "Transaction not found" });
    }

    if (agg.Status is "REVIEWED_APPROVE" or "LEDGER_APPLIED")
    {
        await dbTx.CommitAsync(ct);
        return Results.Ok(new { transactionId, status = agg.Status });
    }

    if (!IsReviewableStatus(agg.Status))
    {
        await dbTx.RollbackAsync(ct);
        return Results.BadRequest(new { error = $"Transaction status '{agg.Status}' không thể approve" });
    }

    var from = await db.Accounts.SingleAsync(a => a.AccountNo == agg.FromAccount, ct);
    var to   = await db.Accounts.SingleAsync(a => a.AccountNo == agg.ToAccount, ct);

    if (from.Balance < agg.Amount)
    {
        await dbTx.RollbackAsync(ct);
        return Results.BadRequest(new { error = "Insufficient funds để approve giao dịch này" });
    }

    from.Balance -= agg.Amount;
    from.UpdatedAtUtc = DateTime.UtcNow;
    to.Balance += agg.Amount;
    to.UpdatedAtUtc = DateTime.UtcNow;

    agg.NewBalanceOrig = from.Balance;
    agg.NewBalanceDest = to.Balance;
    agg.Status = "REVIEWED_APPROVE";

    await db.SaveChangesAsync(ct);
    await dbTx.CommitAsync(ct);

    var now = DateTime.UtcNow;
    var reviewedEvt = new TransactionReviewed(transactionId, "APPROVE", note, reviewer, now);
    await producer.ProduceAsync(cfg["Topics:Reviewed"]!, transactionId, reviewedEvt);

    var ledgerEvt = new LedgerApplied(
        agg.TxId,
        agg.FromAccount,
        agg.ToAccount,
        agg.Amount,
        agg.NewBalanceOrig,
        agg.NewBalanceDest,
        now);
    await producer.ProduceAsync(cfg["Topics:LedgerApplied"]!, transactionId, ledgerEvt);

    return Results.Ok(new
    {
        transactionId,
        status = agg.Status,
        applied = true
    });
}

}
