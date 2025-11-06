using System.Text.Json.Serialization;

public sealed class TxEvent
{
    public string TransactionId { get; init; } = default!;
    public string Status { get; init; } = default!;
    public string? Decision { get; init; }
    public double? Risk { get; init; }

    public string? FromAccount { get; init; }
    public string? ToAccount { get; init; }
    public double? Amount { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DateTime CreatedAtUtc { get; init; }
}
