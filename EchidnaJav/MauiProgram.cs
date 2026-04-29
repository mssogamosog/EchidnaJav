using CommunityToolkit.Maui;
using EchidnaJav.Core.Domain.States;
using EchidnaJav.Core.Infrastructure.FileSystem;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Mappers;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper;
using EchidnaJav.Scraper.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLitePCL;
using System.Diagnostics;

namespace EchidnaJav
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // ✅ Initialize SQLite
            Batteries.Init();

            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // ✅ only once
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // ✅ Correct DB path
           

            // ✅ DI
            builder.Services.AddScoped<IMovieIdService, MovieIdService>();
            builder.Services.AddScoped<IImportService, ImportService>();
            builder.Services.AddScoped<IMovieDbMapper, MovieDbMapper>();
            builder.Services.AddScoped<IImageService, ImageService>();
            builder.Services.AddSingleton<ImportState>();
            builder.Services.AddSingleton<UIState>();
            builder.Services.AddScoped<SearchState>();
            builder.Services.AddSingleton<IAppPaths, AppPaths>();
            builder.Services.AddScoped<IMovieRepositoryService, MovieRepositoryService>();
            builder.Services.AddScoped<INavigationStateService, NavigationStateService>();
            builder.Services.AddScoped<ILocalMediaScanner, LocalMediaScanner>();
            builder.Services.AddScoped<INfoParserService, NfoParserService>();
            builder.Services.AddScoped<IPlaybackService, PlaybackService>();
            builder.Services.AddScoped<IActressRepositoryService, ActressRepositoryService>();
            builder.Services.AddScoped<IScrapeService, ScrapeService>();
            builder.Services.AddScoped<IActressScraper, ActressJavDatabase>();
            builder.Services.AddScoped<IActressScraper, ActressJavModel>();
            builder.Services.AddScoped<IFileUtilityService, FileUtilityService>();
            builder.Services.AddHttpClient<IImageService, ImageService>();
            builder.Services.AddHttpClient();
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
            {
                var dbPath = Path.Combine(FileSystem.AppDataDirectory, "echidnajav.db");
                options.UseSqlite($"Data Source={dbPath}");
                //Process.Start("explorer.exe", FileSystem.AppDataDirectory);
            });
            

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();


            using(var scope = app.Services.CreateScope())
            {
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                using var db = factory.CreateDbContext();

                //db.Database.EnsureDeleted();   // 🧨 drops DB
                db.Database.EnsureCreated();  // 🧱 recreates schema
                //db.Database.Migrate();
            }

            return app;
        }
    }
}