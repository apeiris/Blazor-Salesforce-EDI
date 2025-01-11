using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

public class SalesforceAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SalesforceAuthService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        var client = _httpClientFactory.CreateClient("Salesforce");
        string clientId = _configuration["Salesforce:ClientId"];
        string clientSecret = _configuration["Salesforce:ClientSecret"];
        string username = _configuration["Salesforce:Username"];
        string password = _configuration["Salesforce:Password"];

        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "password" },
            { "client_id", clientId },
            { "client_secret", clientSecret },
            { "username", username },
            { "password", password }
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await client.PostAsync("services/oauth2/token", content);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error retrieving access token: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<JsonElement>(responseContent);
        return tokenData.GetProperty("access_token").GetString();
    }

    public async Task<List<(string Id, string Name)>> GetProductsWithoutPricebookEntryAsync(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        string myUrl = _configuration["Salesforce:myUrl"];
        var query = "SELECT Id, Name FROM Product2 WHERE Id NOT IN (SELECT Product2Id FROM PricebookEntry)";
        var url = $"{myUrl}/services/data/v57.0/query?q={Uri.EscapeDataString(query)}";

        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error querying products: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var products = jsonResponse.GetProperty("records")
                                   .EnumerateArray()
                                   .Select(record => (
                                       Id: record.GetProperty("Id").GetString(),
                                       Name: record.GetProperty("Name").GetString()
                                   ))
                                   .ToList();

        return products;
    }

    public async Task<List<(string Id, string Name)>> GetPricebooksAsync(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        string myUrl = _configuration["Salesforce:myUrl"];
        var url = $"{myUrl}/services/data/v57.0/query?q=SELECT+Id,Name,IsActive,IsStandard+FROM+Pricebook2";

        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error retrieving pricebooks: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var pricebooks = jsonResponse.GetProperty("records")
                                     .EnumerateArray()
                                     .Select(record => (
                                         Id: record.GetProperty("Id").GetString(),
                                         Name: record.GetProperty("Name").GetString()
                                     ))
                                     .ToList();

        return pricebooks;
    }

    public async Task AddProductsToPricebookAsync(string accessToken, string pricebookId, List<string> productIds)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        string myUrl = _configuration["Salesforce:myUrl"];
        var url = $"{myUrl}/services/data/v57.0/composite/sobjects";

        var records = productIds.Select(productId => new
        {
            attributes = new { type = "PricebookEntry" },
            Pricebook2Id = pricebookId,
            Product2Id = productId,
            IsActive = true,
            UnitPrice = 0 // Set appropriate price if required
        }).ToList();

        var payload = new
        {
            allOrNone = false,
            records
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error adding products to pricebook: {response.StatusCode}");
        }
    }
}
