using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public class MovieScraperWorker : BackgroundService
    {
        private readonly IMovieScrapeQueue _movieQueue;
        private readonly IActressScrapeQueue _actressQueue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MovieScraperWorker> _logger;

        public MovieScraperWorker(
            IMovieScrapeQueue movieQueue,
            IActressScrapeQueue actressQueue,
            IServiceScopeFactory scopeFactory,
            ILogger<MovieScraperWorker> logger)
        {
            _movieQueue = movieQueue;
            _actressQueue = actressQueue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Background Movie Scraper Started.");

            try
            {
                await foreach (var request in _movieQueue.ReadAllAsync(stoppingToken))
                {
                    _logger.LogInformation($"🔍 Background processing online import for: {request.MovieId}");

                    // 1. Create a clean DI scope per message loop
                    using var scope = _scopeFactory.CreateScope();
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

                    // Notice we inject the specific IMovieScrapeService (DTO Engine) and IMovieRepositoryService (EF Engine)
                    var movieScraper = scope.ServiceProvider.GetRequiredService<IMovieScrapeService>();
                    var movieRepo = scope.ServiceProvider.GetRequiredService<IMovieRepositoryService>();
                    var nfoGenerator = scope.ServiceProvider.GetRequiredService<INfoGeneratorService>();

                    // Spin up an isolated, short-lived DbContext instance just for initial pre-checks
                    using var db = dbFactory.CreateDbContext();

                    try
                    {
                        // 2. Pre-check: Ensure it wasn't already processed while sitting in the queue
                        if (await db.Movies.AnyAsync(m => m.Id == request.MovieId, stoppingToken))
                        {
                            _logger.LogInformation($"⏩ Movie {request.MovieId} already exists in database. Skipping scrape.");
                            continue;
                        }

                        // Define the absolute path where the downloaded cover should reside alongside the video
                        string targetCoverPath = Path.Combine(request.TargetDirectory, $"{request.MovieId}-cover.jpg");

                        // 3. Scrape Web Data -> Returns pure, unlinked XML/DTO Metadata
                        MovieMetadata scrapedDto = await movieScraper.ScrapeMovieAsync(
                            request.MovieId,
                            targetCoverPath,
                            LanguageType.English);

                        if (scrapedDto == null)
                        {
                            _logger.LogWarning($"⚠️ Online metadata not found or unacceptable for {request.MovieId}");
                            continue;
                        }

                        // 4. Persist to Relational DB -> Resolves keys, updates join tables, creates missing stubs
                        // This method safely handles its own DbContext lifecycles internally via the injected factory
                        Movie savedEntity = await movieRepo.UpsertScrapedMovieAsync(scrapedDto, targetCoverPath , request.Files);

                        // 5. Trigger Secondary Discovery Queues for newly scraped actresses
                        if (scrapedDto.Actors != null && scrapedDto.Actors.Any())
                        {
                            foreach (var actorDto in scrapedDto.Actors)
                            {
                                if (string.IsNullOrWhiteSpace(actorDto.Name)) continue;

                                // Push newly discovered actresses to the background queue to fetch measurements/DOB later
                                await _actressQueue.QueueActressAsync(actorDto.Name);
                            }
                        }

                        // 6. Generate the decoupled .nfo XML directly alongside the media file
                        await nfoGenerator.GenerateNfoAsync(savedEntity.Id, request.TargetDirectory);

                        _logger.LogInformation($"✅ Finished online import & generated NFO safely for: {request.MovieId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Failed processing background import for {request.MovieId}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 Background Movie Scraper shutting down gracefully.");
            }
        }
    }
}