namespace GrpcServer.Repositories.Models
{
    public class EmployeeUpdateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Department { get; set; } = default!;
        public DateTime HireDate { get; set; }
    }
}