using Grpc.Core;
using GrpcServer.Data;
using GrpcServer.Repositories;
using System;

namespace GrpcServer.Services
{
    public class EmployeesService : Employee.EmployeeBase
    {
        private readonly ILogger<EmployeesService> _logger;
        private readonly IEmployeeRepository _employeeRepository;
        private readonly ApplicationDbContext _applicationDbContext;

        public EmployeesService(ILogger<EmployeesService> logger, IEmployeeRepository employeeRepository, ApplicationDbContext applicationDbContext)
        {
            _logger = logger;
            _employeeRepository = employeeRepository;
            _applicationDbContext = applicationDbContext;
        }

        public override async Task<EmployeeReply> GetEmployee(EmployeeRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Received request for employee with ID: {Id}", request.Id);

            var employee = await _employeeRepository.GetByIdAsync(request.Id);
            if (employee is null)
            {
                return null;
            }

            return employee.ToEmployeeReply();
        }

        public override async Task<EmployeesReply> GetEmployees(EmployeesRequest request, ServerCallContext context)
        {
            var employees = await _employeeRepository.GetAllAsync();

            var reply = employees.ToList().ConvertAll(e => e.ToEmployeeReply());

            return new EmployeesReply { Employees = { reply } };
        }

        public override async Task GetEmployeesStream(EmployeesRequest request, IServerStreamWriter<EmployeeReply> responseStream, ServerCallContext context)
        {
            var count = request.Count > 0 ? request.Count : int.MaxValue;
            var processed = 0;

            // Use async streaming from EF Core
            await foreach (var emp in _applicationDbContext.Employees.AsAsyncEnumerable().WithCancellation(context.CancellationToken))
            {
                if (processed >= count) break;

                var employee = new EmployeeReply
                {
                    Id = emp.Id,
                    Name = emp.FirstName + " " + emp.LastName,
                    Department = emp.Department,
                    Salary = (double)emp.Salary,  // Convert decimal to double
                    DateOfBirth = emp.DateOfBirth.ToString("O")  // ISO 8601 format
                };

                await responseStream.WriteAsync(employee);
                processed++;
            }
        }
    }
}