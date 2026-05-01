using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IActressScrapeQueue
    {
        ValueTask QueueActressAsync(string actressName);
        ValueTask<string> DequeueAsync(CancellationToken cancellationToken);
    }

    public class ActressScrapeQueue : IActressScrapeQueue
    {
        private readonly Channel<string> _queue;

        public ActressScrapeQueue()
        {
            // Unbounded means it can hold as many actresses as you find
            _queue = Channel.CreateUnbounded<string>();
        }

        public async ValueTask QueueActressAsync(string actressName)
        {
            await _queue.Writer.WriteAsync(actressName);
        }

        public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
