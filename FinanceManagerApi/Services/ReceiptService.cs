using System.Security.Claims;
using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IReceiptService
{
    Task<ReceiptDto> GetReceiptByIdAsync(long id);
    Task<IEnumerable<ReceiptDto>> GetAllReceiptsAsync();
    Task CreateReceiptAsync(ReceiptDto receiptDto);
}

public class ReceiptService : IReceiptService
{
    private readonly ILogger<ReceiptService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Receipt> _receiptRepository;
    public ReceiptService(ILogger<ReceiptService> logger, IMapper mapper, IGenericRepository<Receipt> receiptRepository)
    {
        _logger = logger;
        _mapper = mapper;
        _receiptRepository = receiptRepository;
    }
    
    public async Task<IEnumerable<ReceiptDto>> GetAllReceiptsAsync()
    {
        try
        {
            var receipts = await _receiptRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<ReceiptDto>>(receipts);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<ReceiptDto> GetReceiptByIdAsync(long id)
    {
        try
        {
            var receipts = await _receiptRepository.GetByIdAsync(id);
            return _mapper.Map<ReceiptDto>(receipts);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task CreateReceiptAsync(ReceiptDto receiptDto)
    {
        try
        { 
            var receipt = _mapper.Map<Receipt>(receiptDto);
            await _receiptRepository.InsertAsync(receipt);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}