namespace FinanceManagerApi.Models.DTO
{
    public class MonthlyExpenseDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthName { get; set; } = string.Empty;
        public double TotalExpense { get; set; }
    }
}
