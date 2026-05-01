using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public class ActressScraperWorker : BackgroundService
    {
        private readonly IActressScrapeQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ActressScraperWorker> _logger;

        public ActressScraperWorker(
            IActressScrapeQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<ActressScraperWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _logger.LogInformation("🚀 Background Actress Scraper Started.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Background Actress Scraper Started.");

            // Keep running until the app shuts down
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // This will wait here patiently until a name is added to the queue
                    var actressName = await _queue.DequeueAsync(stoppingToken);

                    // 1. Create a fresh scope for DB and Scrape Service
                    using var scope = _scopeFactory.CreateScope();
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                    var scrapeService = scope.ServiceProvider.GetRequiredService<IScrapeService>();
                    var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();

                    using var db = dbFactory.CreateDbContext();

                    // 2. Fetch the "shell" actress that the ImportService saved
                    var dbActress = await db.Actresses.FirstOrDefaultAsync(a => a.Name == actressName, stoppingToken);

                    if (dbActress == null) continue;

                    _logger.LogInformation($"🔍 Background scraping data for: {actressName}");

                    var scrapedData = new ActressData { Name = actressName };

                    // 3. Scrape the web (this takes a few seconds)
                    await scrapeService.ScrapeActressAsync(scrapedData, LanguageType.English);

                    // 4. Update the DB record with the newly found data
                    // (Assuming ScrapeActressAsync populates your properties. Update mapping as needed)
                    dbActress.JapaneseName = scrapedData.JapaneseName;
                    dbActress.DobYear = scrapedData.DobYear;
                    dbActress.Height = scrapedData.Height;
                    dbActress.Bust = scrapedData.Bust;
                    dbActress.Waist = scrapedData.Waist;
                    dbActress.Hips = scrapedData.Hips;
                    dbActress.Cup = scrapedData.Cup;

                    // You might also need to process/download her images here using the imageService

                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation($"✅ Finished updating: {actressName}");
                }
                catch (OperationCanceledException)
                {
                    // Prevent throwing if app is shutting down
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in background scraper worker.");
                }
            }
        }
    }
}
