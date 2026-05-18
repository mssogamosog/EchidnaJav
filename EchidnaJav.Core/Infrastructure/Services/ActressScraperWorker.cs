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
                    var scrapeService = scope.ServiceProvider.GetRequiredService<IScrapeActressService>();

                    _logger.LogInformation("🔍 Background scraping data for: {ActressName}", actressName);

                    var scrapedData = new ActressData { Name = actressName };

                    await scrapeService.ScrapeActressAsync(scrapedData, LanguageType.English);

                    _logger.LogInformation("✅ Finished updating: {ActressName}", actressName);
                }
                catch (OperationCanceledException)
                {
                    break; // Application is shutting down gracefully
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
