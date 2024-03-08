namespace FinanceManagerApi.Models.Entity;

public class IncomeCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public Guid CreatedBy { get; set; }

    public List<Income> Incomes { get; set; } = new List<Income>();
}