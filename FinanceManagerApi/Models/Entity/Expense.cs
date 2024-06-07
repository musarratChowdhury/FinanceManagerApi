namespace FinanceManagerApi.Models.Entity;

public class Expense
{
    public long Id {get; set;}
    public string Cause {get; set;} = string.Empty;
    public double Amount {get; set;}

    public int? Quantity { get; set; } = null;
    public double? UnitPrice { get; set; } = null;
    public DateTime? ExpenseDate { get; set; } = null;
    public DateTime EntryDate {get; set;} = DateTime.UtcNow;
    public Guid CreatedBy {get ;set;}

    public long? ReceiptId { get; set; }
    public Receipt? Receipt { get; set; }
    public long? ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }
}