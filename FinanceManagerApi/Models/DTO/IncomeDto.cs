using System.ComponentModel.DataAnnotations;

namespace FinanceManagerApi.Models.DTO;

public class IncomeDto
{
    public long Id { get; set; }
    public string Note { get; set; } = string.Empty;
    [Required] public double Amount { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public Guid CreatedBy { get; set; }
    public long? IncomeCategoryId { get; set; }
}