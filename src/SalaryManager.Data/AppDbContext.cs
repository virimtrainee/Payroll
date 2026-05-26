using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SalaryManager.Data.Entities;
using SalaryManager.Data.Services;

namespace SalaryManager.Data;

public class AppDbContext : DbContext
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<SalaryRevision> SalaryRevisions => Set<SalaryRevision>();
    public DbSet<EmployeeGroup> EmployeeGroups => Set<EmployeeGroup>();
    public DbSet<EmployeeGroupMembership> EmployeeGroupMemberships => Set<EmployeeGroupMembership>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public AppDbContext() { }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(AppContext.BaseDirectory, "salary.design.db")
            }.ToString();
            options.UseSqlite(connectionString);
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
            e.Property(a => a.TdsDeduction)
                .HasColumnType("DECIMAL(18,2)")
                .HasDefaultValue(0m);

            e.HasOne(a => a.Employee)
             .WithMany(p => p.AttendanceRecords)
             .HasForeignKey(a => a.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.EmployeeId, a.Year, a.Month }).IsUnique();
        });

        mb.Entity<Advance>(e =>
        {
            e.Property(a => a.Amount).HasColumnType("DECIMAL(18,2)");
            e.Property(a => a.SourceKey)
                .HasMaxLength(64)
                .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
            e.HasOne(a => a.Employee)
             .WithMany(p => p.Advances)
             .HasForeignKey(a => a.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.EmployeeId, a.Date });
            e.HasIndex(a => new { a.EmployeeId, a.SourceKey }).IsUnique();
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

        mb.Entity<EmployeeGroup>(e =>
        {
            e.Property(g => g.Name).HasMaxLength(120).IsRequired();
            e.Property(g => g.NormalizedName).HasMaxLength(120).IsRequired();
            e.HasIndex(g => g.NormalizedName).IsUnique();
        });

        mb.Entity<EmployeeGroupMembership>(e =>
        {
            e.HasKey(m => new { m.EmployeeId, m.EmployeeGroupId });

            e.HasOne(m => m.EmployeeGroup)
             .WithMany(g => g.Memberships)
             .HasForeignKey(m => m.EmployeeGroupId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(m => m.Employee)
             .WithMany(emp => emp.GroupMemberships)
             .HasForeignKey(m => m.EmployeeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(m => new { m.EmployeeGroupId, m.EmployeeId });
        });
    }

    public override int SaveChanges()
    {
        NormalizeEmployeeGroupNames();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        NormalizeEmployeeGroupNames();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void NormalizeEmployeeGroupNames()
    {
        foreach (var entry in ChangeTracker.Entries<EmployeeGroup>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NormalizedName = GroupNameNormalizer.Normalize(entry.Entity.Name);
            }
        }

        foreach (var entry in ChangeTracker.Entries<Advance>())
        {
            if (entry.State != EntityState.Modified) continue;
            if (!entry.Property(a => a.SourceKey).IsModified) continue;

            var original = entry.Property(a => a.SourceKey).OriginalValue;
            if (!string.IsNullOrWhiteSpace(original) && entry.Entity.SourceKey != original)
            {
                throw new InvalidOperationException("Advance source keys are immutable once set.");
            }
        }
    }
}
