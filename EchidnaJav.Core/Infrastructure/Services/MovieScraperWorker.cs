using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.Entities;
using EchidnaJav.Core.Domain.States; // <-- Required for ScraperMovieState access
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Persistence;
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
        private readonly ScraperMovieState _state;

        public MovieScraperWorker(
            IMovieScrapeQueue movieQueue,
            IActressScrapeQueue actressQueue,
            IServiceScopeFactory scopeFactory,
            ILogger<MovieScraperWorker> logger,
            ScraperMovieState state)
        {
            _movieQueue = movieQueue;
            _actressQueue = actressQueue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _state = state;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Background Movie Scraper Started.");

            try
            {
                await foreach (var request in _movieQueue.ReadAllAsync(stoppingToken))
                {
                    _logger.LogInformation($"🔍 Background processing online import for: {request.MovieId}");

                    // Create a clean DI scope per message loop
                    using var scope = _scopeFactory.CreateScope();
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

                    var movieScraper = scope.ServiceProvider.GetRequiredService<IMovieScrapeService>();
                    var movieRepo = scope.ServiceProvider.GetRequiredService<IMovieRepositoryService>();
                    var nfoGenerator = scope.ServiceProvider.GetRequiredService<INfoGeneratorService>();

                    using var db = dbFactory.CreateDbContext();

                    // 2. Wrap task operations inside a dedicated try/finally pipeline
                    try
                    {
                        // Pre-check: Ensure it wasn't already processed while sitting in the queue
                        if (await db.Movies.AnyAsync(m => m.Id == request.MovieId, stoppingToken))
                        {
                            _logger.LogInformation($"⏩ Movie {request.MovieId} already exists in database. Skipping scrape.");
                            continue;
                        }

                        string targetCoverPath = Path.Combine(request.TargetDirectory, $"{request.MovieId}-cover.jpg");

                        // Scrape Web Data
                        MovieMetadata scrapedDto = await movieScraper.ScrapeMovieAsync(
                            request.MovieId,
                            targetCoverPath,
                            LanguageType.English);

                        if (scrapedDto == null)
                        {
                            _logger.LogWarning($"⚠️ Online metadata not found or unacceptable for {request.MovieId}");
                            continue;
                        }

                        // Persist to Relational DB
                        Movie savedEntity = await movieRepo.UpsertScrapedMovieAsync(scrapedDto, targetCoverPath, request.Files);

                        // Trigger Secondary Discovery Queues for newly scraped actresses
                        if (scrapedDto.Actors != null && scrapedDto.Actors.Any())
                        {
                            foreach (var actorDto in scrapedDto.Actors)
                            {
                                if (string.IsNullOrWhiteSpace(actorDto.Name)) continue;

                                await _actressQueue.QueueActressAsync(actorDto.Name);
                            }
                        }

                        // Generate the decoupled .nfo XML directly alongside the media file
                        await nfoGenerator.GenerateNfoAsync(savedEntity.Id, request.TargetDirectory);
                        string nfoFilePath = Path.Combine(request.TargetDirectory, $"{savedEntity.Id}.nfo");

                        if (File.Exists(nfoFilePath))
                        {
                            // Ensure we don't accidentally add duplicates
                            bool nfoExistsInDb = await db.Files.AnyAsync(f =>
                                f.MovieId == savedEntity.Id && f.FilePath == nfoFilePath, stoppingToken);

                            if (!nfoExistsInDb)
                            {
                                var nfoInfo = new FileInfo(nfoFilePath);

                                db.Files.Add(new FileEntry
                                {
                                    MovieId = savedEntity.Id,
                                    FileName = nfoInfo.Name,
                                    FilePath = nfoInfo.FullName,
                                    SizeBytes = nfoInfo.Length,
                                    LastModified = nfoInfo.LastWriteTimeUtc,
                                    IsScanned = true,
                                    Hash = string.Empty
                                });

                                await db.SaveChangesAsync(stoppingToken);
                            }
                        }

                        _logger.LogInformation($"✅ Finished online import & generated NFO safely for: {request.MovieId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ Failed processing background import for {request.MovieId}");
                    }
                    finally
                    {
                        _state.MarkAsProcessed();
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