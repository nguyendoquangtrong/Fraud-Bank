namespace TransactionService.Domain;

public class Transaction
{
    public Guid Id { get; set; }
    public string TxId { get; set; } = default!;
    public string FromAccount { get; set; } = default!;
    public string ToAccount { get; set; } = default!;
    public decimal Amount { get; set; }
    public string Type { get; set; } = "TRANSFER";
    public decimal OldBalanceOrg { get; set; }
    public decimal NewBalanceOrig { get; set; }
    public decimal OldBalanceDest { get; set; }
    public decimal NewBalanceDest { get; set; }
    public string Status { get; set; } = "REQUESTED"; 
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
