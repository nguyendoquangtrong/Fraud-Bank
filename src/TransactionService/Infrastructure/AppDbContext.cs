// src/TransactionService/Infrastructure/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using TransactionService.Domain;

namespace TransactionService.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> opt) : DbContext(opt)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Account
        b.Entity<Account>(e =>
        {
            e.HasIndex(x => x.AccountNo).IsUnique();
            e.Property(x => x.AccountNo).HasMaxLength(32).IsRequired();
            e.Property(x => x.Balance).HasPrecision(18, 2);
        });


        // Transaction
        b.Entity<Transaction>(e =>
        {
            e.HasIndex(x => x.TxId).IsUnique();
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });

        // Outbox JSONB
        b.Entity<OutboxMessage>()
            .Property(x => x.Payload)
            .HasColumnType("jsonb");
    }
}
