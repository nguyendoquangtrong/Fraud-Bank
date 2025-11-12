using Messaging;
using System.Globalization; // Thêm
using System.Text.Json; // Thêm

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

// *** API TÌM KIẾM/LỌC GIAO DỊCH MỚI ***
app.MapGet("/api/transactions/search", async (
    IHttpClientFactory f,
    IConfiguration cfg,
    string? status,
    string? fromAccount,
    string? toAccount,
    DateTime? startDate,
    DateTime? endDate,
    int size = 100) =>
{
    var http = f.CreateClient("os");
    var baseUrl = cfg["OpenSearch:BaseUrl"]!;
    var index = cfg["OpenSearch:Index"]!;

    // Sử dụng 'filter' trong bool query để OpenSearch cache hiệu quả hơn
    var queryClauses = new List<object>();

    if (!string.IsNullOrEmpty(status))
    {
        queryClauses.Add(new { term = new Dictionary<string, object> { ["status.keyword"] = status } });
    }
    if (!string.IsNullOrEmpty(fromAccount))
    {
        queryClauses.Add(new { term = new Dictionary<string, object> { ["fromAccount.keyword"] = fromAccount } });
    }
    if (!string.IsNullOrEmpty(toAccount))
    {
        queryClauses.Add(new { term = new Dictionary<string, object> { ["toAccount.keyword"] = toAccount } });
    }

    // Xử lý lọc theo khoảng thời gian
    var dateRangeQuery = new Dictionary<string, object>();
    if (startDate.HasValue)
    {
        // Định dạng ISO 8601 (yyyy-MM-ddTHH:mm:ss.fffZ)
        dateRangeQuery["gte"] = startDate.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }
    if (endDate.HasValue)
    {
        dateRangeQuery["lte"] = endDate.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    if (dateRangeQuery.Count > 0)
    {
        queryClauses.Add(new { range = new Dictionary<string, object> { ["createdAtUtc"] = dateRangeQuery } });
    }

    object query;
    if (queryClauses.Count > 0)
    {
        // Nếu có điều kiện lọc, sử dụng bool query
        query = new { @bool = new { filter = queryClauses } };
    }
    else
    {
        // Nếu không, lấy tất cả
        query = new { match_all = new { } };
    }

    var body = new
    {
        query,
        sort = new object[] { new Dictionary<string, object> { ["createdAtUtc"] = "desc" } },
        size
    };
    

    var res = await http.PostAsJsonAsync($"{baseUrl}/{index}/_search", body);
    res.EnsureSuccessStatusCode();
    return Results.Stream(await res.Content.ReadAsStreamAsync(), "application/json");
});


app.MapHub<TxHub>("/hubs/transactions");

using (var scope = app.Services.CreateScope())
{
    var searchIndex = scope.ServiceProvider.GetRequiredService<SearchIndex>();
    app.Logger.LogInformation("Ensuring OpenSearch index exists...");
    await searchIndex.EnsureIndexAsync();
    app.Logger.LogInformation("OpenSearch index check complete.");
}

app.Run();