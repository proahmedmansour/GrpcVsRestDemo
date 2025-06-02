using GrpcClient.GrpcClientServices;
using OfficeOpenXml;

ExcelPackage.License.SetNonCommercialPersonal("Mansour"); //This will also set the Author property to the name provided in the argument.

Console.WriteLine("Starting gRPC client...");
string serverUrl = "http://grpc-server:5011";
var grpcService = new GrpcEmployeeClientService(serverUrl);

try
{
    Console.WriteLine("Exporting employee data to Excel (Streaming)...");
    var excelPath = await grpcService.ExportEmployeesToExcelStreamingV2Async(1000000);
    Console.WriteLine($"Excel report generated successfully: {excelPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}

Console.WriteLine("\nDone. Press Enter to exit.");
Console.ReadLine();