using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Scraper.Helpers
{
    public static class MetadataMerger
    {
        public static MovieMetadata MergePrimary(MovieMetadata? fallbackSource, MovieMetadata? primarySource)
        {
            if (fallbackSource == null) return primarySource ?? new MovieMetadata();
            if (primarySource == null) return fallbackSource;

            var combined = new MovieMetadata
            {
                UniqueID = primarySource.UniqueID,
                // Prefer primarySource (JavDatabase) for titles
                Title = Coalesce(primarySource.Title, fallbackSource.Title),
                // For all other info, prefer fallbackSource (JavLibrary)
                OriginalTitle = Coalesce(fallbackSource.OriginalTitle, primarySource.OriginalTitle),
                Premiered = Coalesce(fallbackSource.Premiered, primarySource.Premiered),
                Year = fallbackSource.Year == 0 ? primarySource.Year : fallbackSource.Year,
                Studio = Coalesce(fallbackSource.Studio, primarySource.Studio),
                Label = Coalesce(fallbackSource.Label, primarySource.Label),
                Runtime = fallbackSource.Runtime == 0 ? primarySource.Runtime : fallbackSource.Runtime,
                Director = Coalesce(fallbackSource.Director, primarySource.Director),
                Series = Coalesce(fallbackSource.Series, primarySource.Series),

                Genres = fallbackSource.Genres
                    .Union(primarySource.Genres, StringComparer.OrdinalIgnoreCase)
                    .ToList(),

                // Scrape actress data from primarySource first, merge alternate names/actors later
                Actors = MergeActorLists(primarySource.Actors?.ToList(), fallbackSource.Actors)
            };

            return combined;
        }
        public static MovieMetadata MergeSecondary(MovieMetadata primary, MovieMetadata secondary)
        {
            if (primary == null) return secondary;
            if (secondary == null) return primary;

            primary.Title = Coalesce(primary.Title, secondary.Title);
            primary.OriginalTitle = Coalesce(primary.OriginalTitle, secondary.OriginalTitle);
            primary.Premiered = Coalesce(primary.Premiered, secondary.Premiered);
            primary.Year = primary.Year == 0 ? secondary.Year : primary.Year;
            primary.Studio = Coalesce(primary.Studio, secondary.Studio);
            primary.Label = Coalesce(primary.Label, secondary.Label);
            primary.Runtime = primary.Runtime == 0 ? secondary.Runtime : primary.Runtime;
            primary.Director = Coalesce(primary.Director, secondary.Director);
            primary.Series = Coalesce(primary.Series, secondary.Series);

            primary.Genres = primary.Genres
                .Union(secondary.Genres, StringComparer.OrdinalIgnoreCase)
                .ToList();

            primary.Actors = MergeActorLists(primary.Actors, secondary.Actors);

            return primary;
        }

        public static List<ActorData> MergeActorLists(List<ActorData>? primary, List<ActorData>? secondary)
        {
            primary ??= new List<ActorData>();
            if (secondary == null || secondary.Count == 0) return primary;

            foreach (var actorB in secondary)
            {
                var existingActor = primary.FirstOrDefault(a => ActorMatchingEngine.AreActorsEquivalent(a, actorB));
                if (existingActor != null)
                {
                    ActorMatchingEngine.MergeActors(existingActor, actorB);
                }
                else
                {
                    primary.Add(actorB);
                }
            }

            return primary;
        }

        private static string Coalesce(string primary, string secondary) =>
            string.IsNullOrEmpty(primary) ? secondary : primary;
    }
}