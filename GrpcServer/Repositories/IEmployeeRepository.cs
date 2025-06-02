using GrpcServer.Repositories.Models;

namespace GrpcServer.Repositories
{
    public interface IEmployeeRepository
    {
        Task<IEnumerable<EmployeeDto>> GetAllAsync();

        Task<EmployeeDto?> GetByIdAsync(int id);

        Task AddAsync(EmployeeCreateDto employee);

        Task UpdateAsync(EmployeeUpdateDto employee);

        Task DeleteAsync(int id);
    }
}