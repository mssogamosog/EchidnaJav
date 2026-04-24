using CommunityToolkit.Maui;
using EchidnaJav.Core.Domain.States;
using EchidnaJav.Core.Infrastructure.Mappers;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SQLitePCL;

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
            builder.Services.AddSingleton<IAppPaths, AppPaths>();
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
            {
                var dbPath = Path.Combine(AppContext.BaseDirectory, "echidnajav.db");
                options.UseSqlite($"Data Source={dbPath}");
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

                db.Database.EnsureDeleted();   // 🧨 drops DB
                db.Database.EnsureCreated();  // 🧱 recreates schema
            }

            return app;
        }
    }
}