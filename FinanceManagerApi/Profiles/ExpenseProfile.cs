using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;

namespace FinanceManagerApi.Profiles;

public class ExpenseProfile : Profile
{
    public ExpenseProfile()
    {
        CreateMap<Expense, ExpenseDto>().ReverseMap();

        // Written as two explicit maps rather than ReverseMap because the receipt
        // carries two members that must not round-trip: Images (only the upload
        // endpoint may create rows) and HasImage (derived from Images).
        CreateMap<Receipt, ReceiptDto>()
            .ForMember(
                dto => dto.HasImage,
                options => options.MapFrom(receipt => receipt.Images != null && receipt.Images.Count > 0));

        CreateMap<ReceiptDto, Receipt>()
            .ForMember(receipt => receipt.Images, options => options.Ignore());

        CreateMap<ReceiptImage, ReceiptImageDto>();

        CreateMap<Income, IncomeDto>().ReverseMap();
    }
}
