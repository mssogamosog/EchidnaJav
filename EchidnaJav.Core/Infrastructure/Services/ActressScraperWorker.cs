using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Domain.States;
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
        private readonly ScraperActressState _state;

        public ActressScraperWorker(
            IActressScrapeQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<ActressScraperWorker> logger,
            ScraperActressState scraper)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _state = scraper;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var actressName = await _queue.DequeueAsync(stoppingToken);

                    using var scope = _scopeFactory.CreateScope();
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                    var scrapeService = scope.ServiceProvider.GetRequiredService<IScrapeActressService>();
                    var imageService = scope.ServiceProvider.GetRequiredService<IImageService>();

                    using var db = dbFactory.CreateDbContext();

                    var dbActress = await db.Actresses.FirstOrDefaultAsync(a => a.Name == actressName, stoppingToken);

                    if (dbActress == null) continue;

                    _logger.LogInformation($"🔍 Background scraping data for: {actressName}");

                    var scrapedData = new ActressData { Name = actressName };

                    await scrapeService.ScrapeActressAsync(scrapedData, LanguageType.English);

                   
                    _logger.LogInformation($"✅ Finished updating: {actressName}");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in background scraper worker.");
                }
                finally
                {
                    _state.MarkAsProcessed();
                }
            }
        }
    }
}
