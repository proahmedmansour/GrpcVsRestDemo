using Google.Protobuf;
using Grpc.Core;

namespace GrpcServer.Services
{
    public class PayrollService : PayrollUploader.PayrollUploaderBase
    {
        private readonly ILogger<PayrollService> _logger;

        public PayrollService(ILogger<PayrollService> logger)
        {
            _logger = logger;
        }

        public override async Task<UploadStatus> UploadPayroll(IAsyncStreamReader<PayrollChunk> requestStream, ServerCallContext context)
        {
            string filePath = null;
            using var memoryStream = new MemoryStream();

            try
            {
                await foreach (var chunk in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    // First chunk with file name
                    if (filePath == null && !string.IsNullOrEmpty(chunk.FileName))
                    {
                        Directory.CreateDirectory("Uploads");
                        filePath = Path.Combine("Uploads", chunk.FileName);
                    }

                    await memoryStream.WriteAsync(chunk.Data.ToByteArray(), context.CancellationToken);
                    _logger.LogInformation("Received chunk of size {Size} bytes", chunk.Data.Length);
                }

                if (filePath != null)
                {
                    await File.WriteAllBytesAsync(filePath, memoryStream.ToArray(), context.CancellationToken);
                    return new UploadStatus { Success = true, Message = "Upload complete" };
                }

                return new UploadStatus { Success = false, Message = "No data received" };
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Upload was canceled by the client (CancellationToken).");
                return new UploadStatus { Success = false, Message = "Upload canceled by client." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during payroll upload.");
                return new UploadStatus { Success = false, Message = "Unexpected server error occurred." };
            }
        }

        public override async Task DownloadPayroll(
         FileRequest request,
         IServerStreamWriter<DownloadFileResponse> responseStream,
         ServerCallContext context)
        {
            string filePath = Path.Combine("Uploads", request.FileName);

            if (!File.Exists(filePath))
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));

            var fileInfo = new FileInfo(filePath);
            long totalSize = fileInfo.Length;

            // Send metadata first
            var metadata = new FileMetadata
            {
                FileName = fileInfo.Name,
                FileSize = totalSize
            };

            await responseStream.WriteAsync(new DownloadFileResponse
            {
                Metadata = metadata
            });

            // Now send file chunks
            const int bufferSize = 64 * 1024; // 64KB
            byte[] buffer = new byte[bufferSize];
            long totalSent = 0;

            try
            {
                using var stream = File.OpenRead(filePath);
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, context.CancellationToken)) > 0)
                {
                    var chunk = new FileChunk
                    {
                        Data = ByteString.CopyFrom(buffer, 0, bytesRead)
                    };

                    await responseStream.WriteAsync(new DownloadFileResponse
                    {
                        Chunk = chunk
                    });

                    totalSent += bytesRead;
                    double percent = (double)totalSent / totalSize * 100;
                    _logger.LogInformation("Sent {Bytes}/{TotalBytes} ({Percent:F2}%)", totalSent, totalSize, percent);
                }

                _logger.LogInformation("Finished streaming file {FileName}", fileInfo.Name);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Client canceled the download.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file stream.");
                throw new RpcException(new Status(StatusCode.Internal, "File streaming failed."));
            }
        }
    }
}