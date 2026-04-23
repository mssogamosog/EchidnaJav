using EchidnaJav.Core.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace EchidnaJav
{
    public class AppPaths : IAppPaths
    {
        public string AppDataDirectory => FileSystem.AppDataDirectory;
    }
}
