using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IIncomeService
{
    Task<IEnumerable<IncomeDto>> GetAllIncomesAsync();
    Task<IncomeDto> GetIncomeByIdAsync(long id);
    Task<IEnumerable<IncomeDto>> GetIncomesOfThisMonthAsync();
    Task<IEnumerable<IncomeDto>> GetIncomesOfADayAsync(DateTime day);
    Task<double> GetTotalAmountIncomesOfACategoryAsync(long categoryId);
    Task CreateIncomeAsync(IncomeDto IncomeDto);
    Task UpdateIncomeAsync(long id, IncomeDto IncomeDto);
    Task DeleteIncomeAsync(long id);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryAsync(long categoryId);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryThisMonthAsync(long categoryId);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryADayAsync(long categoryId, DateTime day);
}

public class IncomeService : IIncomeService
{
    private readonly ILogger<IncomeService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Income> _IncomeRepository;
    
    public IncomeService(ILogger<IncomeService> logger, IMapper mapper, IGenericRepository<Income> IncomeRepository)
    {
        _logger = logger;
        _mapper = mapper;
        _IncomeRepository = IncomeRepository;
    }
    
    public async Task<IEnumerable<IncomeDto>> GetAllIncomesAsync()
    {
        try
        {
            var Incomes = await _IncomeRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IncomeDto> GetIncomeByIdAsync(long id)
    {
        try
        {
            var Income = await _IncomeRepository.GetByIdAsync(id);
            return _mapper.Map<IncomeDto>(Income);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<IncomeDto>> GetIncomesOfThisMonthAsync()
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var Incomes = await _IncomeRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<IncomeDto>> GetIncomesOfADayAsync(DateTime day)
    {
        try
        {
            var Incomes = await _IncomeRepository.Filter(x => x.EntryDate == day);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<double> GetTotalAmountIncomesOfACategoryAsync(long categoryId)
    {
        try
        {
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId);
            return Incomes.Sum(x => x.Amount);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task CreateIncomeAsync(IncomeDto IncomeDto)
    {
        try
        {
            var Income = _mapper.Map<Income>(IncomeDto);
            await _IncomeRepository.InsertAsync(Income);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    
    
    public async Task UpdateIncomeAsync(long id, IncomeDto IncomeDto)
    {
        try
        {
            var Income = _mapper.Map<Income>(IncomeDto);
            Income.Id = id;
            await _IncomeRepository.UpdateAsync(Income);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task DeleteIncomeAsync(long id)
    {
        try
        {
            await _IncomeRepository.DeleteAsync(id);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryAsync(long categoryId)
    {
        try
        {
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    public async Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryThisMonthAsync(long categoryId)
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId &&
                                                                x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryADayAsync(long categoryId, DateTime day)
    {
        try
        {
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId &&
                                                                x.EntryDate == day);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
}