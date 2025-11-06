using System.Net.Http.Json;
using System.Runtime.CompilerServices;

public class SearchIndex(IConfiguration cfg, IHttpClientFactory httpFactory)
{
    private readonly string _base = cfg["OpenSearch:BaseUrl"]!;
    private readonly string _index = cfg["OpenSearch:Index"]!;
    private readonly HttpClient _http = httpFactory.CreateClient("os");

    public async Task EnsureIndexAsync()
    {
        var res = await _http.GetAsync($"{_base}/{_index}");
        if (!res.IsSuccessStatusCode)
        {
            var mapping = new
            {
                settings = new { number_of_shards = 1, number_of_replicas = 0 },
                mappings = new
                {
                    properties = new
                    {
                        transactionId = new { type = "Keyword" },
                        status = new { type = "keyword" },
                        risk = new { type = "double" },
                        amount = new { type = "double" },
                        fromAccount = new { type = "keyword" },
                        toAccount = new { type = "keyword" },
                        createdAtUtc = new { type = "date" }
                    }
                }
            };
            await _http.PutAsJsonAsync($"{_base}/{_index}", mapping);
        }
    }
    public Task UpsertAsync(string id, object partial)
    {
        var body = new { doc = partial, doc_as_upsert = true };
        return _http.PostAsJsonAsync($"{_base}/{_index}/_update/{id}?refresh=true", body);
    }

}