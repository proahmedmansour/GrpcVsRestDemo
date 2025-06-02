using GrpcServer.Repositories.Models;

namespace GrpcServer.Repositories
{
    public class InMemoryEmployeeRepository : IEmployeeRepository
    {
        private readonly List<EmployeeDto> _employees = EmployeeDataInitializer.Initialize();
        private int _nextId = 1;

        public Task<IEnumerable<EmployeeDto>> GetAllAsync()
        {
            return Task.FromResult(_employees.AsEnumerable());
        }

        public Task<EmployeeDto?> GetByIdAsync(int id)
        {
            var employee = _employees.FirstOrDefault(e => e.Id == id);
            return Task.FromResult(employee);
        }

        public Task AddAsync(EmployeeCreateDto employee)
        {
            var newEmployee = new EmployeeDto
            {
                Id = _nextId++,
                Name = employee.Name,
                Department = employee.Department,
                HireDate = employee.HireDate
            };

            _employees.Add(newEmployee);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(EmployeeUpdateDto employee)
        {
            var index = _employees.FindIndex(e => e.Id == employee.Id);
            if (index >= 0)
            {
                _employees[index] = new EmployeeDto
                {
                    Id = employee.Id,
                    Name = employee.Name,
                    Department = employee.Department,
                    HireDate = employee.HireDate
                };
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int id)
        {
            var emp = _employees.FirstOrDefault(e => e.Id == id);
            if (emp != null)
            {
                _employees.Remove(emp);
            }
            return Task.CompletedTask;
        }
    }
}