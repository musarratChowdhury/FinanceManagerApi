using Microsoft.Build.Framework;

namespace FinanceManagerApi.Models.DTO;

public class ReceiptDto
{
    public long Id { get; set; }
    [Required]
    public double GrandTotal { get; set; }
    [Required]
    public int TotalItems { get; set; }
    [Required]
    public DateTime ExpenseDate { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime EntryDate { get; set; }
    [Required]
    public List<ExpenseDto> Expenses { get; set; } = new List<ExpenseDto>();
}