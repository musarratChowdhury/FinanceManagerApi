namespace FinanceManagerApi.Models.DTO
{
	public class ExpenseCategoryGroupDto
	{
		public long CategoryId { get; set; }
		public string CategoryName { get; set; } = string.Empty;
		public IEnumerable<ExpenseDto> Expenses { get; set; } = Enumerable.Empty<ExpenseDto>();
		public double TotalAmount { get; set; } = 0;
	}
}
