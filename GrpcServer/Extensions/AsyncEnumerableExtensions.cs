using System.Collections.Generic;

namespace GrpcServer.Extensions
{
    public static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<List<T>> Buffer<T>(this IAsyncEnumerable<T> source, int size)
        {
            var buffer = new List<T>(size);
            await foreach (var item in source)
            {
                buffer.Add(item);
                if (buffer.Count >= size)
                {
                    yield return buffer;
                    buffer = new List<T>(size);
                }
            }

            if (buffer.Count > 0)
            {
                yield return buffer;
            }
        }
    }
} 