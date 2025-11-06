using Messaging;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();


var kcfg = builder.Configuration.GetSection("Kafka").Get<Messaging.KafkaConfig>()!;
builder.Services.AddSingleton(kcfg);
builder.Services.AddSingleton(sp => new KafkaProducer(kcfg));

var modelPath = builder.Configuration["Onnx:ModelPath"]!;
var metaPath = builder.Configuration["Onnx:MetadataPath"]!;
builder.Services.AddSingleton(new OnnxScoring(modelPath, metaPath));

builder.Services.AddHostedService<RequestedConsumer>();

var app = builder.Build();
app.MapGet("/", () => "FraudService OK");
app.Run();
