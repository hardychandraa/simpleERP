using SimpleERP.Application.Interfaces;
using SimpleERP.Application.Services;
using SimpleERP.Domain.Interfaces;
using SimpleERP.Infrastructure.Data;
using SimpleERP.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SimpleERP.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

        // Repositories
        services.AddScoped<IBranchRepository,           BranchRepository>();
        services.AddScoped<IProductRepository,          ProductRepository>();
        services.AddScoped<ICustomerRepository,         CustomerRepository>();
        services.AddScoped<IInventoryLedgerRepository,  InventoryLedgerRepository>();
        services.AddScoped<ISaleRepository,             SaleRepository>();
        services.AddScoped<IPaymentRecordRepository,    PaymentRecordRepository>();
        services.AddScoped<IStockAdjustmentRepository,  StockAdjustmentRepository>();
        services.AddScoped<IAuditLogRepository,         AuditLogRepository>();
        services.AddScoped<IAppSettingsRepository,      AppSettingsRepository>();
        services.AddScoped<IPaymentTermRepository,      PaymentTermRepository>();
        services.AddScoped<ISalesPersonRepository,      SalesPersonRepository>();
        services.AddScoped<ISupplierRepository,         SupplierRepository>();
        services.AddScoped<IPurchaseRepository,         PurchaseRepository>();
        services.AddScoped<ISupplierPaymentRepository,  SupplierPaymentRepository>();
        services.AddScoped<IRebateRuleRepository,        RebateRuleRepository>();
        services.AddScoped<IRebateAccrualRepository,     RebateAccrualRepository>();
        services.AddScoped<IRebateRealizationRepository, RebateRealizationRepository>();
        services.AddScoped<ICommissionRuleRepository,    CommissionRuleRepository>();
        services.AddScoped<ICommissionAccrualRepository, CommissionAccrualRepository>();
        services.AddScoped<ICommissionPayoutRepository,  CommissionPayoutRepository>();
        services.AddScoped<ICustomerReturnRepository,    CustomerReturnRepository>();
        services.AddScoped<ISupplierReturnRepository,    SupplierReturnRepository>();
        services.AddScoped<ICreditNoteRepository,        CreditNoteRepository>();
        services.AddScoped<IPaymentBatchRepository,      PaymentBatchRepository>();
        services.AddScoped<IExpenseCategoryRepository,  ExpenseCategoryRepository>();
        services.AddScoped<IExpenseRepository,          ExpenseRepository>();
        services.AddScoped<IUnitOfWork,                 UnitOfWork>();

        // Services
        services.AddScoped<InventoryService>();
        services.AddScoped<IInventoryService>(sp => sp.GetRequiredService<InventoryService>());
        services.AddScoped<IProductService,     ProductService>();
        services.AddScoped<ICustomerService,    CustomerService>();
        // SaleService chains commission accrual into CommissionService within one
        // transaction, so it depends on the concrete class — dual-registered.
        services.AddScoped<CommissionService>();
        services.AddScoped<ICommissionService>(sp => sp.GetRequiredService<CommissionService>());
        services.AddScoped<ISaleService,        SaleService>();
        services.AddScoped<IReportService,      ReportService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IPaymentTermService,  PaymentTermService>();
        services.AddScoped<ISalesPersonService,  SalesPersonService>();
        services.AddScoped<ISupplierService,     SupplierService>();
        // RebateService chains writes into InventoryService (in-kind stock) within one
        // transaction, and PurchaseService chains into RebateService, so both are
        // registered by concrete type as well — same dual-registration pattern as
        // InventoryService above.
        services.AddScoped<RebateService>();
        services.AddScoped<IRebateService>(sp => sp.GetRequiredService<RebateService>());
        // PurchaseService chains writes into InventoryService and RebateService inside
        // one transaction, so it depends on those concrete classes.
        services.AddScoped<IPurchaseService,     PurchaseService>();
        // ReturnService chains stock movements into InventoryService inside one
        // transaction, so it takes the concrete class — same pattern as above. It raises
        // its own credit/debit notes straight through the repository rather than through
        // CreditNoteService, which keeps that service free of return-specific rules.
        services.AddScoped<IReturnService,       ReturnService>();
        services.AddScoped<ICreditNoteService,   CreditNoteService>();
        services.AddScoped<IExpenseService,      ExpenseService>();
        services.AddScoped<IAppSettingsService, AppSettingsService>();
        services.AddScoped<SimpleERP.Application.Services.AuditService>();
        services.AddScoped<IAuditService>(
            sp => sp.GetRequiredService<SimpleERP.Application.Services.AuditService>());

        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations at startup.
    /// Replaces the previous EnsureCreatedAsync(), which only ever created the schema
    /// on a brand-new database and silently no-opped against an existing one — meaning
    /// no schema change could ever reach a database that already had tables.
    /// </summary>
    public static async Task InitDatabaseAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
