using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FinanceManagerApi.Options;
using Microsoft.Extensions.Options;

namespace FinanceManagerApi.Services.Storage;

/// <summary>
/// Backblaze B2 through its S3-compatible API.
///
/// Two settings are load-bearing and easy to lose in a refactor:
/// <c>ForcePathStyle</c> (B2 has no virtual-host style buckets) and the v4 SDK's
/// checksum defaults, which B2 rejects — both are pinned to WHEN_REQUIRED below.
///
/// The client is created once and shared: AmazonS3Client is thread-safe, and the
/// class is registered as a singleton, so it is disposed with the app.
/// </summary>
public sealed class B2ReceiptStorage : IReceiptStorage, IDisposable
{
    private readonly B2Options _options;
    private readonly ILogger<B2ReceiptStorage> _logger;
    private readonly AmazonS3Client? _client;

    public B2ReceiptStorage(IOptions<B2Options> options, ILogger<B2ReceiptStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (!IsConfigured)
        {
            // Not an error: the API is usable without photo storage, and the
            // receipt-photo endpoints answer 503 until it is configured.
            _logger.LogWarning(
                "Receipt photo storage is not configured (B2:ServiceUrl/Region/KeyId/AppKey/Bucket). " +
                "Receipt photos will be unavailable; everything else keeps working.");
            return;
        }

        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.KeyId, _options.AppKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                AuthenticationRegion = _options.Region,
                ForcePathStyle = true,
                // Newer SDK versions add optional checksums that some
                // S3-compatible stores (B2 included) reject outright.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            });

        _logger.LogInformation("Receipt photo storage: bucket {Bucket} at {ServiceUrl}", _options.Bucket, _options.ServiceUrl);
    }

    public bool IsConfigured => _options.IsConfigured;

    public async Task PutAsync(
        string key,
        Stream data,
        long length,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = data,
            ContentType = contentType,
            // The caller owns the stream and disposes it.
            AutoCloseStream = false
        };
        request.Headers.ContentLength = length;

        try
        {
            await client.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("Stored receipt photo {Key} ({Bytes} bytes)", key, length);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonServiceException exception)
        {
            _logger.LogError(exception, "Could not store receipt photo {Key}: {Message}", key, exception.Message);
            throw new ReceiptStorageException("The receipt photo could not be stored.", exception);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not reach the receipt photo store for {Key}", key);
            throw new ReceiptStorageException("The receipt photo could not be stored.", exception);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var client = RequireClient();

        try
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _options.Bucket, Key = key },
                cancellationToken);
            _logger.LogInformation("Deleted receipt photo {Key}", key);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AmazonServiceException exception)
        {
            _logger.LogError(exception, "Could not delete receipt photo {Key}: {Message}", key, exception.Message);
            throw new ReceiptStorageException("The receipt photo could not be removed.", exception);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError(exception, "Could not reach the receipt photo store for {Key}", key);
            throw new ReceiptStorageException("The receipt photo could not be removed.", exception);
        }
    }

    public string GetSignedUrl(string key, TimeSpan ttl)
    {
        var client = RequireClient();

        return client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl)
        });
    }

    private AmazonS3Client RequireClient() =>
        _client ?? throw new ReceiptStorageException(
            "Receipt photo storage is not configured.",
            new InvalidOperationException("B2 credentials are missing."));

    public void Dispose() => _client?.Dispose();
}
