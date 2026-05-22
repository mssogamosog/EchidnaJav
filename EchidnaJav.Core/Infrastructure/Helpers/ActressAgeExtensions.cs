using System;
using EchidnaJav.Core.Domain.DTOs;

namespace EchidnaJav.Core.Infrastructure.Helpers
{
    public static class ActressAgeExtensions
    {
        public static string GetCurrentAgeDisplay(this ActressDetailsDto actress)
        {
            int year = actress.DobYear.GetValueOrDefault(0);
            int month = actress.DobMonth.GetValueOrDefault(0);
            int day = actress.DobDay.GetValueOrDefault(0);

            if (year <= 0)
            {
                return "Age Unknown";
            }

            DateTime today = DateTime.Today;

            if (month <= 0 || day <= 0)
            {
                int approximateAge = today.Year - year;
                return approximateAge > 0 ? $"{approximateAge} years old (approx.)" : "Age Unknown";
            }

            
            try
            {
                DateTime birthDate = new DateTime(year, month, day);
                int age = today.Year - birthDate.Year;

                if (birthDate.Date > today.AddYears(-age))
                {
                    age--;
                }

                return age >= 0 ? $"{age} years old" : "Age Unknown";
            }
            catch (ArgumentOutOfRangeException)
            {
                int approximateAge = today.Year - year;
                return approximateAge > 0 ? $"{approximateAge} years old" : "Age Unknown";
            }
        }
    }
}