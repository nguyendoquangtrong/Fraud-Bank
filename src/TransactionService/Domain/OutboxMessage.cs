using System.Text.Json;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string AggregateId { get; set; } = default!;
    public string Type { get; set; } = default!;
    public JsonElement Payload { get; set; }   
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
}
