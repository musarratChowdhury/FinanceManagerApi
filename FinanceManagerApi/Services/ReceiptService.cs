using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IReceiptService
{
    Task<ReceiptDto?> GetReceiptByIdAsync(long id);
    Task<IEnumerable<ReceiptDto>> GetAllReceiptsAsync();

    /// <summary>
    /// Persists a receipt and its line items in a single save, so the expenses are
    /// created with this receipt's id as their foreign key. Returns the stored
    /// receipt, including the generated id.
    /// </summary>
    Task<ReceiptDto> CreateReceiptAsync(ReceiptDto receiptDto);
}

public class ReceiptService : IReceiptService
{
    private readonly ILogger<ReceiptService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Receipt> _receiptRepository;
    private readonly ICurrentUser _currentUser;

    public ReceiptService(
        ILogger<ReceiptService> logger,
        IMapper mapper,
        IGenericRepository<Receipt> receiptRepository,
        ICurrentUser currentUser)
    {
        _logger = logger;
        _mapper = mapper;
        _receiptRepository = receiptRepository;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<ReceiptDto>> GetAllReceiptsAsync()
    {
        try
        {
            // Scoped to the caller — an unscoped read leaks another account's rows —
            // and with the line items and softcopy loaded, so the ledger can expand a
            // receipt without a second round trip per row.
            var receipts = await _receiptRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId,
                "Expenses",
                "Images");

            return _mapper.Map<IEnumerable<ReceiptDto>>(receipts);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<ReceiptDto?> GetReceiptByIdAsync(long id)
    {
        try
        {
            // "exists but belongs to someone else" is a 404, not a 403, so the
            // endpoint cannot be used to probe which ids exist.
            var receipt = (await _receiptRepository.Filter(
                x => x.Id == id && x.CreatedBy == _currentUser.UserId,
                "Expenses",
                "Images")).FirstOrDefault();

            return receipt is null ? null : _mapper.Map<ReceiptDto>(receipt);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<ReceiptDto> CreateReceiptAsync(ReceiptDto receiptDto)
    {
        try
        {
            // Npgsql refuses a DateTime whose Kind is not Utc in a `timestamptz`
            // column, and a JSON value without an offset deserialises as
            // Unspecified. Pin every date we are about to write.
            PinDateKindsToUtc(receiptDto);

            var receipt = _mapper.Map<Receipt>(receiptDto);

            // The line items are the receipt's children: EF assigns the generated
            // id to every Expense.ReceiptId during this single save, which is what
            // makes the write atomic instead of one request per line item.
            if (receipt.TotalItems <= 0)
            {
                receipt.TotalItems = receipt.Expenses.Count;
            }

            await _receiptRepository.InsertAsync(receipt);

            _logger.LogInformation(
                "Receipt {ReceiptId} saved with {ItemCount} expense(s) totalling {NetPayable}",
                receipt.Id,
                receipt.Expenses.Count,
                receipt.NetPayable);

            return _mapper.Map<ReceiptDto>(receipt);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private static void PinDateKindsToUtc(ReceiptDto receiptDto)
    {
        receiptDto.ReceiptDateTime = AsUtc(receiptDto.ReceiptDateTime);
        receiptDto.EntryDate = AsUtc(receiptDto.EntryDate);

        foreach (var expense in receiptDto.Expenses)
        {
            expense.EntryDate = AsUtc(expense.EntryDate);
            if (expense.ExpenseDate.HasValue)
            {
                expense.ExpenseDate = AsUtc(expense.ExpenseDate.Value);
            }
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        // Unspecified: treat the wall-clock value as UTC rather than guessing an offset.
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
