using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Models.Errors;
using FinanceManagerApi.Options;
using FinanceManagerApi.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceManagerApi.Services;

public interface IReceiptImageService
{
    /// <summary>Metadata for the receipt's photo, or null when it has none.</summary>
    Task<ReceiptImageDto?> GetAsync(long receiptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Short-lived download URL, or null when the receipt has no photo.
    /// <paramref name="ttlSeconds"/> is clamped to 60–3600.
    /// </summary>
    Task<ReceiptImageUrlDto?> GetUrlAsync(
        long receiptId,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the photo, replacing any existing one for this receipt. Throws
    /// <see cref="ReceiptImageException"/> with the status the controller should return.
    /// </summary>
    Task<ReceiptImageDto> UploadAsync(
        long receiptId,
        IFormFile file,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the photo. False when the receipt has none.</summary>
    Task<bool> DeleteAsync(long receiptId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the receipt-photo lifecycle. Ordering is the whole game here:
///
/// * store the object, then write the row — a failed row is cleaned up by
///   deleting the object, so the bucket never holds an unreferenced photo;
/// * on replace, the old object is deleted only after the row points at the new one;
/// * on delete, the object goes first, so a store failure leaves a row that can
///   still be retried instead of a silently orphaned object.
/// </summary>
public sealed class ReceiptImageService : IReceiptImageService
{
    private const int MinTtlSeconds = 60;
    private const int MaxTtlSeconds = 3600;
    private const int ReadBufferBytes = 81920;

    private readonly FinanceDbContext _context;
    private readonly IReceiptStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly B2Options _options;
    private readonly ILogger<ReceiptImageService> _logger;

    public ReceiptImageService(
        FinanceDbContext context,
        IReceiptStorage storage,
        ICurrentUser currentUser,
        IOptions<B2Options> options,
        ILogger<ReceiptImageService> logger)
    {
        _context = context;
        _storage = storage;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReceiptImageDto?> GetAsync(long receiptId, CancellationToken cancellationToken = default)
    {
        var image = await FindOwnedImageAsync(receiptId, cancellationToken);
        return image is null ? null : ToDto(image);
    }

    public async Task<ReceiptImageUrlDto?> GetUrlAsync(
        long receiptId,
        int? ttlSeconds = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var image = await FindOwnedImageAsync(receiptId, cancellationToken);
        if (image is null)
        {
            return null;
        }

        var ttl = ResolveTtl(ttlSeconds);

        return new ReceiptImageUrlDto
        {
            Url = _storage.GetSignedUrl(image.ObjectKey, ttl),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public async Task<ReceiptImageDto> UploadAsync(
        long receiptId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var userId = _currentUser.UserId;

        // Ownership first: "someone else's receipt" is a 404, so ids stay unprobeable.
        var owned = await _context.Receipts
            .AnyAsync(receipt => receipt.Id == receiptId && receipt.CreatedBy == userId, cancellationToken);

        if (!owned)
        {
            throw new ReceiptImageException(StatusCodes.Status404NotFound, "Receipt not found.");
        }

        if (file is null || file.Length == 0)
        {
            throw new ReceiptImageException(StatusCodes.Status400BadRequest, "A receipt photo is required.");
        }

        var bytes = await ReadBoundedAsync(file, _options.MaxFileSizeBytes, cancellationToken);
        var detected = DetectImage(bytes);

        if (detected is null)
        {
            throw new ReceiptImageException(
                StatusCodes.Status400BadRequest,
                "Only JPG, PNG or WebP receipt photos are supported.");
        }

        var (contentType, extension) = detected.Value;
        var existing = await _context.ReceiptImages
            .FirstOrDefaultAsync(image => image.ReceiptId == receiptId, cancellationToken);

        await EnsureWithinQuotaAsync(userId, bytes.LongLength, existing?.Id, cancellationToken);

        var key = BuildObjectKey(userId, receiptId, extension);
        await StoreAsync(key, bytes, contentType, cancellationToken);

        var previousKey = existing?.ObjectKey;

        try
        {
            var row = existing ?? new ReceiptImage
            {
                ReceiptId = receiptId,
                CreatedBy = userId
            };

            row.ObjectKey = key;
            row.ContentType = contentType;
            row.SizeBytes = bytes.LongLength;
            row.UploadedAt = DateTime.UtcNow;

            if (existing is null)
            {
                _context.ReceiptImages.Add(row);
            }

            await _context.SaveChangesAsync(cancellationToken);

            if (previousKey is not null)
            {
                // The row already points at the new object, so a failure here only
                // leaves a stale object behind — log it, never fail the upload.
                await TryDeleteObjectAsync(previousKey, cancellationToken);
            }

            return ToDto(row);
        }
        catch (OperationCanceledException)
        {
            await TryDeleteObjectAsync(key, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            // No row means no reference: remove the object we just stored.
            _logger.LogError(exception, "Receipt photo {Key} stored but its row could not be saved; deleting the object", key);
            await TryDeleteObjectAsync(key, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(long receiptId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var image = await FindOwnedImageAsync(receiptId, cancellationToken);
        if (image is null)
        {
            return false;
        }

        try
        {
            await _storage.DeleteAsync(image.ObjectKey, cancellationToken);
        }
        catch (ReceiptStorageException exception)
        {
            _logger.LogError(exception, "Could not delete receipt photo {Key}; keeping its row", image.ObjectKey);
            throw new ReceiptImageException(
                StatusCodes.Status502BadGateway,
                "The receipt photo could not be removed. Please try again.");
        }

        _context.ReceiptImages.Remove(image);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private void EnsureConfigured()
    {
        if (!_storage.IsConfigured)
        {
            throw new ReceiptImageException(
                StatusCodes.Status503ServiceUnavailable,
                "Receipt photo storage is not configured.");
        }
    }

    private async Task<ReceiptImage?> FindOwnedImageAsync(long receiptId, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var owned = await _context.Receipts
            .AnyAsync(receipt => receipt.Id == receiptId && receipt.CreatedBy == userId, cancellationToken);

        if (!owned)
        {
            return null;
        }

        return await _context.ReceiptImages
            .FirstOrDefaultAsync(image => image.ReceiptId == receiptId, cancellationToken);
    }

    private async Task EnsureWithinQuotaAsync(
        Guid userId,
        long incomingBytes,
        long? replacingImageId,
        CancellationToken cancellationToken)
    {
        var used = _context.ReceiptImages.Where(image => image.CreatedBy == userId);

        // A replacement frees the bytes it supersedes, so it is not double counted.
        if (replacingImageId is { } imageId)
        {
            used = used.Where(image => image.Id != imageId);
        }

        var usedBytes = await used.SumAsync(image => image.SizeBytes, cancellationToken);

        if (usedBytes + incomingBytes <= _options.QuotaBytesPerUser)
        {
            return;
        }

        throw new ReceiptImageException(
            StatusCodes.Status422UnprocessableEntity,
            $"This photo would exceed your receipt-photo allowance of {FormatMegabytes(_options.QuotaBytesPerUser)}.",
            new
            {
                message = $"This photo would exceed your receipt-photo allowance of {FormatMegabytes(_options.QuotaBytesPerUser)}.",
                usedBytes,
                quotaBytes = _options.QuotaBytesPerUser
            });
    }

    private async Task StoreAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        await using var content = new MemoryStream(bytes, writable: false);

        try
        {
            await _storage.PutAsync(key, content, bytes.LongLength, contentType, cancellationToken);
        }
        catch (ReceiptStorageException exception)
        {
            throw new ReceiptImageException(
                StatusCodes.Status502BadGateway,
                "The receipt photo could not be stored. Please try again.",
                new { message = exception.Message });
        }
    }

    private async Task TryDeleteObjectAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(key, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not delete receipt photo {Key}", key);
        }
    }

    private async Task<byte[]> ReadBoundedAsync(
        IFormFile file,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferBytes];
        long total = 0;

        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;

            if (total > maxBytes)
            {
                throw new ReceiptImageException(
                    StatusCodes.Status413PayloadTooLarge,
                    $"A receipt photo must be {FormatMegabytes(maxBytes)} or smaller.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (total == 0)
        {
            throw new ReceiptImageException(StatusCodes.Status400BadRequest, "A receipt photo is required.");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Trusts bytes, not headers: the client's Content-Type and filename are
    /// ignored, and anything that is not a real JPEG/PNG/WebP is rejected.
    /// SVG is excluded on purpose — it is a script container, not a photo.
    /// </summary>
    private static (string ContentType, string Extension)? DetectImage(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ("image/jpeg", "jpg");
        }

        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return ("image/png", "png");
        }

        if (data.Length >= 12 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return ("image/webp", "webp");
        }

        return null;
    }

    /// <summary>
    /// A fresh GUID per upload: the key changes on replacement, so a browser or
    /// CDN cannot serve the previous photo from cache.
    /// </summary>
    private static string BuildObjectKey(Guid userId, long receiptId, string extension) =>
        $"receipts/{userId:D}/{receiptId}/{Guid.NewGuid():N}.{extension}";

    private TimeSpan ResolveTtl(int? ttlSeconds)
    {
        var seconds = ttlSeconds ?? _options.SignedUrlTtlSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, MinTtlSeconds, MaxTtlSeconds));
    }

    private static ReceiptImageDto ToDto(ReceiptImage image) => new()
    {
        Id = image.Id,
        ContentType = image.ContentType,
        SizeBytes = image.SizeBytes,
        UploadedAt = image.UploadedAt
    };

    private static string FormatMegabytes(long bytes)
    {
        var megabytes = bytes / (1024d * 1024d);
        return megabytes >= 1 ? $"{megabytes:0.#} MB" : $"{bytes} bytes";
    }
}
