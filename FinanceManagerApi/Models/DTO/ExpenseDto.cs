namespace FinanceManagerApi.Models.DTO;

public class ExpenseDto
{
    public string Cause {get; set;} = string.Empty;
    public double Amount {get; set;}
    public DateTime EntryDate {get; set;} = DateTime.Now;
    public Guid CreatedBy {get ;set;}
    public long? ExpenseCategoryId { get; set; }
}