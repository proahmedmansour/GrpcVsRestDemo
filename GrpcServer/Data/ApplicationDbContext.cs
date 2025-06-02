using Microsoft.EntityFrameworkCore;
using GrpcServer.Models;

namespace GrpcServer.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<EmployeeEntity> Employees { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EmployeeEntity>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired();
                entity.Property(e => e.FirstName).IsRequired();
                entity.Property(e => e.LastName).IsRequired();
                entity.Property(e => e.Department).IsRequired();
                entity.Property(e => e.Salary).HasPrecision(18, 2);
                
                // Add a unique index on email
                entity.HasIndex(e => e.Email).IsUnique();
            });
        }
    }
} 