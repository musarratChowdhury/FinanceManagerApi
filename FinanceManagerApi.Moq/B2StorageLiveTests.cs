using System.Text;
using FinanceManagerApi.Options;
using FinanceManagerApi.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Xunit.Abstractions;

namespace FinanceManagerApi.Moq;

/// <summary>
/// Throws a real object at the configured B2 bucket: put, presign, fetch, delete,
/// then prove the fetch stops working. Skipped unless the B2_TEST_* variables are set.
/// </summary>
public sealed class B2StorageLiveTests
{
    private readonly ITestOutputHelper _output;

    public B2StorageLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [B2Fact]
    [Trait("Category", "Integration")]
    public async Task PutPresignFetchDelete_RoundTrips()
    {
        var options = OptionsFromEnvironment();
        using var storage = new B2ReceiptStorage(
            MsOptions.Create(options),
            NullLogger<B2ReceiptStorage>.Instance);

        var key = "receipts/tests/" + Guid.NewGuid().ToString("N") + ".txt";
        var bytes = Encoding.UTF8.GetBytes($"receipt photo probe {DateTime.UtcNow:O}");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        await using (var content = new MemoryStream(bytes))
        {
            await storage.PutAsync(key, content, bytes.LongLength, "text/plain");
        }

        try
        {
            var url = storage.GetSignedUrl(key, TimeSpan.FromMinutes(2));
            _output.WriteLine($"Presigned URL host: {new Uri(url).Host}");

            var fetched = await http.GetByteArrayAsync(url);
            Assert.Equal(bytes, fetched);
        }
        finally
        {
            await storage.DeleteAsync(key);
        }

        // Deleting a missing key succeeds; the presigned GET must now be refused.
        using var afterDelete = await http.GetAsync(storage.GetSignedUrl(key, TimeSpan.FromMinutes(2)));
        Assert.False(afterDelete.IsSuccessStatusCode, $"Expected a refusal, got {(int)afterDelete.StatusCode}");
    }

    [B2Fact]
    [Trait("Category", "Integration")]
    public async Task DeleteAsync_IsIdempotent()
    {
        var options = OptionsFromEnvironment();
        using var storage = new B2ReceiptStorage(
            MsOptions.Create(options),
            NullLogger<B2ReceiptStorage>.Instance);

        await storage.DeleteAsync("receipts/tests/" + Guid.NewGuid().ToString("N") + ".txt");
    }

    private static B2Options OptionsFromEnvironment() => new()
    {
        ServiceUrl = Environment.GetEnvironmentVariable("B2_TEST_SERVICE_URL")!,
        Region = Environment.GetEnvironmentVariable("B2_TEST_REGION")!,
        KeyId = Environment.GetEnvironmentVariable("B2_TEST_KEY_ID")!,
        AppKey = Environment.GetEnvironmentVariable("B2_TEST_APP_KEY")!,
        Bucket = Environment.GetEnvironmentVariable("B2_TEST_BUCKET")!
    };
}

internal sealed class B2FactAttribute : FactAttribute
{
    private static readonly string[] RequiredVariables =
    {
        "B2_TEST_SERVICE_URL",
        "B2_TEST_REGION",
        "B2_TEST_KEY_ID",
        "B2_TEST_APP_KEY",
        "B2_TEST_BUCKET"
    };

    public B2FactAttribute()
    {
        var missing = RequiredVariables
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();

        if (missing.Length > 0)
        {
            Skip = $"Set {string.Join(", ", missing)} to run live B2 storage tests.";
        }
    }
}

