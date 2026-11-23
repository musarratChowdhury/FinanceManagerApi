using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Models.Entity.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace FinanceManagerApi.DbContext;
using Microsoft.EntityFrameworkCore;

public class FinanceDbContext :  IdentityDbContext<UserProfile>
{
    public FinanceDbContext(DbContextOptions<FinanceDbContext> options) : base(options)
    {
    }
    
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<ExpenseCategory> ExpenseCategories { get; set; }
    public DbSet<IncomeCategory> IncomeCategories { get; set; }
    public DbSet<Income> Incomes { get; set; }
    public DbSet<Receipt> Receipts { get; set; }
    public DbSet<ReceiptImage> ReceiptImages { get; set; }
    public DbSet<Settings> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Expense>()
            .HasOne(e => e.ExpenseCategory) 
            .WithMany(ec => ec.Expenses) 
            .HasForeignKey(e => e.ExpenseCategoryId);
        
        modelBuilder.Entity<Income>()
            .HasOne(i => i.IncomeCategory)
            .WithMany(ic => ic.Incomes)
            .HasForeignKey(i => i.IncomeCategoryId);
        
        modelBuilder.Entity<Expense>()
            .HasOne(e => e.Receipt) 
            .WithMany(ec => ec.Expenses) 
            .HasForeignKey(e => e.ReceiptId);

        // One softcopy per receipt: the unique index is what makes "replace"
        // unambiguous, and it is the last line of defence against a double upload.
        modelBuilder.Entity<ReceiptImage>()
            .HasOne(image => image.Receipt)
            .WithMany(receipt => receipt.Images)
            .HasForeignKey(image => image.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReceiptImage>()
            .HasIndex(image => image.ReceiptId)
            .IsUnique();

        modelBuilder.Entity<ReceiptImage>()
            .HasIndex(image => image.ObjectKey)
            .IsUnique();

        modelBuilder.Entity<ReceiptImage>()
            .HasIndex(image => image.CreatedBy);

        // One row per setting key.
        modelBuilder.Entity<Settings>()
            .HasIndex(s => s.Key)
            .IsUnique();
        
        modelBuilder.Entity<IdentityUserLogin<string>>().HasNoKey();
        modelBuilder.Entity<IdentityUserRole<string>>().HasNoKey();
        modelBuilder.Entity<IdentityUserToken<string>>().HasNoKey();
        modelBuilder.Entity<IdentityPasskeyData>().HasNoKey();
        modelBuilder.Entity<IdentityUserPasskey<string>>().HasNoKey();
        modelBuilder.Entity<IdentityUserPasskey<string>>().Ignore(p => p.Data);
        
    }
}
