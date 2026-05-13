using EchidnaJav.Core.Domain.DTOs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EchidnaJav.Scraper.Helpers
{
    public static class ScraperParsingExtensions
    {
        public static void StringToDateTime(string date, out int year, out int month, out int day)
        {
            year = 1;
            month = 1;
            day = 1;
            try
            {
                string[] dateParts = date.Split('-');
                int.TryParse(dateParts[0], out year);
                if (dateParts.Length > 1)
                    int.TryParse(dateParts[1], out month);
                if (dateParts.Length > 2)
                    int.TryParse(dateParts[2], out day);
                // Validate that this is a legit date
                new DateTime(year, month, day);
            }
            catch
            {
                year = 0;
                month = 0;
                day = 0;
            }
        }
        public static bool Equals(string str, List<string> strings, StringComparison comparison)
        {
            foreach (string s in strings)
            {
                if (String.Equals(str, s, comparison))
                    return true;
            }
            return false;
        }

        // Notice the "this string date" - this turns it into an extension method
        public static void ToActressDob(this string date, out int year, out int month, out int day)
        {
            year = 0; month = 0; day = 0;
            if (string.IsNullOrWhiteSpace(date)) return;

            try
            {
                string[] dateParts = date.Split('-');
                if (dateParts.Length > 0) int.TryParse(dateParts[0], out year);
                if (dateParts.Length > 1) int.TryParse(dateParts[1], out month);
                if (dateParts.Length > 2) int.TryParse(dateParts[2], out day);

                // Validate that this is a legit date
                if (year > 0 && month > 0 && day > 0)
                    _ = new DateTime(year, month, day);
            }
            catch
            {
                year = 0; month = 0; day = 0;
            }
        }
        public static bool IsActressWorthShowing(ActressData actress)
        {
            if (actress == null)
                return false;
            if (actress.ImageFileNames.Count == 0)
                return false;
            if (String.IsNullOrEmpty(actress.JapaneseName) == false)
                return true;
            if (actress.DobYear != 0 && actress.DobMonth != 0 && actress.DobDay != 0)
                return true;
            if (actress.Height != 0)
                return true;
            if (String.IsNullOrEmpty(actress.Cup) == false)
                return true;
            if (actress.Bust != 0 && actress.Waist != 0 && actress.Hips != 0)
                return true;
            if (String.IsNullOrEmpty(actress.BloodType) == false)
                return true;
            return false;
        }

        public static int ParseInitialDigits(this string s, int errVal = -1)
        {
            if (string.IsNullOrWhiteSpace(s)) return errVal;

            int digits = 0;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) ++digits;
                else break;
            }

            if (digits > 0 && int.TryParse(s.Substring(0, digits), out int num))
            {
                return num;
            }
            return errVal;
        }
        public static void FilterActorName(ActorData actor)
        {
            // Some actors are listed as "First Last (AltFirst AltLast).
            // This function will split these out into main and alt names

            if (actor == null || String.IsNullOrEmpty(actor.Name))
                return;

            // Try cplitting name on parens
            string[] actorNames = actor.Name.Split("()".ToCharArray());
            if (actorNames.Length == 1)
            {
                // If those don't exist, just trim and return the first string
                actor.Name = actorNames[0].Trim();
            }
            else
            {
                // Assign the trimmed first part
                actor.Name = actorNames[0].Trim();

                // If we have one or more names in parens, next try splitting on commas
                string[] moreActorNames = actorNames[1].Split(',');
                foreach (string name in moreActorNames)
                {
                    // Add each name to the alias list if it doesn't exist
                    string trimmedName = name.Trim();
                    bool foundAlias = false;
                    foreach (string alias in actor.Aliases)
                    {
                        if (alias == trimmedName)
                        {
                            foundAlias = true;
                            break;
                        }
                    }
                    if (foundAlias == false)
                        actor.Aliases.Add(trimmedName);
                }
            }

            // Make all title case
            TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;
            actor.Name = textInfo.ToTitleCase(actor.Name);
            for (int i = 0; i < actor.Aliases.Count; ++i)
                actor.Aliases[i] = textInfo.ToTitleCase(actor.Aliases[i]);

            // Remove duplicates
            var nameSet = new HashSet<string>();
            foreach (var alias in actor.Aliases)
            {
                if (alias != actor.Name)
                    nameSet.Add(alias.Trim());
            }
            actor.Aliases.Clear();
            foreach (var alias in nameSet)
                actor.Aliases.Add(alias);
        }
    }


}
