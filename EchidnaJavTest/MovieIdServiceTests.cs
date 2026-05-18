using EchidnaJav.Core.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace EchidnaJav.Test
{
    public class MovieIdServiceTests
    {
        private readonly MovieIdService _service = new();

        // ------------------------
        // BASIC
        // ------------------------
        [Theory]
        [InlineData("ABC-123", "ABC-123")]
        [InlineData("ABC_123", "ABC-123")]
        [InlineData("ABC123", "ABC-123")]
        [InlineData("abc-123", "ABC-123")]
        [InlineData("abc_123", "ABC-123")]
        [InlineData("abc123", "ABC-123")]
        [InlineData("abc-12", "ABC-12")]
        [InlineData("abc 123", "ABC-123")]
        [InlineData("mdvr-129A.VR", "MDVR-129")]
        [InlineData("murikuri-001-4k", "MURIKURI-001")]
        
        public void Parse_Basic(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // BRACKETS
        // ------------------------
        [Theory]
        [InlineData("[ABC-123]", "ABC-123")]
        [InlineData("[ABC_123]", "ABC-123")]
        [InlineData("[ABC123]", "ABC-123")]
        [InlineData("[abc-123]", "ABC-123")]
        [InlineData("[abc_123]", "ABC-123")]
        [InlineData("[abc123]", "ABC-123")]
        [InlineData("[abc 123]", "ABC-123")]
        public void Parse_Brackets(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // END WITH D
        // ------------------------
        [Theory]
        [InlineData("abc 123d", "ABC-123D")]
        [InlineData("[abc12-123d]", "ABC12-123D")]

        [InlineData("sivr158D", "sivr-158D")]

        public void Parse_WithSuffixD(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // LONG / EMBEDDED
        // ------------------------
        [Theory]
        [InlineData("ABCDEFG-12345", "ABCDEFG-12345")]
        [InlineData("test test ABCDEFG-12345 test test", "ABCDEFG-12345")]
        [InlineData("test test [ABCDEFG-12345] test test", "ABCDEFG-12345")]
        [InlineData("[abc-123]test", "ABC-123")]
        [InlineData("test[abc-123]test", "ABC-123")]
        [InlineData("448950_3xplanet_DSVR-049_A", "DSVR-049")]
        [InlineData("443876-3xplanet-CRVR-346-A", "CRVR-346")]
        [InlineData("140220B.ABP028HD", "ABP-028")]
        

        public void Parse_LongAndEmbedded(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // MIXED
        // ------------------------
        [Theory]
        [InlineData("abc12-123", "ABC12-123")]
        [InlineData("C-2853", "C-2853")]
        [InlineData("dvaj-277_1080p.mp4", "DVAJ-277")]
        [InlineData("804CMP-001", "CMP-001")]
        public void Parse_Mixed(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // FALSE POSITIVES
        // ------------------------
        [Theory]
        [InlineData("Test at 90% test [ABC-123]", "ABC-123")]
        [InlineData(" askfjkl3234 jksdfjk23 abd234 [ABC-123]", "ABC-123")]
        [InlineData("abc-123 askfjkl3234 jksdfjk23 abd234", "ABC-123")]
        [InlineData("abc123 askfjkl3234 jksdfjk23 abd234", "ABC-123")]
        [InlineData("askfjkl3234 jksdfjk23 abd234 abc-123", "ABC-123")]
        public void Parse_FalsePositives(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // T28
        // ------------------------
        [Theory]
        [InlineData("t28_494", "T28-494")]
        [InlineData("t28-494", "T28-494")]
        [InlineData("[T28-494]", "T28-494")]
        [InlineData("t28 494", "T28-494")]
        [InlineData("t28494", "T28-494")]
        [InlineData("T38-041-4k.mp4", "T38-041")]
        public void Parse_T28(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // FC2 PPV
        // ------------------------
        [Theory]
        [InlineData("FC2-PPV 12", "FC2-PPV-12")]
        [InlineData("FC2-PPV_12345", "FC2-PPV-12345")]
        [InlineData("FC2-PPV1234567", "FC2-PPV-1234567")]
        [InlineData("[Fc2-Ppv-12345678]", "FC2-PPV-12345678")]
        [InlineData("fasdf Fc2-Ppv-12345678 asdf", "FC2-PPV-12345678")]
        [InlineData("430739_3xplanet_FC2_PPV_4533593", "FC2-PPV-4533593")]
        [InlineData("FC2_PPV_4544990", "FC2-PPV-4544990")]
        [InlineData("FC2PPV-4834383", "FC2-PPV-4834383")]
        public void Parse_FC2(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // DMM
        // ------------------------
        [Theory]
        [InlineData("ABC00123", "ABC-123")]
        [InlineData("[ABC00123]", "ABC-123")]
        [InlineData("00ABC00123xx", "ABC-123")]
        [InlineData("00[ABC00123]xx", "ABC-123")]
        [InlineData("  ABC00123  ", "ABC-123")]
        [InlineData("abc00123 askfjkl3234 jksdfjk23 abd234", "ABC-123")]
        public void Parse_DMM(string input, string expected)
        {
            var result = _service.ParseMovieID(input);
            Assert.Equal(expected, result);
        }

        // ------------------------
        // 🚀 NEW CASE (YOUR FIX)
        // ------------------------
        [Fact]
        public void Parse_LeadingNumbers_Format()
        {
            var result = _service.ParseMovieID("13dsvr01744pl");

            Assert.Equal("DSVR-1744", result);
        }
    }
}
