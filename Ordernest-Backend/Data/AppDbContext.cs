using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Ordernest.Backend.Models;

namespace Ordernest.Backend.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // MUST be first: lets Identity configure its own tables.
        base.OnModelCreating(b);

        // Money columns: decimal(18,2) — the standard for currency.
        b.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
        b.Entity<Product>().Property(p => p.CostPrice).HasPrecision(18, 2);
        b.Entity<Order>().Property(o => o.SubTotal).HasPrecision(18, 2);
        b.Entity<Order>().Property(o => o.DiscountAmount).HasPrecision(18, 2);
        b.Entity<Order>().Property(o => o.TaxAmount).HasPrecision(18, 2);
        b.Entity<Order>().Property(o => o.TotalAmount).HasPrecision(18, 2);
        b.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
        b.Entity<OrderItem>().Property(i => i.LineDiscount).HasPrecision(18, 2);
        b.Entity<OrderItem>().Property(i => i.LineTotal).HasPrecision(18, 2);

        // Enums stored as readable strings ("Completed"), not ints (1).
        b.Entity<Order>().Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<Order>().Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(20);

        // Indexes on every column we search or filter by.
        b.Entity<Category>().HasIndex(c => c.Name).IsUnique();
        b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        // Filtered: allows many NULL barcodes (SQL unique indexes treat NULLs as equal).
        b.Entity<Product>().HasIndex(p => p.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        b.Entity<Product>().HasIndex(p => p.Name);
        b.Entity<Product>().HasIndex(p => p.CategoryId);
        b.Entity<Customer>().HasIndex(c => c.Phone);
        b.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        b.Entity<Order>().HasIndex(o => o.CreatedAt);
        b.Entity<Order>().HasIndex(o => o.UserId);
        b.Entity<Order>().HasIndex(o => o.CustomerId);
        b.Entity<OrderItem>().HasIndex(i => i.OrderId);
        b.Entity<OrderItem>().HasIndex(i => i.ProductId);

        // Relationships and delete behavior.
        // Restrict: cannot delete a Category that has products.
        b.Entity<Product>()
            .HasOne(p => p.Category).WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        // SetNull: deleting a customer keeps their orders (as walk-in sales).
        b.Entity<Order>()
            .HasOne(o => o.Customer).WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.SetNull);

        // Restrict: cannot delete the cashier account behind a sale.
        b.Entity<Order>()
            .HasOne(o => o.User).WithMany()
            .HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);

        // Cascade: an order item has no meaning without its order.
        b.Entity<OrderItem>()
            .HasOne(i => i.Order).WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);

        // Restrict: a product with sales history cannot be hard-deleted
        // (soft delete via IsActive is the supported path).
        b.Entity<OrderItem>()
            .HasOne(i => i.Product).WithMany(p => p.OrderItems)
            .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
