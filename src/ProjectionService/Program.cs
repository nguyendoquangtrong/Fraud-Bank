using Messaging;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddHttpClient("os", c => { c.Timeout = TimeSpan.FromSeconds(5); });
builder.Services.AddSingleton<SearchIndex>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<AllEventsConsumer>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

var app = builder.Build();

app.UseCors();

app.MapGet("/api/transactions/{id}", async (string id, IConfiguration cfg, IHttpClientFactory f) =>
{
    var baseUrl = cfg["OpenSearch:BaseUrl"]!;
    var index = cfg["OpenSearch:Index"]!;
    var http = f.CreateClient("os");
    var res = await http.GetAsync($"{baseUrl}/{index}/_doc/{id}");
    return Results.Content(await res.Content.ReadAsStringAsync(), "application/json");
});

// Lấy toàn bộ lịch sử (mặc định 1000) – sort theo thời gian giảm dần
app.MapGet("/api/transactions/latest", async (IHttpClientFactory f, IConfiguration cfg, int size = 1000) =>
{
    var http = f.CreateClient("os");
    var baseUrl = cfg["OpenSearch:BaseUrl"]!;
    var index   = cfg["OpenSearch:Index"]!;
    var body = new {
        query = new { match_all = new { } },
        sort = new object[] { new Dictionary<string, object> { ["createdAtUtc"] = "desc" } },
        size
    };
    var res = await http.PostAsJsonAsync($"{baseUrl}/{index}/_search", body);
    res.EnsureSuccessStatusCode();
    return Results.Stream(await res.Content.ReadAsStreamAsync(), "application/json");
});

app.MapHub<TxHub>("/hubs/transactions");

app.Run();
