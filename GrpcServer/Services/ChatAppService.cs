using Grpc.Core;
using System.Threading;

namespace GrpcServer.Services
{
    public class ChatAppService : ChatService.ChatServiceBase
    {
        private readonly ILogger<ChatAppService> _logger;
        private static IServerStreamWriter<ChatMessage> _clientStream;
        private static CancellationToken _cancellationToken;

        public ChatAppService(ILogger<ChatAppService> logger)
        {
            _logger = logger;
        }

        public override async Task ChatStream(
         IAsyncStreamReader<ChatMessage> requestStream,
         IServerStreamWriter<ChatMessage> responseStream,
         ServerCallContext context)
        {
            _clientStream = responseStream;
            _cancellationToken = context.CancellationToken;

            // Handle incoming messages from client
            _ = Task.Run(async () =>
            {
                await foreach (var message in requestStream.ReadAllAsync(_cancellationToken))
                {
                    Console.WriteLine($"Client [{message.User}]: {message.Message}");
                }
            });

            // Handle server-side console input
            while (!_cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"You: ");
                var input = Console.ReadLine();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    var serverMessage = new ChatMessage
                    {
                        User = "Server",
                        Message = input,
                    };

                    await _clientStream.WriteAsync(serverMessage);
                }
            }
        }
    }
}