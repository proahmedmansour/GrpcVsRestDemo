using Grpc.Core;
using Grpc.Net.Client;
using GrpcServer;

namespace GrpcClient.GrpcClientServices
{
    internal class ChatClientService
    {
        private ChatService.ChatServiceClient _client;

        public ChatClientService(string grpcServerUrl)
        {
            var channel = GrpcChannel.ForAddress(grpcServerUrl);
            _client = new ChatService.ChatServiceClient(channel);
        }

        public async Task ChatAsync()
        {
            using var chat = _client.ChatStream();

            // Send messages
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    Console.WriteLine("You: ");
                    var input = Console.ReadLine();
                    if (input == "exit") break;

                    await chat.RequestStream.WriteAsync(new ChatMessage
                    {
                        User = "ClientUser",
                        Message = input,
                    });
                }

                await chat.RequestStream.CompleteAsync();
            });

            // Read incoming server messages
            await foreach (var message in chat.ResponseStream.ReadAllAsync())
            {
                Console.WriteLine($"[{message.User}] {message.Message}");
            }
        }
    }
}