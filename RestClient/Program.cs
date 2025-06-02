using System.Diagnostics;
using System.Text.Json;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly string _baseUrl = "http://localhost:5000/api/employees";
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    static async Task Main(string[] args)
    {
        Console.WriteLine("REST API Client - Employee Data Performance Test");
        Console.WriteLine("==============================================");

        while (true)
        {
            Console.WriteLine("\nSelect an option:");
            Console.WriteLine("1. Get paginated employees");
            Console.WriteLine("2. Stream employees");
            Console.WriteLine("3. Exit");
            Console.Write("\nEnter your choice (1-3): ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await TestPaginatedEmployees();
                    break;
                case "2":
                    await TestStreamingEmployees();
                    break;
                case "3":
                    return;
                default:
                    Console.WriteLine("Invalid choice. Please try again.");
                    break;
            }
        }
    }

    private static async Task TestPaginatedEmployees()
    {
        Console.Write("\nEnter page size (default 100): ");
        if (!int.TryParse(Console.ReadLine(), out int pageSize) || pageSize <= 0)
        {
            pageSize = 100;
        }

        Console.Write("Enter number of pages to test (default 1): ");
        if (!int.TryParse(Console.ReadLine(), out int pages) || pages <= 0)
        {
            pages = 1;
        }

        var totalTime = 0L;
        var totalRecords = 0;

        for (int page = 1; page <= pages; page++)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}?page={page}&pageSize={pageSize}");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PaginatedResponse>(content, _jsonOptions);

                stopwatch.Stop();
                totalTime += stopwatch.ElapsedMilliseconds;
                totalRecords += result?.Data?.Count ?? 0;

                Console.WriteLine($"\nPage {page}:");
                Console.WriteLine($"Records retrieved: {result?.Data?.Count ?? 0}");
                Console.WriteLine($"Execution time: {stopwatch.ElapsedMilliseconds}ms");
                Console.WriteLine($"Total records in database: {result?.TotalCount ?? 0}");
                Console.WriteLine($"Total pages: {result?.TotalPages ?? 0}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return;
            }
        }

        if (pages > 1)
        {
            Console.WriteLine("\nSummary:");
            Console.WriteLine($"Total records retrieved: {totalRecords}");
            Console.WriteLine($"Average time per page: {totalTime / (double)pages:F2}ms");
            Console.WriteLine($"Total time: {totalTime}ms");
        }
    }

    private static async Task TestStreamingEmployees()
    {
        Console.WriteLine("\nStarting REST employee stream test...");
        var stopwatch = Stopwatch.StartNew();
        var totalRecords = 0;
        var lastProgressUpdate = DateTime.Now;
        var progressInterval = TimeSpan.FromSeconds(1); // Update progress every second
        var streamStartTime = DateTime.Now;

        try
        {
            using var response = await _httpClient.GetStreamAsync($"{_baseUrl}/stream");
            using var reader = new StreamReader(response);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

                var json = line.Substring(6); // Remove "data: " prefix
                var employee = JsonSerializer.Deserialize<Employee>(json, _jsonOptions);
                
                totalRecords++;
                
                // Update progress periodically
                var now = DateTime.Now;
                if (now - lastProgressUpdate >= progressInterval)
                {
                    var elapsed = now - streamStartTime;
                    var recordsPerSecond = totalRecords / elapsed.TotalSeconds;
                    Console.WriteLine($"Processed {totalRecords:N0} records... ({recordsPerSecond:N0} records/sec)");
                    lastProgressUpdate = now;
                }
            }

            stopwatch.Stop();
            Console.WriteLine("\nStreaming completed:");
            Console.WriteLine($"Total records processed: {totalRecords:N0}");
            Console.WriteLine($"Total time: {stopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"Average time per record: {stopwatch.ElapsedMilliseconds / (double)totalRecords:F2}ms");
            Console.WriteLine($"Average throughput: {totalRecords / stopwatch.Elapsed.TotalSeconds:N0} records/sec");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError during streaming: {ex.Message}");
            if (ex is HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP Status Code: {httpEx.StatusCode}");
            }
        }
    }
}

public class PaginatedResponse
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public long ExecutionTimeMs { get; set; }
    public List<Employee> Data { get; set; } = new();
}

public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public decimal Salary { get; set; }
    public DateTime DateOfBirth { get; set; }
} 