using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

public class SalesforceServices
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SalesforceServices(IHttpClientFactory httpClientFactory, IConfiguration configuration)
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
    public async Task DeleteProductAsync(string accessToken, string productId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        string myUrl = _configuration["Salesforce:myUrl"];
        var url = $"{myUrl}/services/data/v57.0/sobjects/Product2/{productId}";
        var response = await client.DeleteAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error deleting product: {response.StatusCode}");
        }
    }
    public async Task<List<Order>> GetOrdersAsync(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var query = "SELECT Id, Name, Status, AccountId, TotalAmount FROM Order";
        string url = $"{_configuration["Salesforce:myUrl"]}/services/data/v57.0/query?q={Uri.EscapeDataString(query)}";
        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error fetching orders: {response.StatusCode} - {errorResponse}");
        }
        var content = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(content);

        var records = jsonResponse.GetProperty("records");
        var orders = new List<Order>();

        foreach (var record in records.EnumerateArray())
        {
            orders.Add(new Order
            {
                Id = record.GetProperty("Id").GetString(),
                Name = record.GetProperty("Name").GetString(),
                Status = record.GetProperty("Status").GetString(),
                AccountId = record.GetProperty("AccountId").GetString(),
                TotalAmount = record.GetProperty("TotalAmount").GetDecimal()
            });
        }

        return orders;
    }

    public async Task<Order> GetOrderByIdAsync(string accessToken, string orderId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Salesforce SOQL query to get the order by ID
        var query = $"SELECT Id, Name, Status, AccountId, TotalAmount,Pricebook2Id,Pricebook2.Name FROM Order WHERE Id = '{orderId}'";
        var url = $"{ _configuration["Salesforce:myUrl"]}/services/data/v57.0/query?q={Uri.EscapeDataString(query)}";
        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error fetching order by ID: {response.StatusCode} - {errorResponse}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(content);

        var records = jsonResponse.GetProperty("records");
        if (records.GetArrayLength() == 0)
        {
            throw new Exception("Order not found.");
        }

        var record = records[0];
        return new Order
        {
            Id = record.GetProperty("Id").GetString(),
            Name = record.GetProperty("Name").GetString(),
            Pricebook2Id = record.GetProperty("Pricebook2Id").GetString(),
            PricebookName = record.GetProperty("Pricebook2").GetProperty("Name").GetString(),
            Status = record.GetProperty("Status").GetString(),
            AccountId = record.GetProperty("AccountId").GetString(),
            TotalAmount = record.TryGetProperty("TotalAmount", out var totalAmountProp)
                ? totalAmountProp.GetDecimal()
                : null
        };

    }

    public async Task UpdateOrderAsync(string accessToken, Order order)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Salesforce API URL to update the Order
  
        var url = $"{_configuration["Salesforce:myUrl"]}/services/data/v57.0/sobjects/Order/{order.Id}";


        // Create the JSON payload with the fields to update
        var payload = new
        {
            Name = order.Name,
            Status = order.Status,
            //TotalAmount = order.TotalAmount
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        // Send the PATCH request to Salesforce
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = content
        };

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error updating order: {response.StatusCode} - {errorResponse}");
        }
    }
    public async Task<List<Product>> GetProductsAsync(string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Salesforce REST API endpoint to retrieve Products with PricebookEntry
        var url = $"{_configuration["Salesforce:myUrl"]}/services/data/v57.0/query/?q=SELECT+Id,+Name,+Description+FROM+Product2+WHERE+Id+IN+(SELECT+Product2Id+FROM+PricebookEntry)";

        var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error retrieving products: {response.StatusCode}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        var productData = JsonSerializer.Deserialize<JsonElement>(responseContent);

        var products = new List<Product>();

        foreach (var record in productData.GetProperty("records").EnumerateArray())
        {
            products.Add(new Product
            {
                Id = record.GetProperty("Id").GetString(),
                Name = record.GetProperty("Name").GetString(),
                Description = record.GetProperty("Description").GetString()
            });
        }

        return products;
    }

    public async Task AddProductToOrderAsync(string accessToken, string orderId, string productId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Salesforce API URL to add product to order
        var url = $"{_configuration["Salesforce:myUrl"]}/services/data/v57.0/sobjects/OrderItem/";

        // Create the OrderItem to link the product with the order
        var orderItem = new
        {
            OrderId = orderId,
            Product2Id = productId,
            Quantity = 1,  // Set quantity as needed
            UnitPrice = 0  // Set a price if needed, or fetch from the product
        };

        var content = new StringContent(JsonSerializer.Serialize(orderItem), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorResponse = await response.Content.ReadAsStringAsync();
            throw new Exception($"Error adding product to order: {response.StatusCode} - {errorResponse}");
        }
    }



    public class Order
    {
        public string? Id { get; set; }
        public string? PricebookName { get; set; }
        public string? Pricebook2Id { get; set; }
        public string? Name { get; set; }
        public string? Status { get; set; }
        public string? AccountId { get; set; }
        public decimal? TotalAmount { get; set; }
    }
    public class Product
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

}
