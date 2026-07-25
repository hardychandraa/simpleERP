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
        services.AddScoped<IExpenseCategoryRepository,  ExpenseCategoryRepository>();
        services.AddScoped<IExpenseRepository,          ExpenseRepository>();
        services.AddScoped<IUnitOfWork,                 UnitOfWork>();

        // Services
        services.AddScoped<InventoryService>();
        services.AddScoped<IInventoryService>(sp => sp.GetRequiredService<InventoryService>());
        services.AddScoped<IProductService,     ProductService>();
        services.AddScoped<ICustomerService,    CustomerService>();
        services.AddScoped<ISaleService,        SaleService>();
        services.AddScoped<IReportService,      ReportService>();
        services.AddScoped<IFinancialReportService, FinancialReportService>();
        services.AddScoped<IPaymentTermService,  PaymentTermService>();
        services.AddScoped<ISalesPersonService,  SalesPersonService>();
        services.AddScoped<ISupplierService,     SupplierService>();
        // PurchaseService chains a write into InventoryService inside one transaction,
        // so it depends on the concrete class — same pattern as SaleService above.
        services.AddScoped<IPurchaseService,     PurchaseService>();
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
