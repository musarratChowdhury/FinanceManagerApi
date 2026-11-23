using System.Text;
using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Models.Errors;
using FinanceManagerApi.Options;
using FinanceManagerApi.Services;
using FinanceManagerApi.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace FinanceManagerApi.Moq;

public class ReceiptImageServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly byte[] JpegBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };

    private const long DefaultQuota = 200L * 1024 * 1024;

    private readonly FakeReceiptStorage _storage = new();

    /* ---------------------------------------------------------------- upload */

    [Fact]
    public async Task UploadAsync_StoresPhotoAndReturnsMetadata()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var service = NewService(context);

        var dto = await service.UploadAsync(receipt.Id, JpegFile(JpegBytes));

        Assert.Equal("image/jpeg", dto.ContentType);
        Assert.Equal(JpegBytes.Length, dto.SizeBytes);
        Assert.NotNull(_storage.LastStored);
        // Server-built key: per-user, per-receipt, random name, extension from the bytes.
        Assert.Matches($@"^receipts/{OwnerId:D}/{receipt.Id}/[0-9a-f]{{32}}\.jpg$", _storage.LastStored!.Key);
        Assert.Equal(JpegBytes, _storage.LastStored.Bytes);

        var row = await context.ReceiptImages.SingleAsync();
        Assert.Equal(receipt.Id, row.ReceiptId);
        Assert.Equal(OwnerId, row.CreatedBy);
        Assert.Equal(JpegBytes.Length, row.SizeBytes);
    }

    [Fact]
    public async Task UploadAsync_DetectsPngAndWebpFromBytes()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var service = NewService(context);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 };
        // Claimed as JPEG; the bytes win.
        var pngDto = await service.UploadAsync(receipt.Id, JpegFile(png, "image/jpeg"));
        Assert.Equal("image/png", pngDto.ContentType);
        Assert.EndsWith(".png", _storage.LastStored!.Key);

        var webp = Encoding.ASCII.GetBytes("RIFF0000WEBPdata");
        var webpDto = await service.UploadAsync(receipt.Id, JpegFile(webp, "application/pdf"));
        Assert.Equal("image/webp", webpDto.ContentType);
        Assert.EndsWith(".webp", _storage.LastStored!.Key);
    }

    [Theory]
    [InlineData("text/plain", "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("image/png", "%PDF-1.7 receipt")]
    [InlineData("image/jpeg", "not an image at all")]
    public async Task UploadAsync_RejectsBytesThatAreNotJpegPngOrWebp(string claimedType, string body)
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var service = NewService(context);

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => service.UploadAsync(receipt.Id, JpegFile(Encoding.UTF8.GetBytes(body), claimedType)));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Empty(_storage.Stored);
        Assert.Empty(context.ReceiptImages);
    }

    [Fact]
    public async Task UploadAsync_RejectsFilesOverTheConfiguredLimit()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var service = NewService(context, maxFileSizeBytes: 4);

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => service.UploadAsync(receipt.Id, JpegFile(JpegBytes)));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
        Assert.Empty(_storage.Stored);
    }

    [Fact]
    public async Task UploadAsync_Returns404ForAnotherUsersReceipt()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OtherId);
        var service = NewService(context);

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => service.UploadAsync(receipt.Id, JpegFile(JpegBytes)));

        // A 404, not a 403: the caller must not learn that the id exists.
        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Empty(_storage.Stored);
    }

    [Fact]
    public async Task UploadAsync_EnforcesThePerUserQuota()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var otherReceipt = await SeedReceiptAsync(context, OwnerId);
        await SeedImageAsync(context, otherReceipt.Id, OwnerId, sizeBytes: 95);

        var service = NewService(context, quotaBytes: 100);

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => service.UploadAsync(receipt.Id, JpegFile(JpegBytes)));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Empty(_storage.Stored);

        // The payload carries the numbers the UI needs to explain the refusal.
        Assert.NotNull(exception.Payload);
        var payload = exception.Payload!;
        var usedBytes = payload.GetType().GetProperty("usedBytes")!.GetValue(payload);
        Assert.Equal(95L, usedBytes);
    }

    [Fact]
    public async Task UploadAsync_ReplacesTheExistingPhotoAndDeletesTheOldObject()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var existing = await SeedImageAsync(context, receipt.Id, OwnerId, sizeBytes: 50);

        // The seeded row is the instance the service will update, so the old key
        // has to be captured before the upload replaces it.
        var oldKey = existing.ObjectKey;

        // The superseded bytes no longer count against the quota.
        var service = NewService(context, quotaBytes: 100);
        await service.UploadAsync(receipt.Id, JpegFile(JpegBytes));

        var row = await context.ReceiptImages.SingleAsync();
        Assert.Equal(receipt.Id, row.ReceiptId);
        Assert.NotEqual(oldKey, row.ObjectKey);
        Assert.Contains(oldKey, _storage.DeletedKeys);
        Assert.Equal(_storage.LastStored!.Key, row.ObjectKey);
    }

    [Fact]
    public async Task UploadAsync_DeletesTheStoredObjectWhenTheRowCannotBeSaved()
    {
        await using var context = new FailingContext(NewDbOptions());
        var receipt = await SeedReceiptAsync(context, OwnerId);
        context.FailOnSave = true;
        var service = NewService(context);

        await Assert.ThrowsAnyAsync<Exception>(() => service.UploadAsync(receipt.Id, JpegFile(JpegBytes)));

        // No row means no reference: the object must not survive.
        Assert.NotNull(_storage.LastStored);
        Assert.Contains(_storage.LastStored!.Key, _storage.DeletedKeys);
    }

    [Fact]
    public async Task UploadAsync_Returns503WhenStorageIsNotConfigured()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        _storage.IsConfigured = false;
        var service = NewService(context);

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => service.UploadAsync(receipt.Id, JpegFile(JpegBytes)));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Empty(_storage.Stored);
    }

    /* ------------------------------------------------------------------- read */

    [Fact]
    public async Task GetAsync_ReturnsMetadataOnlyForTheOwner()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        await SeedImageAsync(context, receipt.Id, OwnerId, sizeBytes: 10);

        var service = NewService(context);

        var dto = await service.GetAsync(receipt.Id);
        Assert.NotNull(dto);
        Assert.Equal(10, dto!.SizeBytes);

        Assert.Null(await service.GetAsync(receipt.Id + 1));

        var foreign = await SeedReceiptAsync(context, OtherId);
        await SeedImageAsync(context, foreign.Id, OtherId, sizeBytes: 10);
        Assert.Null(await service.GetAsync(foreign.Id));
    }

    [Fact]
    public async Task GetUrlAsync_ClampsTheTtlAndReturns404WithoutAPhoto()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        await SeedImageAsync(context, receipt.Id, OwnerId, sizeBytes: 10);

        var service = NewService(context);

        var tooShort = await service.GetUrlAsync(receipt.Id, ttlSeconds: 5);
        Assert.Equal(TimeSpan.FromSeconds(60), _storage.LastSignedTtl);
        Assert.NotNull(tooShort);
        Assert.StartsWith("https://storage.test/", tooShort!.Url);

        await service.GetUrlAsync(receipt.Id, ttlSeconds: 999_999);
        Assert.Equal(TimeSpan.FromSeconds(3600), _storage.LastSignedTtl);

        await service.GetUrlAsync(receipt.Id);
        Assert.Equal(TimeSpan.FromSeconds(300), _storage.LastSignedTtl);

        var photoLess = await SeedReceiptAsync(context, OwnerId);
        Assert.Null(await service.GetUrlAsync(photoLess.Id));
    }

    /* ----------------------------------------------------------------- delete */

    [Fact]
    public async Task DeleteAsync_RemovesTheObjectAndThenTheRow()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        var image = await SeedImageAsync(context, receipt.Id, OwnerId, sizeBytes: 10);

        var removed = await NewService(context).DeleteAsync(receipt.Id);

        Assert.True(removed);
        Assert.Contains(image.ObjectKey, _storage.DeletedKeys);
        Assert.Empty(context.ReceiptImages);
    }

    [Fact]
    public async Task DeleteAsync_KeepsTheRowWhenTheStoreFails()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);
        await SeedImageAsync(context, receipt.Id, OwnerId, sizeBytes: 10);
        _storage.DeleteFailure = new ReceiptStorageException("boom", new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<ReceiptImageException>(
            () => NewService(context).DeleteAsync(receipt.Id));

        Assert.Equal(StatusCodes.Status502BadGateway, exception.StatusCode);
        // The row survives, so the delete can be retried instead of orphaning bytes.
        Assert.Single(context.ReceiptImages);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenThereIsNoPhoto()
    {
        await using var context = NewContext();
        var receipt = await SeedReceiptAsync(context, OwnerId);

        Assert.False(await NewService(context).DeleteAsync(receipt.Id));
    }

    /* ---------------------------------------------------------------- helpers */

    private static B2Options NewOptions(
        long maxFileSizeBytes = 5 * 1024 * 1024,
        long quotaBytes = DefaultQuota) => new()
    {
        ServiceUrl = "https://s3.us-west-004.backblazeb2.com",
        Region = "us-west-004",
        KeyId = "key-id",
        AppKey = "app-key",
        Bucket = "receipts",
        MaxFileSizeBytes = maxFileSizeBytes,
        QuotaBytesPerUser = quotaBytes,
        SignedUrlTtlSeconds = 300
    };

    private ReceiptImageService NewService(
        FinanceDbContext context,
        long maxFileSizeBytes = 5 * 1024 * 1024,
        long quotaBytes = DefaultQuota) =>
        new(
            context,
            _storage,
            new StubCurrentUser(OwnerId),
            MsOptions.Create(NewOptions(maxFileSizeBytes, quotaBytes)),
            NullLogger<ReceiptImageService>.Instance);

    private static FinanceDbContext NewContext() => new(NewDbOptions());

    private static DbContextOptions<FinanceDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static async Task<Receipt> SeedReceiptAsync(FinanceDbContext context, Guid owner)
    {
        var receipt = new Receipt
        {
            Merchant = "Corner shop",
            ReceiptDateTime = DateTime.UtcNow,
            EntryDate = DateTime.UtcNow,
            CreatedBy = owner
        };

        context.Receipts.Add(receipt);
        await context.SaveChangesAsync();
        return receipt;
    }

    private static async Task<ReceiptImage> SeedImageAsync(
        FinanceDbContext context,
        long receiptId,
        Guid owner,
        long sizeBytes)
    {
        var image = new ReceiptImage
        {
            ReceiptId = receiptId,
            ObjectKey = $"receipts/{owner:D}/{receiptId}/{Guid.NewGuid():N}.jpg",
            ContentType = "image/jpeg",
            SizeBytes = sizeBytes,
            UploadedAt = DateTime.UtcNow,
            CreatedBy = owner
        };

        context.ReceiptImages.Add(image);
        await context.SaveChangesAsync();
        return image;
    }

    private static IFormFile JpegFile(byte[] bytes, string contentType = "image/jpeg")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "receipt.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId) => UserId = userId;

        public Guid UserId { get; }

        public bool IsAuthenticated => true;
    }

    /// <summary>Fails the row write so the object-cleanup path can be asserted.</summary>
    private sealed class FailingContext : FinanceDbContext
    {
        public FailingContext(DbContextOptions<FinanceDbContext> options)
            : base(options)
        {
        }

        /// <summary>Seeding runs with this false; the upload under test flips it on.</summary>
        public bool FailOnSave { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            FailOnSave
                ? throw new IOException("the database is unavailable")
                : base.SaveChangesAsync(cancellationToken);
    }
}
