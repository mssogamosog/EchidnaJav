using System.Text.RegularExpressions;

namespace EchidnaJav.Core.Infrastructure.Services
{
    public interface IMovieIdService
    {
        string GenerateNormalizedID(string originalId);
        bool MovieIDEquals(string movieID1, string movieID2);
        int ParseInitialDigits(string s, int errVal = -1);
        string ParseMovieID(string fileName);
    }

    public class MovieIdService : IMovieIdService
    {
        private class IdRule
        {
            public Regex Pattern { get; }
            public Func<Match, string> Formatter { get; }

            public IdRule(Regex pattern, Func<Match, string> formatter)
            {
                Pattern = pattern;
                Formatter = formatter;
            }
        }

        private static readonly List<IdRule> HighPriorityRules = new List<IdRule>
        {
            // FC2-PPV
            new IdRule(new Regex(@"FC2[-_ ]?PPV[-_ ]?([0-9]{2,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("FC2-PPV-{0}", m.Groups[1].Value)),

            // T28
            new IdRule(
                new Regex(@"\bT(2|3)8[-_ ]?([0-9]{3,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => $"T{m.Groups[1].Value}8-{m.Groups[2].Value}"
            )
        };

        private static readonly List<IdRule> StandardRules = new List<IdRule>
        {
            // 13dsvr01744pl → DSVR-1744
            new IdRule(
                new Regex(@"(?<![A-Za-z0-9])\d{1,4}([A-Z]{2,10})0*([0-9]{3,5})[A-Z]{0,3}(?![A-Za-z0-9])",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => $"{m.Groups[1].Value.ToUpper()}-{int.Parse(m.Groups[2].Value)}"
            ),
            
            // DMM (ABC00123 -> ABC-123)
            new IdRule(new Regex(@"(?<![A-Za-z0-9])([A-Z]{2,10})0{2}([0-9]{2,5})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("{0}-{1}", m.Groups[1].Value.ToUpper(), m.Groups[2].Value)),
        
            // Numeric Prefix (804CMP-001 -> CMP-001)
            new IdRule(new Regex(@"(?<![A-Za-z0-9])[0-9]{1,4}([A-Z]{2,10})[-_ ]([0-9]{2,5})(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("{0}-{1}", m.Groups[1].Value.ToUpper(), m.Groups[2].Value)),
        
            // Mixed Alphanumeric (ABC12-123A -> ABC12-123)
            new IdRule(new Regex(@"(?<![A-Za-z0-9])([A-Z]{2,10}[0-9]{0,2})[-_ ]([0-9]{2,5})[A-Za-z]?(?=[^0-9A-Za-z]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("{0}-{1}", m.Groups[1].Value.ToUpper(), m.Groups[2].Value)),
        
            // Basic / Compact (MDVR-129A -> MDVR-129, MURIKURI-001-4k -> MURIKURI-001)
            new IdRule(new Regex(@"(?<![A-Za-z0-9])([A-Z]{2,12})(?:[-_ ]?)([0-9]{2,8})[A-Za-z]?(?=[^0-9A-Za-z]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("{0}-{1}", m.Groups[1].Value.ToUpper(), m.Groups[2].Value)),
        
            // Single Letter (A-123) (Remains unchanged)
            new IdRule(new Regex(@"(?<![A-Za-z0-9])([A-Z])(?:[-_ ]?)([0-9]{3,5})(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                m => string.Format("{0}-{1}", m.Groups[1].Value.ToUpper(), m.Groups[2].Value))
        };

        // Compile the bracket regex once as well
        private static readonly Regex BracketRegex = new Regex(@"\[(.*?)\]", RegexOptions.Compiled);

        public string ParseMovieID(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

            string input = Path.GetFileNameWithoutExtension(fileName).Trim();

            // Step 1: Check High Priority Rules (FC2, T28)
            foreach (var rule in HighPriorityRules)
            {
                var match = rule.Pattern.Match(input);
                if (match.Success) return rule.Formatter(match);
            }

            // Step 2: Recursive Bracket Check
            var bracketMatch = BracketRegex.Match(input);
            if (bracketMatch.Success)
            {
                var innerResult = ParseMovieID(bracketMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(innerResult)) return innerResult;
            }

            // Step 3: Check Standard Rules
            foreach (var rule in StandardRules)
            {
                var match = rule.Pattern.Match(input);
                if (match.Success) return rule.Formatter(match);
            }

            return string.Empty;
        }

        public bool MovieIDEquals(string movieID1, string movieID2)
        {
            movieID2 = ParseMovieID(movieID2);
            if (movieID1 == movieID2)
                return true;

            string[] parts1 = movieID1.Split('-');
            string[] parts2 = movieID2.Split('-');

            if (parts1.Length != parts2.Length || parts1.Length != 2)
                return false;

            if (parts1[0] != parts2[0])
                return false;

            int num1 = ParseInitialDigits(parts1[1]);
            int num2 = ParseInitialDigits(parts2[1]);

            return (num1 == num2 && num1 != -1);
        }

        public string GenerateNormalizedID(string originalId)
        {
            if (string.IsNullOrWhiteSpace(originalId))
                return string.Empty;

            var parts = originalId.Split('-');
            if (parts.Length == 2)
            {
                string prefix = parts[0];
                string numberPart = parts[1];
                return $"{prefix}-{numberPart.PadLeft(5, '0')}";
            }

            return originalId;
        }

        public int ParseInitialDigits(string s, int errVal = -1)
        {
            int digits = 0;
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                    ++digits;
                else
                    break;
            }
            if (digits > 0)
            {
                string numStr = s.Substring(0, digits);
                if (int.TryParse(numStr, out int num))
                    return num;
            }
            return errVal;
        }
    }
}