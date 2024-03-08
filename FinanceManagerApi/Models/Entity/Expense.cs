namespace FinanceManagerApi.Models.Entity;

public class Expense
{
    public long Id {get; set;}
    public string Cause {get; set;} = string.Empty;
    public double Amount {get; set;}
    public DateTime EntryDate {get; set;} = DateTime.Now;
    public Guid CreatedBy {get ;set;}

    public long? ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }
}