namespace FinanceManagerApi.Models.DTO;

public class ExpenseDto
{
    public long Id {get; set;}
    public string Cause {get; set;} = string.Empty;
    public double Amount {get; set;}
    /// <summary>Fractional quantities are real (0.5 kg, 0.3 l), so this is not an int.</summary>
    public double? Quantity { get; set; } = null;
    public double? UnitPrice { get; set; } = null;
    public DateTime? ExpenseDate { get; set; } = null;
    public DateTime EntryDate {get; set;} = DateTime.Now;
    public Guid CreatedBy {get ;set;}
    public long? ExpenseCategoryId { get; set; }
    public long? ReceiptId { get; set; } 
}