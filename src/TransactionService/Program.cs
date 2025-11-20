using Contracts;
using Messaging;
using Microsoft.EntityFrameworkCore;
using TransactionService.Endpoints;
using TransactionService.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

var kcfg = builder.Configuration.GetSection("Kafka").Get<KafkaConfig>()!;
builder.Services.AddSingleton(kcfg);
builder.Services.AddSingleton(_ => new KafkaProducer(kcfg));

builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<ScoredConsumer>();
builder.Services.AddHostedService<TransactionTimeoutJob>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

app.MapAccountsEndpoints();
app.MapTransactionsEndpoints();

app.Run();
