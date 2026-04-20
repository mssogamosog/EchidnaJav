using EchidnaJav.Infrastructure.Persistence;
using EchidnaJav.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
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
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "echidnajav.db");

            builder.Services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));

            // ✅ DI
            builder.Services.AddScoped<IMovieIdService, MovieIdService>();
            builder.Services.AddScoped<IImportService, ImportService>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            // 🔥 THIS WAS MISSING (creates tables)
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.EnsureCreated();
            }

            return app;
        }
    }
}