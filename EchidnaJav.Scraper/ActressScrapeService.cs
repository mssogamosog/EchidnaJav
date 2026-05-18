using EchidnaJav.Core.Domain.DTOs;
using EchidnaJav.Core.Infrastructure.FileSystem;
using EchidnaJav.Core.Infrastructure.Interfaces;
using EchidnaJav.Core.Infrastructure.Persistence;
using EchidnaJav.Core.Infrastructure.Services;
using EchidnaJav.Scraper.Helpers;
using EchidnaJav.Scraper.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;


namespace EchidnaJav.Scraper
{
   

    public class ActressScrapeService : IScrapeActressService
    {
        private readonly IEnumerable<IActressScraper> _scrapers;
        private readonly ILogger<ActressScrapeService> _logger;
        private readonly IActressRepositoryService _actressRepoService;
        private readonly IFileUtilityService _fileService;
        private readonly IAppPaths _appPaths;
        private readonly IImageService _imageService;
        private readonly string _cacheFolder;

        // DI automatically hands you everything you need
        public ActressScrapeService(
            IEnumerable<IActressScraper> scrapers,
            ILogger<ActressScrapeService> logger,
            IActressRepositoryService actressRepoService,
            IFileUtilityService fileService,
            IAppPaths appPaths,
            IImageService imageService)
        {
            _scrapers = scrapers;
            _logger = logger;
            _actressRepoService = actressRepoService;
            _fileService = fileService;
            _imageService = imageService;
            _appPaths = appPaths;
            _cacheFolder = Path.Combine(_appPaths.AppDataDirectory, "actress-cache");
        }
        public async Task<ActressData> ScrapeActressAsync(ActressData actressData, LanguageType language)
        {
            var existingDbData = await _actressRepoService.GetActressDataForScraperAsync(actressData.Name);

            if (existingDbData != null)
            {
                if (existingDbData.ImageFileNames != null && existingDbData.ImageFileNames.Count > 0)
                {
                    return existingDbData;
                }

                MergeActressData(actressData, existingDbData);
                _logger.LogInformation("🔄 Attempting to scrape missing cover image for existing actress: {Name}", actressData.Name);
            }
            else
            {
                _logger.LogInformation("✨ Scraping brand new actress: {Name}", actressData.Name);
            }

          
            foreach (var scraper in _scrapers)
            {
                await ScrapeActressModuleAsync(scraper, actressData, language);
            }

            if (actressData.ImageFileNames.Count > 0)
            {
                actressData.ImageFileNames = _fileService.DeleteDuplicateFiles(_cacheFolder, actressData.ImageFileNames);
                actressData.ImageIndex = Math.Min(actressData.ImageIndex, actressData.ImageFileNames.Count - 1);
            }

            if (ScraperParsingExtensions.IsActressWorthShowing(actressData) == false)
                _logger.LogWarning("Unable to find online information for " + actressData.Name + " or aliases");
            else
                _logger.LogInformation("Found information for " + actressData.Name);

            // 7. Save to DB (Your Repo handles the Upsert)
            await _actressRepoService.SaveScrapedActressAsync(actressData);

            return actressData;
        }

        private async Task<ActressData> ScrapeActressModuleAsync(IActressScraper module, ActressData actressData, LanguageType language)
        {
           // if (IsActressDataComplete(actressData))
               // return actressData;

            // Pass the runtime data into the module
            await module.ScrapeAsync(actressData.Name, language);

            await DownloadActressImageAsync(actressData, module);
            MergeActressData(actressData, module.Actress);

            return actressData;
        }
        private async Task DownloadActressImageAsync(ActressData actressData, IActressScraper module)
        {
            if (!string.IsNullOrEmpty(module.ImageSource))
            {
                string? finalPath = await _imageService.DownloadImageAsync(_cacheFolder, module.ImageSource);
                if (!string.IsNullOrEmpty(finalPath))
                {
                    actressData.ImageFileNames.Add(finalPath);
                }
            }
        }
        private void MergeActressData(ActressData a, ActressData b)
        {
            if (a.Cup == "?")
                a.Cup = "";
            if (b == null)
                return;
            a.ImageFileNames.Concat(b.ImageFileNames);
            if (IsActressDataComplete(a))
                return;
            if (String.IsNullOrEmpty(a.JapaneseName))
                a.JapaneseName = b.JapaneseName;
            foreach (string altName in b.AltNames)
            {
                if (ScraperParsingExtensions.Equals(altName, a.AltNames))
                    a.AltNames.Add(altName);
            }
            if (a.DobYear == 0)
                a.DobYear = b.DobYear;
            if (a.DobMonth == 0)
                a.DobMonth = b.DobMonth;
            if (a.DobDay == 0)
                a.DobDay = b.DobDay;
            if (a.Height < 50)
                a.Height = b.Height;
            if (String.IsNullOrEmpty(a.Cup))
                a.Cup = b.Cup;
            if (a.Bust == 0)
                a.Bust = b.Bust;
            if (a.Waist == 0)
                a.Waist = b.Waist;
            if (a.Hips == 0)
                a.Hips = b.Hips;
            if (String.IsNullOrEmpty(a.BloodType))
                a.BloodType = b.BloodType;
        }
        private bool IsActressDataComplete(ActressData actressData)
        {
            if (actressData == null)
                return false;
            if (actressData.ImageFileNames.Count == 0)
                return false;
            if (String.IsNullOrEmpty(actressData.Name))
                return false;
            if (String.IsNullOrEmpty(actressData.JapaneseName))
                return false;
            if (actressData.DobYear == 0)
                return false;
            if (actressData.DobMonth == 0)
                return false;
            if (actressData.DobDay == 0)
                return false;
            if (actressData.Height == 0)
                return false;
            if (String.IsNullOrEmpty(actressData.Cup))
                return false;
            if (actressData.Bust == 0)
                return false;
            if (actressData.Waist == 0)
                return false;
            if (actressData.Hips == 0)
                return false;
            if (String.IsNullOrEmpty(actressData.BloodType))
                return false;
            return true;
        }
    }
}
