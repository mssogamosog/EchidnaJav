using EchidnaJav.Core.Domain.States;
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
        private readonly ScraperActressState _state;

        public ActressScrapeQueue(ScraperActressState state)
        {
            _state = state;
            _queue = Channel.CreateUnbounded<string>();
        }

        public async ValueTask QueueActressAsync(string actressName)
        {
            _state.AddToQueue();
            await _queue.Writer.WriteAsync(actressName);
        }

        public async ValueTask<string> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
