namespace FinanceManagerApi.Models.Entity;
public class Income
{
    public long Id { get; set; }
    public string Note { get; set; } = string.Empty;
    public double Amount { get; set; }
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public Guid CreatedBy { get; set; }

    public long? IncomeCategoryId { get; set; }
    public IncomeCategory IncomeCategory { get; set; }
}
