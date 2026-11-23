using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FinanceManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsAndGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetLimits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExpenseCategoryId = table.Column<long>(type: "bigint", nullable: false),
                    MonthlyLimit = table.Column<double>(type: "double precision", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetLimits_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsGoals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TargetAmount = table.Column<double>(type: "double precision", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoalContributions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SavingsGoalId = table.Column<long>(type: "bigint", nullable: false),
                    Amount = table.Column<double>(type: "double precision", nullable: false),
                    ContributionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoalContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoalContributions_SavingsGoals_SavingsGoalId",
                        column: x => x.SavingsGoalId,
                        principalTable: "SavingsGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLimits_CreatedBy_ExpenseCategoryId",
                table: "BudgetLimits",
                columns: new[] { "CreatedBy", "ExpenseCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetLimits_ExpenseCategoryId",
                table: "BudgetLimits",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_GoalContributions_SavingsGoalId",
                table: "GoalContributions",
                column: "SavingsGoalId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoals_CreatedBy",
                table: "SavingsGoals",
                column: "CreatedBy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BudgetLimits");

            migrationBuilder.DropTable(
                name: "GoalContributions");

            migrationBuilder.DropTable(
                name: "SavingsGoals");
        }
    }
}
