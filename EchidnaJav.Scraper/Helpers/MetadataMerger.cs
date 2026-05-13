using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchidnaJav.Scraper.Helpers
{
    public static class MetadataMerger
    {
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