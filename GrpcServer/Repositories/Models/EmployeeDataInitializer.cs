namespace GrpcServer.Repositories.Models
{
    public static class EmployeeDataInitializer
    {
        public static List<EmployeeDto> Initialize()
        {
            return new List<EmployeeDto>
            {
                new EmployeeDto
                {
                    Id = 1,
                    Name = "Ahmad Al-Fulan",
                    Department = "Engineering",
                    HireDate = new DateTime(2020, 1, 15)
                },
                new EmployeeDto
                {
                    Id = 2,
                    Name = "Sara Al-Otaibi",
                    Department = "Human Resources",
                    HireDate = new DateTime(2021, 6, 10)
                },
                new EmployeeDto
                {
                    Id = 3,
                    Name = "Khalid Al-Zahrani",
                    Department = "Finance",
                    HireDate = new DateTime(2019, 3, 5)
                },
                new EmployeeDto
                {
                    Id = 4,
                    Name = "Mona Al-Fahad",
                    Department = "Marketing",
                    HireDate = new DateTime(2022, 11, 1)
                }
            };
        }
    }
}