using Messaging;
using Contracts;
using Microsoft.EntityFrameworkCore;
using TransactionService.Domain;
using TransactionService.Infrastructure;

namespace TransactionService.Infrastructure;

public class TransactionTimeoutJob(
    IServiceProvider sp, 
    IConfiguration cfg, 
    KafkaProducer producer, // <--- Thêm cái này
    ILogger<TransactionTimeoutJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeoutMinutes = cfg.GetValue<int>("Transaction:TimeoutMinutes", 2);
        var checkIntervalSeconds = cfg.GetValue<int>("Transaction:CheckIntervalSeconds", 30);
        var decidedTopic = cfg["Topics:Decided"]!; // <--- Lấy tên topic

        logger.LogInformation("TransactionTimeoutJob started. Timeout: {min} mins", timeoutMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var thresholdTime = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

                // Tìm giao dịch treo
                var stuckTransactions = await db.Transactions
                    .Where(t => t.Status == "REQUESTED" && t.CreatedAtUtc < thresholdTime)
                    .ToListAsync(stoppingToken);

                if (stuckTransactions.Any())
                {
                    foreach (var tx in stuckTransactions)
                    {
                        // 1. Cập nhật DB
                        tx.Status = "FAILED";
                        
                        logger.LogWarning("Transaction {TxId} timed out. Marking FAILED and publishing event.", tx.TxId);

                        // 2. Tạo Event báo lỗi (Sử dụng TransactionDecided)
                        var evt = new TransactionDecided(
                            tx.TxId,
                            "FAILED",           // Quyết định là FAILED
                            -1,                 // Risk không có
                            0, 0,               // Threshold không có
                            "System Timeout",   // Lý do
                            DateTime.UtcNow
                        );

                        // 3. Bắn Event lên Kafka
                        // ProjectionService sẽ nhận được và cập nhật Dashboard thành "DECIDED_FAILED"
                        await producer.ProduceAsync(decidedTopic, tx.TxId, evt);
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error running TransactionTimeoutJob");
            }

            await Task.Delay(TimeSpan.FromSeconds(checkIntervalSeconds), stoppingToken);
        }
    }
}