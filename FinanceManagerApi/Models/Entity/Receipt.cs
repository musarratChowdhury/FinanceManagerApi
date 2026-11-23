namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// A scanned receipt — the header of a purchase. One receipt owns many expenses
/// (its line items), and every expense points back through <see cref="Expense.ReceiptId"/>.
/// </summary>
public class Receipt
{
    public long Id { get; set; }

    /// <summary>Merchant / shop name as printed on the receipt.</summary>
    public string? Merchant { get; set; }

    /// <summary>
    /// Invoice / bill number. Optional: plenty of receipts never print one, so it
    /// stays null rather than being invented by the scan.
    /// </summary>
    public string? InvoiceNo { get; set; }

    /// <summary>Total before any discount is applied.</summary>
    public double GrossTotal { get; set; }

    /// <summary>Total discount applied, 0 when the receipt has none.</summary>
    public double Discount { get; set; }

    /// <summary>What was actually payable after the discount.</summary>
    public double NetPayable { get; set; }

    /// <summary>Number of line items on the receipt.</summary>
    public int TotalItems { get; set; }

    /// <summary>Date and time printed on the receipt.</summary>
    public DateTime ReceiptDateTime { get; set; }

    /// <summary>When this row was created — server owned.</summary>
    public DateTime EntryDate { get; set; }

    public Guid CreatedBy { get; set; }

    public List<Expense> Expenses { get; set; } = new();

    /// <summary>
    /// The receipt's softcopy. At most one row: the photo lives in the private
    /// receipt-photo bucket and this only points at it.
    /// </summary>
    public List<ReceiptImage> Images { get; set; } = new();
}
