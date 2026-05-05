using Microsoft.EntityFrameworkCore;
using SalaryManager.Data.Entities;

namespace SalaryManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<SalaryRevision> SalaryRevisions => Set<SalaryRevision>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public AppDbContext() { }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            options.UseSqlite("Data Source=salary.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Employee>(e =>
        {
            e.Property(p => p.BaseSalary).HasColumnType("DECIMAL(18,2)");
            e.HasIndex(p => new { p.IsActive, p.Name });
        });

        mb.Entity<AttendanceRecord>(e =>
        {
            e.Property(a => a.EsicDeduction).HasColumnType("DECIMAL(18,2)");
            e.Property(a => a.PfDeduction).HasColumnType("DECIMAL(18,2)");

            e.HasOne(a => a.Employee)
             .WithMany(p => p.AttendanceRecords)
             .HasForeignKey(a => a.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.EmployeeId, a.Year, a.Month }).IsUnique();
        });

        mb.Entity<Advance>(e =>
        {
            e.Property(a => a.Amount).HasColumnType("DECIMAL(18,2)");
            e.HasOne(a => a.Employee)
             .WithMany(p => p.Advances)
             .HasForeignKey(a => a.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.EmployeeId, a.Date });
        });

        mb.Entity<SalaryRevision>(e =>
        {
            e.Property(r => r.OldSalary).HasColumnType("DECIMAL(18,2)");
            e.Property(r => r.NewSalary).HasColumnType("DECIMAL(18,2)");
            e.HasOne(r => r.Employee)
             .WithMany(emp => emp.SalaryRevisions)
             .HasForeignKey(r => r.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(r => new { r.EmployeeId, r.ChangedAt });
        });
    }
}
