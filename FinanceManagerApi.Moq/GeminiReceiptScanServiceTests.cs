using System.Net;
using System.Text;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagerApi.Moq;

public sealed class GeminiReceiptScanServiceTests
{
    [Theory]
    [InlineData("306571b207386a00f4a1d293ec1e8b3d.jpg")]
    [InlineData("4b03dde1d159048fb76f60c109543c79.jpg")]
    public async Task ScanAsync_ShouldReturnStructuredReceipt(string sampleName)
    {
        var handler = new GeminiHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = "test-key"
            })
            .Build();
        var service = new GeminiReceiptScanService(
            client,
            configuration,
            NullLogger<GeminiReceiptScanService>.Instance);

        await using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", sampleName));
        var result = await service.ScanAsync(image, "image/jpeg");

        Assert.Equal("Green Leaf Pharmacy", result.Merchant);
        Assert.Equal(12.50m, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.ItemsMatchTotal);
        Assert.False(result.NeedsReview);
        Assert.Contains("inline_data", handler.RequestBody);
        Assert.Equal("test-key", handler.ApiKey);
    }

    private sealed class GeminiHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[{"text":"{\"merchant\":\"Green Leaf Pharmacy\",\"date\":\"2026-10-22\",\"currency\":\"USD\",\"items\":[{\"name\":\"Milk\",\"quantity\":1,\"unitPrice\":5.5,\"amount\":5.5},{\"name\":\"Bread\",\"quantity\":1,\"unitPrice\":7,\"amount\":7}],\"subtotal\":12.5,\"discount\":0,\"total\":12.5,\"rawTotalText\":\"TOTAL $12.50\"}"}]}}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
