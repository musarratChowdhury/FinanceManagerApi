namespace FinanceManagerApi.Models.DTO;

public class ReceiptDto
{
    public long Id { get; set; }

    public string? Merchant { get; set; }

    /// <summary>Optional invoice / bill number — null when the receipt has none.</summary>
    public string? InvoiceNo { get; set; }

    /// <summary>Total before discounts.</summary>
    public double GrossTotal { get; set; }

    /// <summary>Total discount applied.</summary>
    public double Discount { get; set; }

    /// <summary>Amount payable after discounts.</summary>
    public double NetPayable { get; set; }

    public int TotalItems { get; set; }

    /// <summary>Date and time printed on the receipt.</summary>
    public DateTime ReceiptDateTime { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime EntryDate { get; set; }

    /// <summary>The line items. Each one is persisted with this receipt's id as its FK.</summary>
    public List<ExpenseDto> Expenses { get; set; } = new();
}
