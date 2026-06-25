using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Models.Entity.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManagerApi.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<UserProfile>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();

        try
        {
            await SeedAdminUserAsync(userManager);
            await SeedTestDataAsync(dbContext);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DbInitializer] Seeding failed: {ex.Message}");
        }
    }

    private static async Task SeedAdminUserAsync(UserManager<UserProfile> userManager)
    {
        const string adminEmail = "admin@gmail.com";

        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing != null)
        {
            Console.WriteLine($"[DbInitializer] Admin user '{adminEmail}' already exists - skipping.");
            return;
        }

        var admin = new UserProfile
        {
            UserName = adminEmail,
            Email = adminEmail,
            FirstName = "Admin",
            LastName = "User",
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(admin, "123456");
        if (result.Succeeded)
        {
            Console.WriteLine($"[DbInitializer] Created admin user '{adminEmail}'.");
        }
        else
        {
            Console.Error.WriteLine($"[DbInitializer] Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
    }

    private static async Task SeedTestDataAsync(FinanceDbContext dbContext)
    {
        if (await dbContext.ExpenseCategories.AnyAsync())
        {
            Console.WriteLine("[DbInitializer] ExpenseCategories already contain data - skipping test data seed.");
            return;
        }

        var createdBy = Guid.Empty;

        var shopping = new ExpenseCategory
        {
            Name = "shopping",
            EntryDate = new DateTime(2024, 5, 6, 4, 56, 44, 340, DateTimeKind.Utc),
            CreatedBy = createdBy
        };
        var education = new ExpenseCategory
        {
            Name = "Education",
            EntryDate = new DateTime(2024, 5, 6, 5, 6, 25, 755, DateTimeKind.Utc),
            CreatedBy = createdBy
        };
        var medical = new ExpenseCategory
        {
            Name = "Medical",
            EntryDate = new DateTime(2024, 5, 6, 5, 6, 25, 755, DateTimeKind.Utc),
            CreatedBy = createdBy
        };
        var entertainment = new ExpenseCategory
        {
            Name = "Entertainment",
            EntryDate = new DateTime(2024, 5, 6, 5, 6, 25, 755, DateTimeKind.Utc),
            CreatedBy = createdBy
        };

        var expenseDate1 = new DateTime(2024, 5, 6, 5, 2, 44, 259, DateTimeKind.Utc);
        var expenseDate2 = new DateTime(2024, 5, 6, 5, 6, 17, 620, DateTimeKind.Utc);

        var expenses = new[]
        {
            new Expense { Cause = "buy iron for ", Amount = 1000, ExpenseCategory = shopping, EntryDate = expenseDate1, CreatedBy = createdBy },
            new Expense { Cause = "buy 3 piece for wife", Amount = 700, ExpenseCategory = shopping, EntryDate = expenseDate1, CreatedBy = createdBy },
            new Expense { Cause = "Bought medicine", Amount = 1000, ExpenseCategory = medical, EntryDate = expenseDate2, CreatedBy = createdBy },
            new Expense { Cause = "Bought Game Console", Amount = 1000, ExpenseCategory = entertainment, EntryDate = expenseDate2, CreatedBy = createdBy }
        };

        await dbContext.ExpenseCategories.AddRangeAsync(shopping, education, medical, entertainment);
        await dbContext.Expenses.AddRangeAsync(expenses);
        await dbContext.SaveChangesAsync();

        Console.WriteLine("[DbInitializer] Seeded 4 ExpenseCategories and 4 Expenses.");
    }
}
