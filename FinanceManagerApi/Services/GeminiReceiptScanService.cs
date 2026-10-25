using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FinanceManagerApi.Models.DTO;

namespace FinanceManagerApi.Services;

public interface IGeminiReceiptScanService
{
    Task<ReceiptScanDto> ScanAsync(Stream image, string mimeType, CancellationToken cancellationToken = default);
}

public sealed class GeminiReceiptScanService : IGeminiReceiptScanService
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

    private static readonly string[] Models =
    [
        "gemini-3.8-flash",
        "gemini-flash-latest",
    ];

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiReceiptScanService> _logger;

    public GeminiReceiptScanService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiReceiptScanService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ReceiptScanDto> ScanAsync(
        Stream image,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["GEMINI_API_KEY"] ??
                     Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("GEMINI_API_KEY is not configured.");
        }

        using var memory = new MemoryStream();
        await image.CopyToAsync(memory, cancellationToken);
        var imageData = Convert.ToBase64String(memory.ToArray());
        Exception? lastException = null;

        foreach (var model in Models)
        {
            using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCancellation.CancelAfter(AttemptTimeout);

            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"v1beta/models/{model}:generateContent");
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = JsonContent.Create(new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = Prompt },
                                new
                                {
                                    inline_data = new
                                    {
                                        mime_type = mimeType,
                                        data = imageData
                                    }
                                }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0,
                        responseMimeType = "application/json",
                        responseSchema = ResponseSchema
                    }
                });

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptCancellation.Token);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    lastException = new HttpRequestException(
                        $"Gemini model {model} returned {(int)response.StatusCode}: {body}");
                    _logger.LogWarning(lastException, "Gemini receipt scan failed for model {Model}", model);
                    if ((int)response.StatusCode is >= 400 and < 500 &&
                        response.StatusCode != System.Net.HttpStatusCode.NotFound &&
                        response.StatusCode != System.Net.HttpStatusCode.RequestTimeout &&
                        response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
                    {
                        break;
                    }

                    continue;
                }

                using var document = JsonDocument.Parse(
                    await response.Content.ReadAsStreamAsync(attemptCancellation.Token));
                var text = document.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException("Gemini returned an empty receipt result.");
                }

                var result = JsonSerializer.Deserialize<GeminiReceiptResponse>(text);
                if (result is null)
                {
                    throw new InvalidOperationException("Gemini returned an invalid receipt result.");
                }

                return Normalize(result);
            }
            catch (OperationCanceledException exception) when (
                !cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException(
                    $"Gemini model {model} did not respond within {AttemptTimeout.TotalSeconds:0} seconds.",
                    exception);
                _logger.LogWarning(lastException, "Gemini receipt scan timed out for model {Model}", model);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
            {
                lastException = exception;
                _logger.LogWarning(exception, "Gemini receipt scan failed for model {Model}", model);
            }
        }

        throw new InvalidOperationException("All Gemini receipt scan models failed.", lastException);
    }

    private static ReceiptScanDto Normalize(GeminiReceiptResponse result)
    {
        DateTime? date = null;
        if (DateTime.TryParse(
                result.Date,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDate))
        {
            date = parsedDate;
        }

        var items = result.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ReceiptScanItemDto
            {
                Name = item.Name!.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount
            })
            .ToList();
        var itemTotal = items.Sum(item => item.Amount ?? 0);
        var itemsMatchTotal = !result.Total.HasValue ||
                              Math.Abs(itemTotal - result.Total.Value) < 0.01m;

        return new ReceiptScanDto
        {
            Merchant = result.Merchant?.Trim(),
            Date = date,
            Currency = result.Currency?.Trim(),
            Items = items,
            Subtotal = result.Subtotal,
            Discount = result.Discount,
            Total = result.Total,
            RawTotalText = result.RawTotalText?.Trim(),
            ItemsMatchTotal = itemsMatchTotal,
            NeedsReview = !itemsMatchTotal || !result.Total.HasValue || items.Count == 0
        };
    }

    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            merchant = new { type = "STRING" },
            date = new { type = "STRING", description = "ISO date YYYY-MM-DD, or empty string if unavailable" },
            currency = new { type = "STRING" },
            items = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        name = new { type = "STRING" },
                        quantity = new { type = "NUMBER" },
                        unitPrice = new { type = "NUMBER" },
                        amount = new { type = "NUMBER" }
                    },
                    required = new[] { "name", "amount" }
                }
            },
            subtotal = new { type = "NUMBER" },
            discount = new { type = "NUMBER" },
            total = new { type = "NUMBER" },
            rawTotalText = new { type = "STRING" }
        },
        required = new[] { "merchant", "date", "currency", "items", "total", "rawTotalText" }
    };

    private const string Prompt = """
        Extract this receipt into the JSON schema. Do not invent values. Use null only where the schema permits it, and use an empty string for unavailable text fields. Include every visible purchased line item, its quantity, unit price, and final line amount. Preserve the receipt's total text in rawTotalText. The total must be the final amount charged, not a subtotal.
        """;
}
