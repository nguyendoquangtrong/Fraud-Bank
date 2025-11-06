namespace TransactionService.Domain;

public class Account
{
    public Guid Id { get; set; }
    public string AccountNo { get; set; } = default!;
    public string? HolderName { get; set; }
    public decimal Balance { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    }
