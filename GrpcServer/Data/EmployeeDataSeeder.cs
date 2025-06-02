using Bogus;
using GrpcServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GrpcServer.Data
{
    public class EmployeeDataSeeder
    {
        private readonly ApplicationDbContext _context;
        private const int BatchSize = 1000; // Number of records to insert in each batch
        private int _emailCounter = 0; // Counter to ensure unique emails

        public EmployeeDataSeeder(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task SeedAsync(int numberOfRecords = 5_000_000)
        {
            if (await _context.Employees.CountAsync() == numberOfRecords)
            {
                return; // Database already seeded
            }

            var faker = new Faker<EmployeeEntity>()
                .RuleFor(e => e.FirstName, f => f.Name.FirstName())
                .RuleFor(e => e.LastName, f => f.Name.LastName())
                .RuleFor(e => e.Email, (f, e) =>
                {
                    var counter = Interlocked.Increment(ref _emailCounter);
                    return $"{e.FirstName.ToLower()}.{e.LastName.ToLower()}{counter}@company.com";
                })
                .RuleFor(e => e.DateOfBirth, f => f.Date.Past(50, DateTime.Now.AddYears(-18)))
                .RuleFor(e => e.Department, f => f.PickRandom(new[] { "Engineering", "Marketing", "Finance", "HR", "Sales", "IT", "Operations", "Customer Support" }))
                .RuleFor(e => e.Salary, f => f.Random.Decimal(30000, 150000))
                .RuleFor(e => e.CreatedAt, f => f.Date.Past(2))
                .RuleFor(e => e.UpdatedAt, f => f.Date.Recent());

            var totalBatches = (int)Math.Ceiling(numberOfRecords / (double)BatchSize);

            for (int i = 0; i < totalBatches; i++)
            {
                var batchSize = Math.Min(BatchSize, numberOfRecords - (i * BatchSize));
                var employees = faker.Generate(batchSize);

                await _context.Employees.AddRangeAsync(employees);
                await _context.SaveChangesAsync();

                // Clear the change tracker to prevent memory issues
                _context.ChangeTracker.Clear();

                Console.WriteLine($"Seeded batch {i + 1} of {totalBatches} ({(i + 1) * BatchSize:N0} records)");
            }
        }
    }
}