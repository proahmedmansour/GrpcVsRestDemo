namespace GrpcServer.Repositories.Models
{
    public class EmployeeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Department { get; set; } = default!;
        public DateTime HireDate { get; set; }

        internal EmployeeReply ToEmployeeReply()
        {
            return new EmployeeReply
            {
                Id = Id,
                Name = Name,
                Department = Department,
            };
        }
    }
}