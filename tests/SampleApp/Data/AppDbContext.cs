using Messaging.OutboxInbox;
using Microsoft.EntityFrameworkCore;
using SampleApp.Entities;

namespace SampleApp.Data;

public sealed class AppDbContext : OutboxInboxContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Order>().ToTable("Orders");
        modelBuilder.Entity<Order>().HasKey(e => e.Id);
        modelBuilder.Entity<Order>().Property(e => e.CustomerName).IsRequired().HasMaxLength(200);
        modelBuilder.Entity<Order>().Property(e => e.TotalAmount).HasPrecision(18, 2);
    }
}