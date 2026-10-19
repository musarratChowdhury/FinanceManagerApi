using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IIncomeService
{
    Task<IEnumerable<IncomeDto>> GetAllIncomesAsync();
    Task<IncomeDto?> GetIncomeByIdAsync(long id);
    Task<IEnumerable<IncomeDto>> GetIncomesOfThisMonthAsync();
    Task<IEnumerable<IncomeDto>> GetIncomesOfAMonthAsync(int monthNo, int year);
    Task<IEnumerable<IncomeDto>> GetIncomesOfADayAsync(DateTime day);
    Task<double> GetTotalAmountIncomesOfACategoryAsync(long categoryId);
    Task CreateIncomeAsync(IncomeDto IncomeDto);
    Task<bool> UpdateIncomeAsync(long id, IncomeDto IncomeDto);
    Task<bool> DeleteIncomeAsync(long id);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryAsync(long categoryId);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryThisMonthAsync(long categoryId);
    Task<IEnumerable<IncomeDto>> GetIncomesOfACategoryADayAsync(long categoryId, DateTime day);
}

// Incomes are private to the user who entered them: every query is scoped to CreatedBy == the
// caller, so a new read that omits "x.CreatedBy == _currentUser.UserId" leaks another account's rows.
public class IncomeService : IIncomeService
{
    private readonly ILogger<IncomeService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Income> _IncomeRepository;
    private readonly ICurrentUser _currentUser;

    public IncomeService(ILogger<IncomeService> logger, IMapper mapper, IGenericRepository<Income> IncomeRepository, ICurrentUser currentUser)
    {
        _logger = logger;
        _mapper = mapper;
        _IncomeRepository = IncomeRepository;
        _currentUser = currentUser;
    }
    
    public async Task<IEnumerable<IncomeDto>> GetAllIncomesAsync()
    {
        try
        {
            var Incomes = await _IncomeRepository.Filter(x => x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IncomeDto?> GetIncomeByIdAsync(long id)
    {
        try
        {
            var Income = (await _IncomeRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();
            return Income is null ? null : _mapper.Map<IncomeDto>(Income);
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
                                                                x.EntryDate <= lastDayOfMonth &&
                                                                x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<IEnumerable<IncomeDto>> GetIncomesOfAMonthAsync(int monthNo, int year)
    {
        if (monthNo < 1 || monthNo > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(monthNo), "Month number must be between 1 and 12.");
        }

        var currentYear = DateTime.UtcNow.Year;
        if (year < 1 || year > currentYear + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), $"Year must be between 1 and {currentYear + 1}.");
        }

        try
        {
            var firstDayOfMonth = new DateTime(year, monthNo, 1, 0, 0, 0, DateTimeKind.Utc);
            var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);
            var incomes = await _IncomeRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                               x.EntryDate < firstDayOfNextMonth &&
                                                               x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<IncomeDto>>(incomes);
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
            var Incomes = await _IncomeRepository.Filter(x => x.EntryDate == day &&
                                                                x.CreatedBy == _currentUser.UserId);
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
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId &&
                                                                x.CreatedBy == _currentUser.UserId);
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
            // Server-owned: whatever the payload claimed, the row belongs to the caller.
            Income.CreatedBy = _currentUser.UserId;
            await _IncomeRepository.InsertAsync(Income);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    
    
    public async Task<bool> UpdateIncomeAsync(long id, IncomeDto IncomeDto)
    {
        try
        {
            var Income = (await _IncomeRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();

            if (Income is null)
            {
                return false;
            }

            // Only fields the caller owns the value of are copied. CreatedBy and EntryDate stay
            // server-owned: letting them through would erase ownership (the row would disappear
            // from the owner's own lists).
            Income.Note = IncomeDto.Note;
            Income.Amount = IncomeDto.Amount;
            Income.IncomeCategoryId = IncomeDto.IncomeCategoryId;

            await _IncomeRepository.UpdateAsync(Income);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<bool> DeleteIncomeAsync(long id)
    {
        try
        {
            var Income = (await _IncomeRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();

            if (Income is null)
            {
                return false;
            }

            await _IncomeRepository.DeleteAsync(id);
            return true;
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
            var Incomes = await _IncomeRepository.Filter(x => x.IncomeCategoryId == categoryId &&
                                                                x.CreatedBy == _currentUser.UserId);
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
                                                                x.EntryDate <= lastDayOfMonth &&
                                                                x.CreatedBy == _currentUser.UserId);
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
                                                                x.EntryDate == day &&
                                                                x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<IncomeDto>>(Incomes);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
}
