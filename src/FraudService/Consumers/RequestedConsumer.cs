using Contracts;
using Messaging;
using System.Text.Json;

public class RequestedConsumer(IServiceProvider sp, IConfiguration cfg) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var topics = new[] { cfg["Topics:Requested"]! };
        var kcfg = cfg.GetSection("Kafka").Get<Messaging.KafkaConfig>()!;
        var consumer = new KafkaConsumer(kcfg, topics);
        return Task.Run(() => consumer.ConsumeLoop(Handle, stoppingToken), stoppingToken);
    }
    async Task Handle(Confluent.Kafka.ConsumeResult<string,string> cr)
    {
        var requested = Ser.U<TransactionRequested>(cr.Message.Value)!;
        if (requested == null) return;

        var timeoutMinutes = cfg.GetValue<int>("Transaction:TimeoutMinutes", 2);

        var age = DateTime.UtcNow - requested.OccurredAtUtc;
        if (age.TotalMinutes > timeoutMinutes)
        {
            var logger = sp.GetRequiredService<ILogger<RequestedConsumer>>();
            logger.LogWarning("Transaction {TxId} expired (age: {Age}s). Skipped scoring.",
                requested.TransactionId, age.TotalSeconds);
            return; 
        }
        using var scope = sp.CreateScope();
        var prod = scope.ServiceProvider.GetRequiredService<KafkaProducer>();

        var model = scope.ServiceProvider.GetRequiredService<OnnxScoring>();

        var feat = BuildFeatures(requested); // map theo metadata.json
        var reverseMap = LoadReverseMap(scope.ServiceProvider.GetRequiredService<IConfiguration>());
        var (risk, label) = model.Predict(feat, reverseMap);

        var scored = new TransactionScored(requested.TransactionId, risk, label, feat, DateTime.UtcNow);
        await prod.ProduceAsync("transactions.scored", requested.TransactionId, scored);
    }

    static IDictionary<string,double> BuildFeatures(TransactionRequested e)
    {
        // mapping original names (metadata feature_mapping values)
        var isEmptied = e.NewBalanceOrig == 0 && e.Amount > 0 ? 1d : 0d;
        var errorOrig = (double)(e.OldBalanceOrg - e.Amount - e.NewBalanceOrig);
        var errorDest = (double)(e.NewBalanceDest - e.OldBalanceDest - e.Amount);
        var (cashOut, debit, payment, transfer) = OneHotType(e.Type);

        return new Dictionary<string,double> {
            ["amount"] = (double)e.Amount,
            ["oldbalanceOrg"] = (double)e.OldBalanceOrg,
            ["newbalanceOrig"] = (double)e.NewBalanceOrig,
            ["oldbalanceDest"] = (double)e.OldBalanceDest,
            ["newbalanceDest"] = (double)e.NewBalanceDest,
            ["isAccountEmptied"] = isEmptied,
            ["errorBalanceOrig"] = errorOrig,
            ["errorBalanceDest"] = errorDest,
            ["type_CASH_OUT"] = cashOut,
            ["type_DEBIT"] = debit,
            ["type_PAYMENT"] = payment,
            ["type_TRANSFER"] = transfer
        };
    }

    static (double cashOut,double debit,double payment,double transfer) OneHotType(string type)
    {
        type = type?.ToUpperInvariant() ?? "";
        return (
            cashOut: type == "CASH_OUT" ? 1 : 0,
            debit:   type == "DEBIT"    ? 1 : 0,
            payment: type == "PAYMENT"  ? 1 : 0,
            transfer:type == "TRANSFER" ? 1 : 0
        );
    }

    static IDictionary<string,string> LoadReverseMap(IConfiguration cfg)
    {
        var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(cfg["Onnx:MetadataPath"]!))!;
        
        return meta.feature_mapping.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private record Meta(string input_name, string[] output_names, Dictionary<string,string> feature_mapping, string[] input_order);
}
