using Grpc.Core;
using Grpc.Net.Client;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace GrpcClient.GrpcClientServices
{
    internal class GrpcEmployeeClientService
    {
        private readonly Employee.EmployeeClient _employeeClient;
        private readonly string _reportDirectory = "Reports";

        public GrpcEmployeeClientService(string grpcServerUrl)
        {
            var channel = GrpcChannel.ForAddress(grpcServerUrl);
            _employeeClient = new Employee.EmployeeClient(channel);
            Directory.CreateDirectory(_reportDirectory);
        }

        /// <summary>
        /// Uniery call to get an employee by ID.
        /// </summary>
        /// <returns></returns>
        public async Task GetEmployeeByIdAsync()
        {
            Console.WriteLine("Making RPC call to GetEmployee... \nEnter employee Id: ");
            var id = Console.ReadLine();

            if (int.TryParse(id, out int employeeId))
            {
                var reply = await _employeeClient.GetEmployeeAsync(new EmployeeRequest { Id = employeeId });
                Console.WriteLine($"Response: Name: {reply.Name}, Department: {reply.Department}");
            }
            else
            {
                Console.WriteLine("Invalid ID entered.");
            }
        }

        /// <summary>
        /// Unary call to get a list of employees.
        /// </summary>
        /// <returns></returns>
        public async Task GetEmployeesListAsync()
        {
            Console.WriteLine("Using list response:");
            var reply = await _employeeClient.GetEmployeesAsync(new EmployeesRequest());

            foreach (var item in reply.Employees)
            {
                Console.WriteLine($"Employee ID: {item.Id}, Name: {item.Name}, Department: {item.Department}");
            }
        }

        /// <summary>
        /// Server streaming call to get a list of employees with performance measurements.
        /// </summary>
        /// <returns></returns>
        public async Task StreamEmployeesAsync()
        {
            Console.WriteLine("\nStarting gRPC employee stream test...");
            var stopwatch = Stopwatch.StartNew();
            var totalRecords = 0;
            var lastProgressUpdate = DateTime.Now;
            var progressInterval = TimeSpan.FromSeconds(1); // Update progress every second

            try
            {
                using var call = _employeeClient.GetEmployeesStream(new EmployeesRequest());
                var streamStartTime = DateTime.Now;

                await foreach (var employee in call.ResponseStream.ReadAllAsync())
                {
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
                if (ex is RpcException rpcEx)
                {
                    Console.WriteLine($"gRPC Status Code: {rpcEx.Status.StatusCode}");
                    Console.WriteLine($"gRPC Status Detail: {rpcEx.Status.Detail}");
                }
            }
        }

        public async Task<EmployeeReport> GetEmployeesWithReportAsync(int count, CancellationToken cancellationToken = default)
        {
            var report = new EmployeeReport
            {
                GeneratedAt = DateTime.UtcNow,
                TotalRecords = count
            };

            var departmentStats = new ConcurrentDictionary<string, DepartmentStats>();
            var salaryRanges = new ConcurrentDictionary<string, int>();
            var ageGroups = new ConcurrentDictionary<string, int>();
            var processingStartTime = DateTime.UtcNow;

            try
            {
                using var call = _employeeClient.GetEmployeesStream(new EmployeesRequest { Count = count });
                var responseStream = call.ResponseStream;

                await foreach (var employee in responseStream.ReadAllAsync(cancellationToken))
                {
                    // Convert salary from double to decimal
                    var salary = (decimal)employee.Salary;

                    // Parse date of birth from ISO 8601 string
                    var dateOfBirth = DateTime.Parse(employee.DateOfBirth);

                    // Update department statistics
                    departmentStats.AddOrUpdate(
                        employee.Department,
                        new DepartmentStats { Count = 1, TotalSalary = salary },
                        (_, stats) => new DepartmentStats
                        {
                            Count = stats.Count + 1,
                            TotalSalary = stats.TotalSalary + salary
                        });

                    // Update salary range statistics
                    var salaryRange = GetSalaryRange(salary);
                    salaryRanges.AddOrUpdate(salaryRange, 1, (_, count) => count + 1);

                    // Update age group statistics
                    var ageGroup = GetAgeGroup(dateOfBirth);
                    ageGroups.AddOrUpdate(ageGroup, 1, (_, count) => count + 1);

                    // Update report metrics
                    report.ProcessedRecords++;
                    report.TotalSalary += salary;
                    report.AverageSalary = report.TotalSalary / report.ProcessedRecords;
                }

                // Finalize report
                report.ProcessingTime = DateTime.UtcNow - processingStartTime;
                report.DepartmentStatistics = departmentStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new DepartmentReport
                    {
                        EmployeeCount = kvp.Value.Count,
                        AverageSalary = kvp.Value.TotalSalary / kvp.Value.Count,
                        PercentageOfTotal = (double)kvp.Value.Count / report.ProcessedRecords * 100
                    });

                report.SalaryDistribution = salaryRanges.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new SalaryRangeReport
                    {
                        Count = kvp.Value,
                        Percentage = (double)kvp.Value / report.ProcessedRecords * 100
                    });

                report.AgeDistribution = ageGroups.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new AgeGroupReport
                    {
                        Count = kvp.Value,
                        Percentage = (double)kvp.Value / report.ProcessedRecords * 100
                    });

                // Generate and save report
                await GenerateReportFileAsync(report);

                return report;
            }
            catch (Exception ex)
            {
                report.Error = ex.Message;
                return report;
            }
        }

        private async Task GenerateReportFileAsync(EmployeeReport report)
        {
            var reportFileName = $"EmployeeReport_{report.GeneratedAt:yyyyMMdd_HHmmss}.json";
            var reportPath = Path.Combine(_reportDirectory, reportFileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var reportJson = JsonSerializer.Serialize(report, options);
            await File.WriteAllTextAsync(reportPath, reportJson);
        }

        private string GetSalaryRange(decimal salary)
        {
            return salary switch
            {
                < 30000 => "Below 30K",
                < 50000 => "30K-50K",
                < 75000 => "50K-75K",
                < 100000 => "75K-100K",
                < 150000 => "100K-150K",
                _ => "Above 150K"
            };
        }

        private string GetAgeGroup(DateTime dateOfBirth)
        {
            var age = DateTime.Today.Year - dateOfBirth.Year;
            if (dateOfBirth.Date > DateTime.Today.AddYears(-age)) age--;

            return age switch
            {
                < 25 => "Under 25",
                < 35 => "25-34",
                < 45 => "35-44",
                < 55 => "45-54",
                < 65 => "55-64",
                _ => "65 and above"
            };
        }

        private async Task<string> GenerateHtmlReportAsync(EmployeeReport report)
        {
            var html = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Employee Report - {DateTime.Now:yyyy-MM-dd HH:mm:ss}</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 1200px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background-color: white;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            padding: 30px;
            margin-bottom: 20px;
        }}
        h1, h2, h3 {{
            color: #2c3e50;
            margin-top: 0;
        }}
        h1 {{
            text-align: center;
            border-bottom: 2px solid #3498db;
            padding-bottom: 10px;
            margin-bottom: 30px;
        }}
        .summary-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }}
        .summary-card {{
            background-color: #f8f9fa;
            padding: 15px;
            border-radius: 6px;
            text-align: center;
            border-left: 4px solid #3498db;
        }}
        .summary-card h3 {{
            margin: 0 0 10px 0;
            font-size: 1.1em;
            color: #7f8c8d;
        }}
        .summary-card .value {{
            font-size: 1.5em;
            font-weight: bold;
            color: #2c3e50;
        }}
        .department-stats {{
            margin-bottom: 30px;
        }}
        .department-card {{
            background-color: white;
            border: 1px solid #e0e0e0;
            border-radius: 6px;
            padding: 15px;
            margin-bottom: 15px;
        }}
        .department-card h3 {{
            color: #3498db;
            margin-top: 0;
        }}
        .chart-container {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }}
        .chart {{
            background-color: white;
            border-radius: 6px;
            padding: 20px;
            box-shadow: 0 1px 3px rgba(0,0,0,0.1);
        }}
        .chart h3 {{
            text-align: center;
            margin-bottom: 20px;
        }}
        .bar-chart {{
            display: flex;
            flex-direction: column;
            gap: 10px;
        }}
        .bar {{
            background-color: #3498db;
            color: white;
            padding: 8px 15px;
            border-radius: 4px;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}
        .bar-label {{
            font-weight: bold;
        }}
        .bar-value {{
            background-color: rgba(255,255,255,0.2);
            padding: 2px 8px;
            border-radius: 3px;
        }}
        .footer {{
            text-align: center;
            color: #7f8c8d;
            font-size: 0.9em;
            margin-top: 30px;
            padding-top: 20px;
            border-top: 1px solid #e0e0e0;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>Employee Report</h1>

        <div class='summary-grid'>
            <div class='summary-card'>
                <h3>Total Records</h3>
                <div class='value'>{report.TotalRecords:N0}</div>
            </div>
            <div class='summary-card'>
                <h3>Processed Records</h3>
                <div class='value'>{report.ProcessedRecords:N0}</div>
            </div>
            <div class='summary-card'>
                <h3>Processing Time</h3>
                <div class='value'>{report.ProcessingTime.TotalSeconds:F2}s</div>
            </div>
            <div class='summary-card'>
                <h3>Average Salary</h3>
                <div class='value'>{report.AverageSalary:C}</div>
            </div>
        </div>

        <h2>Department Statistics</h2>
        <div class='department-stats'>";

            foreach (var dept in report.DepartmentStatistics.OrderByDescending(d => d.Value.EmployeeCount))
            {
                html += $@"
            <div class='department-card'>
                <h3>{dept.Key}</h3>
                <p>Employees: {dept.Value.EmployeeCount:N0} ({dept.Value.PercentageOfTotal:F1}%)</p>
                <p>Average Salary: {dept.Value.AverageSalary:C}</p>
            </div>";
            }

            html += $@"
        </div>

        <div class='chart-container'>
            <div class='chart'>
                <h3>Salary Distribution</h3>
                <div class='bar-chart'>";

            foreach (var range in report.SalaryDistribution.OrderBy(r => r.Key))
            {
                html += $@"
                    <div class='bar' style='width: {range.Value.Percentage}%'>
                        <span class='bar-label'>{range.Key}</span>
                        <span class='bar-value'>{range.Value.Count:N0} ({range.Value.Percentage:F1}%)</span>
                    </div>";
            }

            html += $@"
                </div>
            </div>

            <div class='chart'>
                <h3>Age Distribution</h3>
                <div class='bar-chart'>";

            foreach (var group in report.AgeDistribution.OrderBy(a => a.Key))
            {
                html += $@"
                    <div class='bar' style='width: {group.Value.Percentage}%'>
                        <span class='bar-label'>{group.Key}</span>
                        <span class='bar-value'>{group.Value.Count:N0} ({group.Value.Percentage:F1}%)</span>
                    </div>";
            }

            html += $@"
                </div>
            </div>
        </div>

        <div class='footer'>
            <p>Report generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
            {(!string.IsNullOrEmpty(report.Error) ? $"<p style='color: #e74c3c;'>Warning: {report.Error}</p>" : "")}
        </div>
    </div>
</body>
</html>";

            return html;
        }

        public async Task<string> GetEmployeesWithHtmlReportAsync(int count, CancellationToken cancellationToken = default)
        {
            var report = await GetEmployeesWithReportAsync(count, cancellationToken);
            var htmlReport = await GenerateHtmlReportAsync(report);

            // Save the HTML report
            var reportDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Reports");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(reportDirectory, $"EmployeeReport_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            await File.WriteAllTextAsync(reportPath, htmlReport, cancellationToken);

            return reportPath;
        }

        public async Task<string> ExportEmployeesToExcelAsync(int count, CancellationToken cancellationToken = default)
        {
            // EPPlus 8+ doesn't require explicit license setting for non-commercial use
            var report = await GetEmployeesWithReportAsync(count, cancellationToken);
            var excelPath = Path.Combine(_reportDirectory, $"EmployeeReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

            using var package = new ExcelPackage();

            // Create Summary Sheet
            var summarySheet = package.Workbook.Worksheets.Add("Summary");
            AddSummarySheet(summarySheet, report);

            // Create Department Stats Sheet
            var deptSheet = package.Workbook.Worksheets.Add("Department Statistics");
            AddDepartmentStatsSheet(deptSheet, report);

            // Create Salary Distribution Sheet
            var salarySheet = package.Workbook.Worksheets.Add("Salary Distribution");
            AddSalaryDistributionSheet(salarySheet, report);

            // Create Age Distribution Sheet
            var ageSheet = package.Workbook.Worksheets.Add("Age Distribution");
            AddAgeDistributionSheet(ageSheet, report);

            // Create Raw Data Sheet
            var rawDataSheet = package.Workbook.Worksheets.Add("Employee Data");
            await AddEmployeeDataSheetAsync(rawDataSheet, count, cancellationToken);

            // Auto-fit columns for all sheets
            foreach (var worksheet in package.Workbook.Worksheets)
            {
                worksheet.Cells.AutoFitColumns();
            }

            // Save the Excel file
            await package.SaveAsAsync(new FileInfo(excelPath), cancellationToken);
            return excelPath;
        }

        private void AddSummarySheet(ExcelWorksheet sheet, EmployeeReport report)
        {
            // Add title
            sheet.Cells["A1"].Value = "Employee Report Summary";
            sheet.Cells["A1:D1"].Merge = true;
            sheet.Cells["A1"].Style.Font.Size = 16;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add summary data
            var summaryData = new[]
            {
                new { Metric = "Total Records", Value = report.TotalRecords.ToString("N0") },
                new { Metric = "Processed Records", Value = report.ProcessedRecords.ToString("N0") },
                new { Metric = "Processing Time", Value = $"{report.ProcessingTime.TotalSeconds:F2} seconds" },
                new { Metric = "Average Salary", Value = report.AverageSalary.ToString("C") },
                new { Metric = "Total Salary", Value = report.TotalSalary.ToString("C") },
                new { Metric = "Generated At", Value = report.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            // Add headers
            sheet.Cells["A3"].Value = "Metric";
            sheet.Cells["B3"].Value = "Value";
            sheet.Cells["A3:B3"].Style.Font.Bold = true;
            sheet.Cells["A3:B3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3:B3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Add data
            for (int i = 0; i < summaryData.Length; i++)
            {
                sheet.Cells[$"A{i + 4}"].Value = summaryData[i].Metric;
                sheet.Cells[$"B{i + 4}"].Value = summaryData[i].Value;
            }

            // Add warning if any
            if (!string.IsNullOrEmpty(report.Error))
            {
                sheet.Cells[$"A{summaryData.Length + 5}"].Value = "Warning";
                sheet.Cells[$"B{summaryData.Length + 5}"].Value = report.Error;
                sheet.Cells[$"A{summaryData.Length + 5}:B{summaryData.Length + 5}"].Style.Font.Color.SetColor(Color.Red);
            }

            // Style the summary sheet
            sheet.Cells["A1:B10"].Style.Border.Top.Style = ExcelBorderStyle.Thin;
            sheet.Cells["A1:B10"].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            sheet.Cells["A1:B10"].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            sheet.Cells["A1:B10"].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        private void AddDepartmentStatsSheet(ExcelWorksheet sheet, EmployeeReport report)
        {
            // Add title
            sheet.Cells["A1"].Value = "Department Statistics";
            sheet.Cells["A1:D1"].Merge = true;
            sheet.Cells["A1"].Style.Font.Size = 14;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add headers
            sheet.Cells["A3"].Value = "Department";
            sheet.Cells["B3"].Value = "Employee Count";
            sheet.Cells["C3"].Value = "Percentage";
            sheet.Cells["D3"].Value = "Average Salary";
            sheet.Cells["A3:D3"].Style.Font.Bold = true;
            sheet.Cells["A3:D3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3:D3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Add data
            int row = 4;
            foreach (var dept in report.DepartmentStatistics.OrderByDescending(d => d.Value.EmployeeCount))
            {
                sheet.Cells[$"A{row}"].Value = dept.Key;
                sheet.Cells[$"B{row}"].Value = dept.Value.EmployeeCount;
                sheet.Cells[$"C{row}"].Value = dept.Value.PercentageOfTotal;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                sheet.Cells[$"D{row}"].Value = dept.Value.AverageSalary;
                sheet.Cells[$"D{row}"].Style.Numberformat.Format = "$#,##0.00";
                row++;
            }

            // Add total row
            sheet.Cells[$"A{row}"].Value = "Total";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"B{row}"].Value = report.ProcessedRecords;
            sheet.Cells[$"B{row}"].Style.Font.Bold = true;
            sheet.Cells[$"C{row}"].Value = 1;
            sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
            sheet.Cells[$"C{row}"].Style.Font.Bold = true;
            sheet.Cells[$"D{row}"].Value = report.AverageSalary;
            sheet.Cells[$"D{row}"].Style.Numberformat.Format = "$#,##0.00";
            sheet.Cells[$"D{row}"].Style.Font.Bold = true;

            // Style the sheet
            var dataRange = sheet.Cells[$"A3:D{row}"];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        private void AddSalaryDistributionSheet(ExcelWorksheet sheet, EmployeeReport report)
        {
            // Add title
            sheet.Cells["A1"].Value = "Salary Distribution";
            sheet.Cells["A1:C1"].Merge = true;
            sheet.Cells["A1"].Style.Font.Size = 14;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add headers
            sheet.Cells["A3"].Value = "Salary Range";
            sheet.Cells["B3"].Value = "Count";
            sheet.Cells["C3"].Value = "Percentage";
            sheet.Cells["A3:C3"].Style.Font.Bold = true;
            sheet.Cells["A3:C3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3:C3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Add data
            int row = 4;
            foreach (var range in report.SalaryDistribution.OrderBy(r => r.Key))
            {
                sheet.Cells[$"A{row}"].Value = range.Key;
                sheet.Cells[$"B{row}"].Value = range.Value.Count;
                sheet.Cells[$"C{row}"].Value = range.Value.Percentage;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                row++;
            }

            // Style the sheet
            var dataRange = sheet.Cells[$"A3:C{row - 1}"];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        private void AddAgeDistributionSheet(ExcelWorksheet sheet, EmployeeReport report)
        {
            // Add title
            sheet.Cells["A1"].Value = "Age Distribution";
            sheet.Cells["A1:C1"].Merge = true;
            sheet.Cells["A1"].Style.Font.Size = 14;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add headers
            sheet.Cells["A3"].Value = "Age Group";
            sheet.Cells["B3"].Value = "Count";
            sheet.Cells["C3"].Value = "Percentage";
            sheet.Cells["A3:C3"].Style.Font.Bold = true;
            sheet.Cells["A3:C3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3:C3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Add data
            int row = 4;
            foreach (var group in report.AgeDistribution.OrderBy(a => a.Key))
            {
                sheet.Cells[$"A{row}"].Value = group.Key;
                sheet.Cells[$"B{row}"].Value = group.Value.Count;
                sheet.Cells[$"C{row}"].Value = group.Value.Percentage;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                row++;
            }

            // Style the sheet
            var dataRange = sheet.Cells[$"A3:C{row - 1}"];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        private async Task AddEmployeeDataSheetAsync(ExcelWorksheet sheet, int count, CancellationToken cancellationToken)
        {
            // Add title
            sheet.Cells["A1"].Value = "Employee Data";
            sheet.Cells["A1:E1"].Merge = true;
            sheet.Cells["A1"].Style.Font.Size = 14;
            sheet.Cells["A1"].Style.Font.Bold = true;
            sheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            // Add headers
            sheet.Cells["A3"].Value = "ID";
            sheet.Cells["B3"].Value = "Name";
            sheet.Cells["C3"].Value = "Department";
            sheet.Cells["D3"].Value = "Salary";
            sheet.Cells["E3"].Value = "Date of Birth";
            sheet.Cells["A3:E3"].Style.Font.Bold = true;
            sheet.Cells["A3:E3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            sheet.Cells["A3:E3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Add data
            int row = 4;
            using var call = _employeeClient.GetEmployeesStream(new EmployeesRequest { Count = count });
            await foreach (var employee in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                sheet.Cells[$"A{row}"].Value = employee.Id;
                sheet.Cells[$"B{row}"].Value = employee.Name;
                sheet.Cells[$"C{row}"].Value = employee.Department;
                sheet.Cells[$"D{row}"].Value = employee.Salary;
                sheet.Cells[$"D{row}"].Style.Numberformat.Format = "$#,##0.00";
                sheet.Cells[$"E{row}"].Value = DateTime.Parse(employee.DateOfBirth);
                sheet.Cells[$"E{row}"].Style.Numberformat.Format = "yyyy-mm-dd";
                row++;

                // Add progress update every 1000 records
                if (row % 1000 == 0)
                {
                    Console.WriteLine($"Processed {row - 3:N0} employee records...");
                }
            }

            // Style the sheet
            var dataRange = sheet.Cells[$"A3:E{row - 1}"];
            dataRange.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            dataRange.Style.Border.Right.Style = ExcelBorderStyle.Thin;

            // Freeze the header row
            sheet.View.FreezePanes(3, 1);
        }

        public async Task<string> ExportEmployeesToExcelStreamingV2Async(int count, CancellationToken cancellationToken = default)
        {
            var excelPath = Path.Combine(_reportDirectory, $"EmployeeReport_Streaming_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            var processingStartTime = DateTime.UtcNow;
            var processedRecords = 0;
            var totalSalary = 0m;

            // Concurrent collections for real-time statistics
            var departmentStats = new ConcurrentDictionary<string, DepartmentStats>();
            var salaryRanges = new ConcurrentDictionary<string, int>();
            var ageGroups = new ConcurrentDictionary<string, int>();

            using var package = new ExcelPackage();

            // Create all sheets upfront
            var summarySheet = package.Workbook.Worksheets.Add("Summary");
            var deptSheet = package.Workbook.Worksheets.Add("Department Statistics");
            var salarySheet = package.Workbook.Worksheets.Add("Salary Distribution");
            var ageSheet = package.Workbook.Worksheets.Add("Age Distribution");
            var dataSheet = package.Workbook.Worksheets.Add("Employee Data");

            // Initialize headers for all sheets
            InitializeSheetHeaders(summarySheet, deptSheet, salarySheet, ageSheet, dataSheet);

            // Set up data sheet for streaming
            var dataRow = 4; // Start after headers
            var lastProgressUpdate = DateTime.Now;
            var progressInterval = TimeSpan.FromSeconds(1);
            var lastSaveTime = DateTime.Now;
            var saveInterval = TimeSpan.FromSeconds(5); // Save every 5 seconds instead of every 1000 records
            var batchSize = 100000; // Process in larger batches

            try
            {
                using var call = _employeeClient.GetEmployeesStream(new EmployeesRequest { Count = count });
                var responseStream = call.ResponseStream;
                var employeeBatch = new List<(EmployeeReply Employee, decimal Salary, DateTime DateOfBirth)>(batchSize);

                await foreach (var employee in responseStream.ReadAllAsync(cancellationToken))
                {
                    // Convert and process data
                    var salary = (decimal)employee.Salary;
                    var dateOfBirth = DateTime.Parse(employee.DateOfBirth);

                    // Add to batch
                    employeeBatch.Add((employee, salary, dateOfBirth));

                    // Update statistics
                    UpdateStatistics(employee.Department, salary, dateOfBirth,
                        ref processedRecords, ref totalSalary,
                        departmentStats, salaryRanges, ageGroups);

                    // Process batch when it reaches the batch size
                    if (employeeBatch.Count >= batchSize)
                    {
                        // Write batch to Excel
                        foreach (var (emp, sal, dob) in employeeBatch)
                        {
                            WriteEmployeeToSheet(dataSheet, dataRow, emp, sal, dob);
                            dataRow++;
                        }
                        employeeBatch.Clear();

                        // Update progress in console
                        var now = DateTime.Now;
                        if (now - lastProgressUpdate >= progressInterval)
                        {
                            var elapsed = now - processingStartTime;
                            var recordsPerSecond = processedRecords / elapsed.TotalSeconds;
                            Console.WriteLine($"Processed {processedRecords:N0} records... ({recordsPerSecond:N0} records/sec)");
                            lastProgressUpdate = now;
                        }

                        // Save periodically (every 5 seconds) instead of every batch
                        //if (now - lastSaveTime >= saveInterval)
                        //{
                        UpdateSummarySheet(summarySheet, count, processedRecords, totalSalary, processingStartTime);
                        UpdateDepartmentSheet(deptSheet, departmentStats, processedRecords);
                        UpdateSalarySheet(salarySheet, salaryRanges, processedRecords);
                        UpdateAgeSheet(ageSheet, ageGroups, processedRecords);
                        await package.SaveAsAsync(new FileInfo(excelPath), cancellationToken);
                        lastSaveTime = now;
                        Console.WriteLine($"Progress saved to: {excelPath}");
                        //}
                    }
                }

                // Process any remaining records
                if (employeeBatch.Any())
                {
                    foreach (var (emp, sal, dob) in employeeBatch)
                    {
                        WriteEmployeeToSheet(dataSheet, dataRow, emp, sal, dob);
                        dataRow++;
                    }
                }

                // Final update of all sheets
                UpdateSummarySheet(summarySheet, count, processedRecords, totalSalary, processingStartTime);
                UpdateDepartmentSheet(deptSheet, departmentStats, processedRecords);
                UpdateSalarySheet(salarySheet, salaryRanges, processedRecords);
                UpdateAgeSheet(ageSheet, ageGroups, processedRecords);

                // Auto-fit columns and save final version
                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    worksheet.Cells.AutoFitColumns();
                }
                await package.SaveAsAsync(new FileInfo(excelPath), cancellationToken);
                Console.WriteLine($"Final report saved to: {excelPath}");

                return excelPath;
            }
            catch (Exception ex)
            {
                // Add error to summary sheet
                summarySheet.Cells["A10"].Value = "Error";
                summarySheet.Cells["B10"].Value = ex.Message;
                summarySheet.Cells["A10:B10"].Style.Font.Color.SetColor(Color.Red);
                await package.SaveAsAsync(new FileInfo(excelPath), cancellationToken);
                Console.WriteLine($"Error occurred. Partial report saved to: {excelPath}");
                throw;
            }
        }

        private void InitializeSheetHeaders(ExcelWorksheet summarySheet, ExcelWorksheet deptSheet,
            ExcelWorksheet salarySheet, ExcelWorksheet ageSheet, ExcelWorksheet dataSheet)
        {
            // Summary Sheet
            summarySheet.Cells["A1"].Value = "Employee Report Summary (Streaming)";
            summarySheet.Cells["A1:D1"].Merge = true;
            summarySheet.Cells["A1"].Style.Font.Size = 16;
            summarySheet.Cells["A1"].Style.Font.Bold = true;
            summarySheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            summarySheet.Cells["A3"].Value = "Metric";
            summarySheet.Cells["B3"].Value = "Value";
            summarySheet.Cells["A3:B3"].Style.Font.Bold = true;
            summarySheet.Cells["A3:B3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            summarySheet.Cells["A3:B3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Department Sheet
            deptSheet.Cells["A1"].Value = "Department Statistics";
            deptSheet.Cells["A1:D1"].Merge = true;
            deptSheet.Cells["A1"].Style.Font.Size = 14;
            deptSheet.Cells["A1"].Style.Font.Bold = true;
            deptSheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            deptSheet.Cells["A3"].Value = "Department";
            deptSheet.Cells["B3"].Value = "Employee Count";
            deptSheet.Cells["C3"].Value = "Percentage";
            deptSheet.Cells["D3"].Value = "Average Salary";
            deptSheet.Cells["A3:D3"].Style.Font.Bold = true;
            deptSheet.Cells["A3:D3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            deptSheet.Cells["A3:D3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Salary Sheet
            salarySheet.Cells["A1"].Value = "Salary Distribution";
            salarySheet.Cells["A1:C1"].Merge = true;
            salarySheet.Cells["A1"].Style.Font.Size = 14;
            salarySheet.Cells["A1"].Style.Font.Bold = true;
            salarySheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            salarySheet.Cells["A3"].Value = "Salary Range";
            salarySheet.Cells["B3"].Value = "Count";
            salarySheet.Cells["C3"].Value = "Percentage";
            salarySheet.Cells["A3:C3"].Style.Font.Bold = true;
            salarySheet.Cells["A3:C3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            salarySheet.Cells["A3:C3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Age Sheet
            ageSheet.Cells["A1"].Value = "Age Distribution";
            ageSheet.Cells["A1:C1"].Merge = true;
            ageSheet.Cells["A1"].Style.Font.Size = 14;
            ageSheet.Cells["A1"].Style.Font.Bold = true;
            ageSheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            ageSheet.Cells["A3"].Value = "Age Group";
            ageSheet.Cells["B3"].Value = "Count";
            ageSheet.Cells["C3"].Value = "Percentage";
            ageSheet.Cells["A3:C3"].Style.Font.Bold = true;
            ageSheet.Cells["A3:C3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ageSheet.Cells["A3:C3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Data Sheet
            dataSheet.Cells["A1"].Value = "Employee Data (Streaming)";
            dataSheet.Cells["A1:E1"].Merge = true;
            dataSheet.Cells["A1"].Style.Font.Size = 14;
            dataSheet.Cells["A1"].Style.Font.Bold = true;
            dataSheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

            dataSheet.Cells["A3"].Value = "ID";
            dataSheet.Cells["B3"].Value = "Name";
            dataSheet.Cells["C3"].Value = "Department";
            dataSheet.Cells["D3"].Value = "Salary";
            dataSheet.Cells["E3"].Value = "Date of Birth";
            dataSheet.Cells["A3:E3"].Style.Font.Bold = true;
            dataSheet.Cells["A3:E3"].Style.Fill.PatternType = ExcelFillStyle.Solid;
            dataSheet.Cells["A3:E3"].Style.Fill.BackgroundColor.SetColor(Color.LightGray);

            // Freeze headers
            dataSheet.View.FreezePanes(3, 1);
        }

        private void WriteEmployeeToSheet(ExcelWorksheet sheet, int row, EmployeeReply employee, decimal salary, DateTime dateOfBirth)
        {
            sheet.Cells[$"A{row}"].Value = employee.Id;
            sheet.Cells[$"B{row}"].Value = employee.Name;
            sheet.Cells[$"C{row}"].Value = employee.Department;
            sheet.Cells[$"D{row}"].Value = salary;
            sheet.Cells[$"D{row}"].Style.Numberformat.Format = "$#,##0.00";
            sheet.Cells[$"E{row}"].Value = dateOfBirth;
            sheet.Cells[$"E{row}"].Style.Numberformat.Format = "yyyy-mm-dd";
        }

        private void UpdateStatistics(string department, decimal salary, DateTime dateOfBirth,
            ref int processedRecords, ref decimal totalSalary,
            ConcurrentDictionary<string, DepartmentStats> departmentStats,
            ConcurrentDictionary<string, int> salaryRanges,
            ConcurrentDictionary<string, int> ageGroups)
        {
            processedRecords++;
            totalSalary += salary;

            // Update department statistics
            departmentStats.AddOrUpdate(
                department,
                new DepartmentStats { Count = 1, TotalSalary = salary },
                (_, stats) => new DepartmentStats
                {
                    Count = stats.Count + 1,
                    TotalSalary = stats.TotalSalary + salary
                });

            // Update salary range statistics
            var salaryRange = GetSalaryRange(salary);
            salaryRanges.AddOrUpdate(salaryRange, 1, (_, count) => count + 1);

            // Update age group statistics
            var ageGroup = GetAgeGroup(dateOfBirth);
            ageGroups.AddOrUpdate(ageGroup, 1, (_, count) => count + 1);
        }

        private void UpdateSummarySheet(ExcelWorksheet sheet, int totalRecords, int processedRecords,
            decimal totalSalary, DateTime startTime)
        {
            var summaryData = new[]
            {
                new { Metric = "Total Records", Value = totalRecords.ToString("N0") },
                new { Metric = "Processed Records", Value = processedRecords.ToString("N0") },
                new { Metric = "Processing Time", Value = $"{DateTime.UtcNow - startTime:hh\\:mm\\:ss}" },
                new { Metric = "Average Salary", Value = (processedRecords > 0 ? totalSalary / processedRecords : 0).ToString("C") },
                new { Metric = "Total Salary", Value = totalSalary.ToString("C") },
                new { Metric = "Last Updated", Value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };

            for (int i = 0; i < summaryData.Length; i++)
            {
                sheet.Cells[$"A{i + 4}"].Value = summaryData[i].Metric;
                sheet.Cells[$"B{i + 4}"].Value = summaryData[i].Value;
            }
        }

        private void UpdateDepartmentSheet(ExcelWorksheet sheet, ConcurrentDictionary<string, DepartmentStats> stats, int totalRecords)
        {
            int row = 4;
            foreach (var dept in stats.OrderByDescending(d => d.Value.Count))
            {
                sheet.Cells[$"A{row}"].Value = dept.Key;
                sheet.Cells[$"B{row}"].Value = dept.Value.Count;
                sheet.Cells[$"C{row}"].Value = (double)dept.Value.Count / totalRecords;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                sheet.Cells[$"D{row}"].Value = dept.Value.TotalSalary / dept.Value.Count;
                sheet.Cells[$"D{row}"].Style.Numberformat.Format = "$#,##0.00";
                row++;
            }
        }

        private void UpdateSalarySheet(ExcelWorksheet sheet, ConcurrentDictionary<string, int> ranges, int totalRecords)
        {
            int row = 4;
            foreach (var range in ranges.OrderBy(r => r.Key))
            {
                sheet.Cells[$"A{row}"].Value = range.Key;
                sheet.Cells[$"B{row}"].Value = range.Value;
                sheet.Cells[$"C{row}"].Value = (double)range.Value / totalRecords;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                row++;
            }
        }

        private void UpdateAgeSheet(ExcelWorksheet sheet, ConcurrentDictionary<string, int> groups, int totalRecords)
        {
            int row = 4;
            foreach (var group in groups.OrderBy(a => a.Key))
            {
                sheet.Cells[$"A{row}"].Value = group.Key;
                sheet.Cells[$"B{row}"].Value = group.Value;
                sheet.Cells[$"C{row}"].Value = (double)group.Value / totalRecords;
                sheet.Cells[$"C{row}"].Style.Numberformat.Format = "0.0%";
                row++;
            }
        }
    }

    public class EmployeeReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalRecords { get; set; }
        public int ProcessedRecords { get; set; }
        public decimal TotalSalary { get; set; }
        public decimal AverageSalary { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public Dictionary<string, DepartmentReport> DepartmentStatistics { get; set; } = new();
        public Dictionary<string, SalaryRangeReport> SalaryDistribution { get; set; } = new();
        public Dictionary<string, AgeGroupReport> AgeDistribution { get; set; } = new();
        public string? Error { get; set; }
    }

    public class DepartmentReport
    {
        public int EmployeeCount { get; set; }
        public decimal AverageSalary { get; set; }
        public double PercentageOfTotal { get; set; }
    }

    public class SalaryRangeReport
    {
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class AgeGroupReport
    {
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class DepartmentStats
    {
        public int Count { get; set; }
        public decimal TotalSalary { get; set; }
    }
}