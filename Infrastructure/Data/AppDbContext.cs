using SimpleERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SimpleERP.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Branch>          Branches          => Set<Branch>();
    public DbSet<Product>         Products          => Set<Product>();
    public DbSet<Customer>        Customers         => Set<Customer>();
    public DbSet<InventoryLedger> InventoryLedgers  => Set<InventoryLedger>();
    public DbSet<Sale>            Sales             => Set<Sale>();
    public DbSet<SaleItem>        SaleItems         => Set<SaleItem>();
    public DbSet<PaymentRecord>   PaymentRecords    => Set<PaymentRecord>();
    public DbSet<StockAdjustment> StockAdjustments  => Set<StockAdjustment>();
    public DbSet<AuditLog>        AuditLogs         => Set<AuditLog>();
    public DbSet<AppSettings>     AppSettings       => Set<AppSettings>();
    public DbSet<PaymentTerm>     PaymentTerms      => Set<PaymentTerm>();
    public DbSet<SalesPerson>     SalesPersons      => Set<SalesPerson>();
    public DbSet<Supplier>        Suppliers         => Set<Supplier>();
    public DbSet<Purchase>        Purchases         => Set<Purchase>();
    public DbSet<PurchaseItem>    PurchaseItems     => Set<PurchaseItem>();
    public DbSet<SupplierPayment> SupplierPayments  => Set<SupplierPayment>();
    public DbSet<RebateRule>        RebateRules        => Set<RebateRule>();
    public DbSet<RebateAccrual>     RebateAccruals     => Set<RebateAccrual>();
    public DbSet<RebateRealization> RebateRealizations => Set<RebateRealization>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense>         Expenses          => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        base.OnModelCreating(m);

        m.Entity<Branch>(e => { e.HasKey(b => b.Id); e.Property(b => b.Name).IsRequired().HasMaxLength(200); });

        m.Entity<Product>(e => {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(300);
            e.Property(p => p.SKU).IsRequired().HasMaxLength(100);
            e.Property(p => p.UnitPrice).HasColumnType("decimal(18,4)");
            e.HasIndex(p => p.SKU).IsUnique();
        });

        m.Entity<Customer>(e => {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(300);
            e.Property(c => c.Phone).HasMaxLength(50);
            e.Property(c => c.Address).HasMaxLength(500);
        });

        m.Entity<InventoryLedger>(e => {
            e.HasKey(l => l.Id);
            e.Property(l => l.QtyIn).HasColumnType("decimal(18,4)");
            e.Property(l => l.QtyOut).HasColumnType("decimal(18,4)");
            e.Property(l => l.UnitCost).HasColumnType("decimal(18,4)");
            e.Property(l => l.TotalCost).HasColumnType("decimal(18,4)");
            e.HasOne(l => l.Product).WithMany(p => p.InventoryLedgers)
                .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Branch).WithMany()
                .HasForeignKey(l => l.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Sale>(e => {
            e.HasKey(s => s.Id);
            e.Property(s => s.InvoiceNumber).IsRequired().HasMaxLength(50);
            e.Property(s => s.SubTotal).HasColumnType("decimal(18,4)");
            e.Property(s => s.DiscountTotal).HasColumnType("decimal(18,4)");
            e.Property(s => s.InvoiceDiscountAmount).HasColumnType("decimal(18,4)");
            e.Property(s => s.InvoiceDiscountPercent).HasColumnType("decimal(18,4)");
            e.Property(s => s.TaxBase).HasColumnType("decimal(18,4)");
            e.Property(s => s.TaxRate).HasColumnType("decimal(18,4)");
            e.Property(s => s.TaxAmount).HasColumnType("decimal(18,4)");
            e.Property(s => s.GrandTotal).HasColumnType("decimal(18,4)");
            e.Property(s => s.AmountPaid).HasColumnType("decimal(18,4)");
            e.Property(s => s.CreatedBy).HasMaxLength(100);
            e.Property(s => s.Notes).HasMaxLength(500);
            // DueDate: from PaymentTerm.DueDays; null for Cash and open credit
            e.Property(s => s.DueDate).IsRequired(false);
            e.HasOne(s => s.Customer).WithMany(c => c.Sales)
                .HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(s => s.Branch).WithMany()
                .HasForeignKey(s => s.BranchId).OnDelete(DeleteBehavior.Restrict);
            // Restrict: a term that has been used on a posted sale must not be
            // deletable — deactivate it instead, so history stays readable.
            e.HasOne(s => s.PaymentTerm).WithMany()
                .HasForeignKey(s => s.PaymentTermId).OnDelete(DeleteBehavior.Restrict);
            // Restrict for the same reason: a person credited with a posted sale must
            // stay resolvable — deactivate instead of deleting.
            e.HasOne(s => s.SalesPerson).WithMany()
                .HasForeignKey(s => s.SalesPersonId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.InvoiceNumber).IsUnique();
            // Commission (Step 7) settles per salesperson over a period.
            e.HasIndex(s => s.SalesPersonId);
            e.HasIndex(s => s.SaleDate);
            e.HasIndex(s => s.DueDate);  // for aging queries
        });

        m.Entity<SaleItem>(e => {
            e.HasKey(i => i.Id);
            e.Property(i => i.Qty).HasColumnType("decimal(18,4)");
            e.Property(i => i.UnitPrice).HasColumnType("decimal(18,4)");
            e.Property(i => i.DiscountAmount).HasColumnType("decimal(18,4)");
            e.Property(i => i.DiscountPercent).HasColumnType("decimal(18,4)");
            e.Property(i => i.AllocatedInvoiceDiscount).HasColumnType("decimal(18,4)");
            e.Property(i => i.LineTotal).HasColumnType("decimal(18,4)");
            e.Property(i => i.CostAtSale).HasColumnType("decimal(18,4)");
            e.Property(i => i.Notes).HasMaxLength(500);
            e.Property(i => i.PriceReason).HasMaxLength(300);
            e.HasOne(i => i.Sale).WithMany(s => s.SaleItems)
                .HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Product).WithMany()
                .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<PaymentRecord>(e => {
            e.HasKey(p => p.Id);
            e.Property(p => p.Amount).HasColumnType("decimal(18,4)");
            e.Property(p => p.Notes).HasMaxLength(300);
            e.Property(p => p.CreatedBy).HasMaxLength(100);
            e.HasOne(p => p.Sale).WithMany(s => s.PaymentRecords)
                .HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.SaleId);
        });

        m.Entity<StockAdjustment>(e => {
            e.HasKey(a => a.Id);
            e.Property(a => a.QtyBefore).HasColumnType("decimal(18,4)");
            e.Property(a => a.QtyAfter).HasColumnType("decimal(18,4)");
            e.Property(a => a.Reason).IsRequired().HasMaxLength(500);
            e.Property(a => a.CreatedBy).HasMaxLength(100);
            e.HasOne(a => a.Product).WithMany()
                .HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(a => a.QtyDelta);
        });

        m.Entity<AuditLog>(e => {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).ValueGeneratedOnAdd();
            e.Property(l => l.User).HasMaxLength(100);
            e.Property(l => l.Action).HasMaxLength(100);
            e.Property(l => l.Detail).HasMaxLength(500);
            e.Property(l => l.IpAddress).HasMaxLength(45);
            e.HasIndex(l => l.Timestamp);
        });

        m.Entity<PaymentTerm>(e => {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(50);
            e.HasIndex(t => t.Name).IsUnique();
        });

        m.Entity<Supplier>(e => {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(300);
            e.Property(s => s.Phone).HasMaxLength(50);
            e.Property(s => s.Address).HasMaxLength(500);
            e.Property(s => s.TaxId).HasMaxLength(50);
            e.Property(s => s.Notes).HasMaxLength(500);
            e.HasIndex(s => s.Name).IsUnique();
            e.HasOne(s => s.PaymentTerm).WithMany()
                .HasForeignKey(s => s.PaymentTermId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Purchase>(e => {
            e.HasKey(p => p.Id);
            e.Property(p => p.PurchaseNumber).IsRequired().HasMaxLength(50);
            e.Property(p => p.SupplierDocumentNumber).HasMaxLength(100);
            e.Property(p => p.SubTotal).HasColumnType("decimal(18,4)");
            e.Property(p => p.DiscountTotal).HasColumnType("decimal(18,4)");
            e.Property(p => p.InvoiceDiscountAmount).HasColumnType("decimal(18,4)");
            e.Property(p => p.InvoiceDiscountPercent).HasColumnType("decimal(18,4)");
            e.Property(p => p.TaxBase).HasColumnType("decimal(18,4)");
            e.Property(p => p.TaxRate).HasColumnType("decimal(18,4)");
            e.Property(p => p.TaxAmount).HasColumnType("decimal(18,4)");
            e.Property(p => p.GrandTotal).HasColumnType("decimal(18,4)");
            e.Property(p => p.AmountPaid).HasColumnType("decimal(18,4)");
            e.Property(p => p.CreatedBy).HasMaxLength(100);
            e.Property(p => p.Notes).HasMaxLength(500);
            e.HasOne(p => p.Supplier).WithMany(s => s.Purchases)
                .HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Branch).WithMany()
                .HasForeignKey(p => p.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.PaymentTerm).WithMany()
                .HasForeignKey(p => p.PaymentTermId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.PurchaseNumber).IsUnique();
            e.HasIndex(p => p.PurchaseDate);
            e.HasIndex(p => p.DueDate);   // AP ageing
            // Rebate settlements are matched back to the supplier's own document
            // number, so that lookup needs to be indexed, not a scan.
            e.HasIndex(p => new { p.SupplierId, p.SupplierDocumentNumber });
        });

        m.Entity<PurchaseItem>(e => {
            e.HasKey(i => i.Id);
            e.Property(i => i.Qty).HasColumnType("decimal(18,4)");
            e.Property(i => i.UnitCost).HasColumnType("decimal(18,4)");
            e.Property(i => i.DiscountAmount).HasColumnType("decimal(18,4)");
            e.Property(i => i.DiscountPercent).HasColumnType("decimal(18,4)");
            e.Property(i => i.AllocatedInvoiceDiscount).HasColumnType("decimal(18,4)");
            e.Property(i => i.LineTotal).HasColumnType("decimal(18,4)");
            e.Property(i => i.Notes).HasMaxLength(500);
            e.HasOne(i => i.Purchase).WithMany(p => p.PurchaseItems)
                .HasForeignKey(i => i.PurchaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Product).WithMany()
                .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            // Rebate rules scope by product, and volume is summed per product.
            e.HasIndex(i => i.ProductId);
        });

        m.Entity<SupplierPayment>(e => {
            e.HasKey(p => p.Id);
            e.Property(p => p.Amount).HasColumnType("decimal(18,4)");
            e.Property(p => p.Notes).HasMaxLength(300);
            e.Property(p => p.CreatedBy).HasMaxLength(100);
            e.HasOne(p => p.Purchase).WithMany(x => x.SupplierPayments)
                .HasForeignKey(p => p.PurchaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.PurchaseId);
        });

        m.Entity<RebateRule>(e => {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.Property(r => r.ThresholdQty).HasColumnType("decimal(18,4)");
            e.Property(r => r.ThresholdValue).HasColumnType("decimal(18,4)");
            e.Property(r => r.ReferenceCost).HasColumnType("decimal(18,4)");
            e.Property(r => r.RewardRate).HasColumnType("decimal(18,4)");
            e.Property(r => r.RewardAmount).HasColumnType("decimal(18,4)");
            e.Property(r => r.RewardQty).HasColumnType("decimal(18,4)");
            e.HasIndex(r => r.Name).IsUnique();
            e.HasOne(r => r.Supplier).WithMany()
                .HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Product).WithMany()
                .HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.RewardProduct).WithMany()
                .HasForeignKey(r => r.RewardProductId).OnDelete(DeleteBehavior.Restrict);
            // Evaluation loads active rules per supplier on every purchase post.
            e.HasIndex(r => new { r.SupplierId, r.IsActive });
        });

        m.Entity<RebateAccrual>(e => {
            e.HasKey(a => a.Id);
            e.Property(a => a.Qty).HasColumnType("decimal(18,4)");
            e.Property(a => a.Amount).HasColumnType("decimal(18,4)");
            e.HasOne(a => a.Rule).WithMany()
                .HasForeignKey(a => a.RebateRuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Supplier).WithMany()
                .HasForeignKey(a => a.SupplierId).OnDelete(DeleteBehavior.Restrict);
            // A cancelled purchase voids its accruals (never deletes), so keep this
            // Restrict — the accrual outlives nothing silently.
            e.HasOne(a => a.Purchase).WithMany()
                .HasForeignKey(a => a.PurchaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Realization).WithMany(r => r.Accruals)
                .HasForeignKey(a => a.RebateRealizationId).OnDelete(DeleteBehavior.SetNull);
            // The two hot queries: outstanding-by-supplier (claim worklist) and by-purchase (void/detail).
            e.HasIndex(a => new { a.SupplierId, a.RebateRealizationId, a.IsVoided });
            e.HasIndex(a => a.PurchaseId);
        });

        m.Entity<RebateRealization>(e => {
            e.HasKey(r => r.Id);
            e.Property(r => r.GrossAmount).HasColumnType("decimal(18,4)");
            e.Property(r => r.WithholdingRate).HasColumnType("decimal(18,4)");
            e.Property(r => r.WithholdingAmount).HasColumnType("decimal(18,4)");
            e.Property(r => r.NetAmount).HasColumnType("decimal(18,4)");
            e.Property(r => r.InKindQty).HasColumnType("decimal(18,4)");
            e.Property(r => r.ReferenceId).HasMaxLength(100);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.Property(r => r.CreatedBy).HasMaxLength(100);
            e.HasOne(r => r.Supplier).WithMany()
                .HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.InKindProduct).WithMany()
                .HasForeignKey(r => r.InKindProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => new { r.SupplierId, r.RealizationDate });
        });

        m.Entity<SalesPerson>(e => {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(200);
            e.Property(p => p.Phone).HasMaxLength(50);
            e.HasIndex(p => p.Name).IsUnique();
        });

        m.Entity<ExpenseCategory>(e => {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(100);
            e.HasIndex(c => c.Name).IsUnique();
        });

        m.Entity<Expense>(e => {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("decimal(18,4)");
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ReferenceNo).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            // Restrict: a category used by a posted expense must not be deletable —
            // deactivate it instead, so historical expenses stay categorised.
            e.HasOne(x => x.Category).WithMany()
                .HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            // Drives the period-and-category rollup the P&L does.
            e.HasIndex(x => new { x.ExpenseDate, x.ExpenseCategoryId });
        });

        m.Entity<AppSettings>(e => {
            e.HasKey(a => a.Id);
            e.Property(a => a.AppName).HasMaxLength(100);
            e.Property(a => a.StoreName).IsRequired().HasMaxLength(200);
            e.Property(a => a.StoreAddress).HasMaxLength(500);
            e.Property(a => a.StorePhone).HasMaxLength(100);
            e.Property(a => a.StoreFooter).HasMaxLength(300);
            e.Property(a => a.VatRate).HasColumnType("decimal(18,4)");
            e.Property(a => a.PrinterName).HasMaxLength(300);
        });

        // Seeds
        m.Entity<Branch>().HasData(new Branch {
            Id = new Guid("00000000-0000-0000-0000-000000000001"),
            Name = "Main Branch", IsDefault = true,
            CreatedAt = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc)
        });
        // Seeded to mirror the existing PaymentType.TOP* options so the switch to
        // master data is invisible to staff, plus COD which the enum never had.
        m.Entity<PaymentTerm>().HasData(
            new PaymentTerm { Id = new Guid("00000000-0000-0000-0000-000000000101"), Name = "COD",    DueDays =  0, IsActive = true, SortOrder = 1 },
            new PaymentTerm { Id = new Guid("00000000-0000-0000-0000-000000000102"), Name = "TOP 30", DueDays = 30, IsActive = true, SortOrder = 2 },
            new PaymentTerm { Id = new Guid("00000000-0000-0000-0000-000000000103"), Name = "TOP 45", DueDays = 45, IsActive = true, SortOrder = 3 },
            new PaymentTerm { Id = new Guid("00000000-0000-0000-0000-000000000104"), Name = "TOP 60", DueDays = 60, IsActive = true, SortOrder = 4 },
            new PaymentTerm { Id = new Guid("00000000-0000-0000-0000-000000000105"), Name = "TOP 90", DueDays = 90, IsActive = true, SortOrder = 5 });

        // Seeded from the Biaya Usaha lines on the FY2025 statement, so the P&L
        // reports against categories the accountant already uses.
        m.Entity<ExpenseCategory>().HasData(
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000201"), Name = "Gaji",              SortOrder =  1 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000202"), Name = "Sewa Kendaraan",    SortOrder =  2 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000203"), Name = "BBM",               SortOrder =  3 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000204"), Name = "Service Kendaraan", SortOrder =  4 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000205"), Name = "ATK",               SortOrder =  5 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000206"), Name = "Listrik",           SortOrder =  6 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000207"), Name = "Air",               SortOrder =  7 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000208"), Name = "Telepon",           SortOrder =  8 },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-000000000209"), Name = "Admin Bank",        SortOrder =  9 },
            // Tax penalties are added back as a fiscal correction by the consultant.
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-00000000020a"), Name = "Bunga & Denda Pajak", SortOrder = 10, IsTaxDeductible = false },
            new ExpenseCategory { Id = new Guid("00000000-0000-0000-0000-00000000020b"), Name = "Lain-lain",         SortOrder = 99 });

        m.Entity<AppSettings>().HasData(new AppSettings {
            Id = "default", AppName = "SimpleERP", StoreName = "My Store",
            StoreAddress = "", StorePhone = "",
            StoreFooter = "Thank you for your purchase!",
            PrinterName = "", PaperColumns = 80, PrinterEnabled = false,
            VatRate = 0.10m, RebateWithholdingRate = 0.15m
        });
    }
}
