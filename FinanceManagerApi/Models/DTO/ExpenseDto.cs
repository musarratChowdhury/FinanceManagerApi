namespace FinanceManagerApi.Models.DTO;

public class ExpenseDto
{
    public long Id {get; set;}
    public string Cause {get; set;} = string.Empty;
    public double Amount {get; set;}
    public int? Quantity { get; set; } = null;
    public double? UnitPrice { get; set; } = null;
    public DateTime? ExpenseDate { get; set; } = null;
    public DateTime EntryDate {get; set;} = DateTime.Now;
    public Guid CreatedBy {get ;set;}
    public long? ExpenseCategoryId { get; set; }
    public long? ReceiptId { get; set; } 
}