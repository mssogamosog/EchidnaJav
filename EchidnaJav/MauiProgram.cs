using CommunityToolkit.Maui;
using EchidnaJav.Core.Domain.States;
using EchidnaJav.Core.Infrastructure.FileSystem;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Mappers;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper;
using EchidnaJav.Scraper.Interfaces;
using EchidnaJav.Scraper.Services;
using EchidnaJav.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SQLitePCL;
namespace EchidnaJav
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // ✅ Initialize native SQLite wrapper safely across platforms
            Batteries.Init();

            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // Ensure this is only called once
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            #region Core Infrastructure & State DI

            builder.Services.AddSingleton<IAppPaths, AppPaths>();
            builder.Services.AddScoped<IFileUtilityService, FileUtilityService>();
            builder.Services.AddScoped<IMovieIdService, MovieIdService>();
            builder.Services.AddScoped<IImportService, ImportService>();
            builder.Services.AddScoped<IMovieDbMapper, MovieDbMapper>();
            builder.Services.AddScoped<INavigationStateService, NavigationStateService>();
            builder.Services.AddScoped<ILocalMediaScanner, LocalMediaScanner>();
            builder.Services.AddScoped<IPlaybackService, PlaybackService>();
            builder.Services.AddScoped<INativeDialogService, MauiNativeDialogService>();

            // UI & Runtime States
            builder.Services.AddSingleton<ImportState>();
            builder.Services.AddSingleton<AppPreferencesState>();
            builder.Services.AddSingleton<TransientUIState>();
            builder.Services.AddSingleton<SearchFilterState>();
            builder.Services.AddSingleton<NavigationCacheState>();
            builder.Services.AddSingleton<ScraperActressState>();
            builder.Services.AddSingleton<ScraperMovieState>();

            #endregion

            #region Database & Repositories

            builder.Services.AddDbContextFactory<AppDbContext>(options =>
            {
                var dbPath = Path.Combine(FileSystem.AppDataDirectory, "echidnajav.db");
                options.UseSqlite($"Data Source={dbPath};Cache=Shared;");
            });

            builder.Services.AddScoped<IMovieRepositoryService, MovieRepositoryService>();
            builder.Services.AddScoped<IActressRepositoryService, ActressRepositoryService>();

            #endregion

            #region HTTP & Image Services

            builder.Services.AddHttpClient();
            builder.Services.AddHttpClient<IImageService, ImageService>();
            builder.Services.AddScoped<IImageService, ImageService>();

            #endregion

            #region Scraping Pipelines & Workers

            // 1. NFO Parsers & Generators
            builder.Services.AddScoped<INfoParserService, NfoParserService>();
            builder.Services.AddScoped<INfoGeneratorService, NfoGeneratorService>();

            // 2. Actress Scraping Engine
            builder.Services.AddScoped<IScrapeActressService, ActressScrapeService>();
            builder.Services.AddScoped<IActressScraper, ActressJavDatabase>();
            builder.Services.AddScoped<IActressScraper, ActressJavModel>();

            builder.Services.AddSingleton<IActressScrapeQueue, ActressScrapeQueue>();
            builder.Services.AddHostedService<ActressScraperWorker>();

            // 3. Movie Scraping Engine (FIXED: Added missing orchestrator and modules)
            builder.Services.AddScoped<IMovieScrapeService, MovieScrapeService>();
            builder.Services.AddTransient<MovieJavDatabase>();
            builder.Services.AddTransient<MovieSupJav>();
            builder.Services.AddTransient<MovieJavTiful>();
            builder.Services.AddTransient<MovieMissAv>();
            builder.Services.AddTransient<MovieJavLibrary>();
            builder.Services.AddTransient<MovieR18Dev>();

            builder.Services.AddSingleton<ISilentWebViewSandbox, SilentWebViewSandbox>();
            // Add your other specific movie scrapers here if needed (e.g., MovieR18Dev, MovieSupJav)

            builder.Services.AddSingleton<IMovieScrapeQueue, MovieScrapeQueue>();
            builder.Services.AddHostedService<MovieScraperWorker>();

            #endregion

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            #region Database Initialization

            using (var scope = app.Services.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                using var db = factory.CreateDbContext();

                // Ensure schema is fully created on app boot
                db.Database.EnsureCreated();
            }

            #endregion

            #region Background Worker Booting (Safe Threading)

            // OPTIMIZATION: Boot background workers safely off the UI thread
            Task.Run(async () =>
            {
                var hostedServices = app.Services.GetServices<IHostedService>();

                var actressWorker = hostedServices.OfType<ActressScraperWorker>().FirstOrDefault();
                if (actressWorker != null)
                    await actressWorker.StartAsync(CancellationToken.None);

                var movieWorker = hostedServices.OfType<MovieScraperWorker>().FirstOrDefault();
                if (movieWorker != null)
                    await movieWorker.StartAsync(CancellationToken.None);
            });

            #endregion

            return app;
        }
    }
}