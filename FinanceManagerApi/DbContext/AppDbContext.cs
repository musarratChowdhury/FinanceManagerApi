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
        
        modelBuilder.Entity<IdentityUserLogin<string>>().HasNoKey();
        modelBuilder.Entity<IdentityUserRole<string>>().HasNoKey();
        modelBuilder.Entity<IdentityUserToken<string>>().HasNoKey();
        
    }
}
