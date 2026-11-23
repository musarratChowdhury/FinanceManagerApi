using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Profiles;

namespace FinanceManagerApi.Moq;

public class ExpenseProfileTests
{
    private static IMapper NewMapper() =>
        new MapperConfiguration(configuration => configuration.AddProfile<ExpenseProfile>()).CreateMapper();

    [Fact]
    public void Receipt_WithAPhoto_MapsTheImagesAndTheHasImageFlag()
    {
        var uploadedAt = new DateTime(2026, 11, 23, 5, 0, 0, DateTimeKind.Utc);
        var receipt = new Receipt
        {
            Id = 7,
            Merchant = "Corner shop",
            Images =
            {
                new ReceiptImage
                {
                    Id = 1,
                    ReceiptId = 7,
                    ObjectKey = "receipts/x/7/abc.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 1234,
                    UploadedAt = uploadedAt
                }
            }
        };

        var dto = NewMapper().Map<ReceiptDto>(receipt);

        Assert.True(dto.HasImage);
        var image = Assert.Single(dto.Images);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal(1234, image.SizeBytes);
        Assert.Equal(uploadedAt, image.UploadedAt);
    }

    [Fact]
    public void Receipt_WithoutAPhoto_ReportsHasImageFalse()
    {
        var dto = NewMapper().Map<ReceiptDto>(new Receipt { Id = 8, Merchant = "Kiosk" });

        Assert.False(dto.HasImage);
        Assert.Empty(dto.Images);
    }

    [Fact]
    public void ReceiptDto_ImagesAreNotMappedBackOntoTheEntity()
    {
        // Only the upload endpoint may create image rows; a client that posts
        // images with a create must not get them persisted.
        var dto = new ReceiptDto
        {
            Merchant = "Kiosk",
            Images = { new ReceiptImageDto { Id = 99, ContentType = "image/png" } }
        };

        var entity = NewMapper().Map<Receipt>(dto);

        Assert.Empty(entity.Images);
    }
}
