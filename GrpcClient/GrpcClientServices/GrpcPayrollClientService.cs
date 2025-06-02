using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcServer;

namespace GrpcClient.GrpcClientServices
{
    internal class GrpcPayrollClientService
    {
        private PayrollUploader.PayrollUploaderClient _payrollClient;

        public GrpcPayrollClientService(string grpcServerUrl)
        {
            var channel = GrpcChannel.ForAddress(grpcServerUrl);
            _payrollClient = new PayrollUploader.PayrollUploaderClient(channel);
        }

        public async Task UploadLargePayrollFileAsync(string filePath)
        {
            using var call = _payrollClient.UploadPayroll();

            using var fileStream = File.OpenRead(filePath);
            byte[] buffer = new byte[64 * 1024]; // 64KB buffer
            int bytesRead;
            bool isFirstChunk = true;

            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new PayrollChunk
                {
                    Data = ByteString.CopyFrom(buffer, 0, bytesRead)
                };

                if (isFirstChunk)
                {
                    chunk.FileName = Path.GetFileName(filePath);
                    isFirstChunk = false;
                }

                await call.RequestStream.WriteAsync(chunk);

                await Task.Delay(1000);
            }

            await call.RequestStream.CompleteAsync();
            var response = await call.ResponseAsync;

            Console.WriteLine($"Upload finished: {response.Success}, Message: {response.Message}");
        }

        public async Task DownloadLargeFileAsync(string fileName)
        {
            using var call = _payrollClient.DownloadPayroll(new FileRequest { FileName = fileName });

            FileMetadata metadata = null;
            long totalReceived = 0;
            FileStream output = null;

            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                if (response.PayloadCase == DownloadFileResponse.PayloadOneofCase.Metadata)
                {
                    metadata = response.Metadata;
                    Console.WriteLine($"Receiving file: {metadata.FileName}, size: {metadata.FileSize / (1024.0 * 1024.0):F2} MB");
                    Directory.CreateDirectory("Downloads");

                    output = File.Create(Path.Combine("Downloads", metadata.FileName));
                }
                else if (response.PayloadCase == DownloadFileResponse.PayloadOneofCase.Chunk)
                {
                    if (output == null)
                    {
                        Console.WriteLine("Received data before metadata. Aborting.");
                        break;
                    }

                    byte[] bytes = response.Chunk.Data.ToByteArray();
                    await output.WriteAsync(bytes);
                    totalReceived += bytes.Length;

                    DisplayProgressBar(metadata.FileSize, totalReceived);
                }
            }

            output?.Close();
            Console.WriteLine(" Download completed.");
        }

        private void DisplayProgressBar(long total, long current)
        {
            const int barLength = 50;
            double percent = (double)current / total;
            int filled = (int)(percent * barLength);

            string bar = "[" + new string('#', filled) + new string('-', barLength - filled) + $"] {percent:P0}";
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(bar);
        }
    }
}