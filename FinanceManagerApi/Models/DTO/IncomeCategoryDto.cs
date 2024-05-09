namespace FinanceManagerApi.Models.DTO;

public class IncomeCategoryDto
{
    public string Name { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; } = DateTime.Now;
    public Guid CreatedBy {get;set;}
}