
using EchidnaJav.Core.Domain.States;
using System.Threading.Channels;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public class MovieScrapeRequest
    {
        public string MovieId { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public IReadOnlyList<string> Files { get; set; } = Array.Empty<string>();
    }

    public interface IMovieScrapeQueue
    {
        ValueTask QueueMovieAsync(MovieScrapeRequest request);
        IAsyncEnumerable<MovieScrapeRequest> ReadAllAsync(CancellationToken ct);
    }

    public class MovieScrapeQueue : IMovieScrapeQueue
    {
        private readonly Channel<MovieScrapeRequest> _queue;
        private readonly ScraperMovieState _state;
        public MovieScrapeQueue(ScraperMovieState state)
        {
            _state = state;
            _queue = Channel.CreateUnbounded<MovieScrapeRequest>();
        }
        public async ValueTask QueueMovieAsync(MovieScrapeRequest request)
        {
            _state.AddToQueue();
            await _queue.Writer.WriteAsync(request);
        }

        public IAsyncEnumerable<MovieScrapeRequest> ReadAllAsync(CancellationToken ct)
        {
            return _queue.Reader.ReadAllAsync(ct);
        }
    }
}
