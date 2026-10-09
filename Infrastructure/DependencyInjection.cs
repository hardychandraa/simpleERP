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
        services.AddDbContext<AppDbContext>(o => o
            // Single query is EF's default; stating it is what stops the "no QuerySplittingBehavior
            // configured" warning on every multi-Include load. Behaviour is unchanged.
            .UseNpgsql(connectionString, n => n.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
            // The report totals use GroupBy(_ => 1)...FirstOrDefault(): one group, so always one
            // row and nothing to order by. EF can't tell and warned on every report load, which
            // buried real warnings on Settings → Logs (2026-10-07).
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.FirstWithoutOrderByAndFilterWarning)));

        // Repositories
        services.AddScoped<IBranchRepository,           BranchRepository>();
        services.AddScoped<IProductRepository,          ProductRepository>();
        services.AddScoped<ICustomerRepository,         CustomerRepository>();
        services.AddScoped<IInventoryLedgerRepository,  InventoryLedgerRepository>();
        services.AddScoped<ICostSnapshotRepository,     CostSnapshotRepository>();
        services.AddScoped<ISaleRepository,             SaleRepository>();
        services.AddScoped<IPaymentRecordRepository,    PaymentRecordRepository>();
        services.AddScoped<IStockAdjustmentRepository,  StockAdjustmentRepository>();
        services.AddScoped<IAuditLogRepository,         AuditLogRepository>();
        // Reads only. Writes go through AppLogSink over raw Npgsql, outside DI scope —
        // see the note on IAppLogRepository.
        services.AddScoped<IAppLogRepository,           AppLogRepository>();
        services.AddScoped<IAppSettingsRepository,      AppSettingsRepository>();
        services.AddScoped<IPaymentTermRepository,      PaymentTermRepository>();
        services.AddScoped<ISalesPersonRepository,      SalesPersonRepository>();
        services.AddScoped<IUserRepository,             UserRepository>();
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
        services.AddScoped<ICreditNoteApplicationRepository, CreditNoteApplicationRepository>();
        services.AddScoped<IPaymentBatchRepository,      PaymentBatchRepository>();
        services.AddScoped<IExpenseCategoryRepository,  ExpenseCategoryRepository>();
        services.AddScoped<IExpenseRepository,          ExpenseRepository>();
        services.AddScoped<IUnitOfWork,                 UnitOfWork>();

        // Services
        services.AddScoped<PeriodLock>();
        services.AddScoped<PurchaseRecoster>();
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
        // Stateless and thread-safe; the hasher itself holds no per-request state.
        services.AddSingleton<IPasswordHasher, SimpleERP.Infrastructure.Security.PasswordHasherAdapter>();
        services.AddScoped<IAuthService,         AuthService>();
        services.AddScoped<IUserService,         UserService>();
        services.AddScoped<ISupplierService,     SupplierService>();
        // CreditNoteService is dual-registered because RebateService now chains a debit
        // note out of it (realizing a cash rebate) within one transaction — same pattern
        // as InventoryService below.
        services.AddScoped<CreditNoteService>();
        services.AddScoped<ICreditNoteService>(sp => sp.GetRequiredService<CreditNoteService>());
        // RebateService chains writes into InventoryService (in-kind stock) and
        // CreditNoteService (the debit note a cash settlement raises) within one
        // transaction, and PurchaseService chains into RebateService, so all three are
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
        services.AddScoped<IExpenseService,      ExpenseService>();
        services.AddScoped<IAppSettingsService, AppSettingsService>();
        services.AddScoped<SimpleERP.Application.Services.AuditService>();
        services.AddScoped<IAuditService>(
            sp => sp.GetRequiredService<SimpleERP.Application.Services.AuditService>());
        services.AddScoped<IAppLogService,       AppLogService>();

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
