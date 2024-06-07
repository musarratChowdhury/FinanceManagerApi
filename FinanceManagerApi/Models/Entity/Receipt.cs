namespace FinanceManagerApi.Models.Entity;

public class Receipt
{
    public long Id { get; set; }
    public double GrandTotal { get; set; }
    public int TotalItems { get; set; }
    public DateTime ExpenseDate { get; set; }
    
    public DateTime EntryDate { get; set; }
    public Guid CreatedBy {get ;set;}
    
    public List<Expense> Expenses = new List<Expense>();
}