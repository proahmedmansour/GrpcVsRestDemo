namespace GrpcServer.Repositories.Models
{
    public class EmployeeCreateDto
    {
        public string Name { get; set; } = default!;
        public string Department { get; set; } = default!;
        public DateTime HireDate { get; set; }
    }

}
