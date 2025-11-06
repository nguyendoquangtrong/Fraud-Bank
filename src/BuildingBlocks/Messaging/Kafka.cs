using Confluent.Kafka;
using System.Text.Json;

namespace Messaging;

public record KafkaConfig(string BootstrapServers, string ClientId, string GroupId);
public static class Ser
{
    static readonly JsonSerializerOptions Opt = new(JsonSerializerOptions.Web);
    public static string J<T>(T obj) => JsonSerializer.Serialize(obj, Opt);
    public static T? U<T>(string s) => JsonSerializer.Deserialize<T>(s, Opt);
}

public static class HeadersEx
{
    public static Headers WithCommon(this Headers h, string messageId, string correlationId, string causationId)
    {
        h.Add("message-id", System.Text.Encoding.UTF8.GetBytes(messageId));
        h.Add("correlation-id", System.Text.Encoding.UTF8.GetBytes(correlationId));
        h.Add("causation-id", System.Text.Encoding.UTF8.GetBytes(causationId));
        h.Add("occurredAt", System.Text.Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));
        h.Add("schemaVersion", System.Text.Encoding.UTF8.GetBytes("v1"));
        return h;
    }
}

public sealed class KafkaProducer : IDisposable
{
    private readonly IProducer<string, string> _p;
    public KafkaProducer(KafkaConfig cfg)
    {
        var pc = new ProducerConfig
        {
            BootstrapServers = cfg.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
            MaxInFlight = 5,
            LingerMs = 5,
            ClientId = cfg.ClientId,
            TransactionalId = $"{cfg.ClientId}-{Guid.NewGuid():N}"
        };
        _p = new ProducerBuilder<string, string>(pc).Build();
        _p.InitTransactions(TimeSpan.FromSeconds(10));
    }
    public async Task ProduceAsync<T>(string topic, string key, T payload, Headers? headers = null)
    {
        _p.BeginTransaction();
        try
        {
            var msg = new Message<string, string>
            {
                Key = key,
                Value = Ser.J(payload),
                Headers = headers ?? new Headers()
            };
            await _p.ProduceAsync(topic, msg);
            _p.CommitTransaction();
        }
        catch
        {
            _p.AbortTransaction();
            throw;
        }
    }
    public void Dispose() => _p.Dispose();
}

public sealed class KafkaConsumer : IDisposable
{
    private readonly IConsumer<string, string> _c;
    public KafkaConsumer(KafkaConfig cfg, IEnumerable<string> topics)
    {
        var cc = new ConsumerConfig
        {
            BootstrapServers = cfg.BootstrapServers,
            GroupId = cfg.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        _c = new ConsumerBuilder<string, string>(cc).Build();
        _c.Subscribe(topics);
    }
    
    public void ConsumeLoop(Func<ConsumeResult<string,string>, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var cr = _c.Consume(ct);
            handler(cr).GetAwaiter().GetResult();
            _c.Commit(cr);
        }
    }
    public void Dispose() => _c.Close();
}