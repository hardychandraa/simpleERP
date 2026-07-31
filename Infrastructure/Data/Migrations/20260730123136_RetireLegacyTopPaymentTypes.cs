using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SimpleERP.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Collapses the retired PaymentType.TOP30–TOP90 members (3–6) onto Due (2).
    ///
    /// No schema change: PaymentType is stored as an int, so retiring enum members is
    /// purely a data concern. Any row left holding 3–6 would read back as an undefined
    /// enum value and fall through every display switch to a bare number.
    ///
    /// The term itself is not lost — those rows already carry a PaymentTermId, which is
    /// now the only place a credit term is recorded. Verified before writing this: the one
    /// affected row (INV-202607-0003) held TOP30 alongside the "TOP 30" term, and its
    /// stored DueDate already matched SaleDate + the term's 30 days exactly, so the
    /// collapse changes no computed figure.
    /// </summary>
    public partial class RetireLegacyTopPaymentTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purchases is covered too. It never offered the TOP options in its UI, so it
            // should have no such rows — but this is the migration that makes "3–6 no
            // longer exist anywhere" true, and asserting it costs one statement.
            migrationBuilder.Sql(@"UPDATE ""Sales""     SET ""PaymentType"" = 2 WHERE ""PaymentType"" NOT IN (1, 2);");
            migrationBuilder.Sql(@"UPDATE ""Purchases"" SET ""PaymentType"" = 2 WHERE ""PaymentType"" NOT IN (1, 2);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Which of TOP30/45/60/90 a row held is not recoverable
            // from PaymentType alone, and it does not need to be: on the pre-retirement
            // code these rows still behave correctly, because a present PaymentTermId
            // always took precedence over the TOP day-count fallback.
        }
    }
}
