using EchidnaJav.Core.Infrastructure.Services;

namespace EchidnaJav
{
    public class AppPaths : IAppPaths
    {
        public string AppDataDirectory => FileSystem.AppDataDirectory;
    }
}
