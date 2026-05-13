using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EchidnaJav.Scraper.Helpers
{
    public static class ActorMatchingEngine
    {
        public static bool AreActorsEquivalent(ActorData? a, ActorData? b)
        {
            if (a == null || b == null) return false;

            if (string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
                return true;

            if (ContainsName(b.Name, a.Aliases)) return true;
            if (ContainsName(a.Name, b.Aliases)) return true;

            foreach (var name in a.Aliases ?? Enumerable.Empty<string>())
            {
                if (ContainsName(name, b.Aliases)) return true;
            }

            return AreActorsNearlyEquivalent(a, b);
        }

        public static void MergeActors(ActorData target, ActorData source)
        {
            if (target == null || source == null) return;

            if (IsBetterName(source.Name, target.Name))
            {
                AddAliasIfMissing(target, target.Name);
                target.Name = source.Name;
            }
            else
            {
                AddAliasIfMissing(target, source.Name);
            }

            foreach (var alias in source.Aliases ?? Enumerable.Empty<string>())
            {
                AddAliasIfMissing(target, alias);
            }
        }

        private static bool AreActorsNearlyEquivalent(ActorData a, ActorData b)
        {
            if (AreNamesStructurallyEquivalent(a.Name, b.Name))
                return true;

            const float SimilarityThreshold = 0.75f;

            if (GetSimilarity(a.Name, b.Name) > SimilarityThreshold) return true;
            if (HasSimilarMatch(b.Name, a.Aliases, SimilarityThreshold)) return true;
            if (HasSimilarMatch(a.Name, b.Aliases, SimilarityThreshold)) return true;

            foreach (var name in a.Aliases ?? Enumerable.Empty<string>())
            {
                if (HasSimilarMatch(name, b.Aliases, SimilarityThreshold)) return true;
            }

            return false;
        }

        private static void AddAliasIfMissing(ActorData actor, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            actor.Aliases ??= new List<string>();

            if (!actor.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
            {
                actor.Aliases.Add(name);
            }
        }

        private static bool IsBetterName(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (string.IsNullOrWhiteSpace(current)) return true;

            bool candidateHasSpace = candidate.Contains(' ');
            bool currentHasSpace = current.Contains(' ');

            if (candidateHasSpace && !currentHasSpace) return true;
            if (!candidateHasSpace && currentHasSpace) return false;

            return candidate.Length > current.Length;
        }

        private static bool AreNamesStructurallyEquivalent(string nameA, string nameB)
        {
            if (string.IsNullOrWhiteSpace(nameA) || string.IsNullOrWhiteSpace(nameB)) return false;

            var tokensA = Tokenize(nameA);
            var tokensB = Tokenize(nameB);
            string normA = Normalize(nameA);
            string normB = Normalize(nameB);

            if (tokensA.Count > 1 && tokensB.Count > 1 && new HashSet<string>(tokensA).SetEquals(tokensB))
                return true;

            if (tokensA.Count > 1 && tokensA.All(normB.Contains)) return true;
            if (tokensB.Count > 1 && tokensB.All(normA.Contains)) return true;

            return false;
        }

        private static float GetSimilarity(string left, string right)
        {
            if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right)) return 1.0f;
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return 0.0f;
            if (left == right) return 1.0f;

            int leftSize = left.Length;
            int rightSize = right.Length;
            int leftIdx = 0, rightIdx = 0;
            float matchVal = 0.0f;
            int maxSize = Math.Max(leftSize, rightSize);

            while (leftIdx < leftSize && rightIdx < rightSize)
            {
                if (left[leftIdx] == right[rightIdx])
                {
                    matchVal += 1.0f / maxSize;
                    ++leftIdx; ++rightIdx;
                }
                else if (char.ToLowerInvariant(left[leftIdx]) == char.ToLowerInvariant(right[rightIdx]))
                {
                    matchVal += 0.9f / maxSize;
                    ++leftIdx; ++rightIdx;
                }
                else
                {
                    int lidxbest = leftSize, ridxbest = rightSize;
                    int bestCount = int.MaxValue;
                    int leftCount = 0;

                    for (int lidx = leftIdx; lidx != leftSize; ++lidx)
                    {
                        int rightCount = 0;
                        for (int ridx = rightIdx; ridx != rightSize; ++ridx)
                        {
                            if (char.ToLowerInvariant(left[lidx]) == char.ToLowerInvariant(right[ridx]))
                            {
                                int totalCount = leftCount + rightCount;
                                if (totalCount < bestCount)
                                {
                                    bestCount = totalCount;
                                    lidxbest = lidx;
                                    ridxbest = ridx;
                                }
                            }
                            ++rightCount;
                        }
                        ++leftCount;
                    }
                    leftIdx = lidxbest;
                    rightIdx = ridxbest;
                }
            }
            return Math.Max(Math.Min(matchVal, 1.0f), 0.0f);
        }

        private static bool HasSimilarMatch(string s, List<string>? strings, float threshold) =>
            strings != null && strings.Any(str => GetSimilarity(s, str) > threshold);

        private static bool ContainsName(string str, List<string>? strings) =>
            strings != null && strings.Any(s => string.Equals(str, s, StringComparison.OrdinalIgnoreCase));

        private static List<string> Tokenize(string name) =>
            name.ToLowerInvariant().Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static string Normalize(string name) =>
            name.ToLowerInvariant().Replace(" ", "").Replace("　", "");
    }
}