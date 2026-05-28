using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace EchidnaJav.Core.Domain.DTOs
{
    [Serializable]
    public class UniqueID
    {
        public UniqueID()
        {
            Type = "JP DVD-ID";
            Value = string.Empty;
            Default = true;
        }

        public UniqueID(string id, string type = "JP DVD-ID", bool isDefault = true)
        {
            Type = type;
            Value = id;
            Default = isDefault;
        }

        [XmlAttribute("type")]
        public string Type { get; set; }

        [XmlAttribute("default")]
        public bool Default { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    [Serializable]
    public class RatingData
    {
        // FIXED: Added public modifiers so XmlSerializer doesn't ignore these properties
        [XmlAttribute("name")]
        public string Name { get; set; } = string.Empty;

        [XmlAttribute("max")]
        public int Max { get; set; } = 10;

        [XmlAttribute("default")]
        public bool Default { get; set; }

        [XmlElement("value")]
        public float Value { get; set; }

        [XmlElement("votes")]
        public int Votes { get; set; }
    }

    [Serializable]
    public class ActorData
    {
        public ActorData() { }

        public ActorData(string name)
        {
            Name = name;
        }

        [XmlElement("name")]
        public string Name { get; set; } = string.Empty;

        [XmlElement("altname")]
        public List<string> Aliases { get; set; } = new List<string>();

        [XmlElement("thumb")]
        public string Thumbnail { get; set; } = string.Empty;

        [XmlElement("role")]
        public string Role { get; set; } = string.Empty;

        [XmlElement("order")]
        public int Order { get; set; }
    }

    [Serializable]
    [XmlRoot("fileinfo")]
    public class FileInfoData
    {
        [XmlElement("streamdetails")]
        public StreamDetailsData StreamDetails { get; set; } = new StreamDetailsData();
    }

    [Serializable]
    public class StreamDetailsData
    {
        [XmlElement("video")]
        public VideoData Video { get; set; } = new VideoData();

        [XmlElement("audio")]
        public List<AudioData> Audio { get; set; } = new List<AudioData>();

        [XmlElement("subtitle")]
        public List<SubtitleData> Subtitles { get; set; } = new List<SubtitleData>();
    }

    [Serializable]
    public class VideoData
    {
        [XmlElement("codec")]
        public string Codec { get; set; } = string.Empty;

        [XmlElement("aspect")]
        public string Aspect { get; set; } = string.Empty;

        [XmlElement("width")]
        public int Width { get; set; }

        [XmlElement("height")]
        public int Height { get; set; }

        [XmlElement("durationinseconds")]
        public int DurationInSeconds { get; set; }

        [XmlElement("stereomode")]
        public string StereoMode { get; set; } = string.Empty;

        // NEW: Required for modern video file definitions (SDR, HDR10, DolbyVision)
        [XmlElement("hdrtype")]
        public string HdrType { get; set; } = string.Empty;
    }

    [Serializable]
    public class AudioData
    {
        [XmlElement("codec")]
        public string Codec { get; set; } = string.Empty;

        [XmlElement("language")]
        public string Language { get; set; } = string.Empty;

        [XmlElement("channels")]
        public string Channels { get; set; } = string.Empty;
    }

    [Serializable]
    public class SubtitleData
    {
        [XmlElement("language")]
        public string Language { get; set; } = string.Empty;
    }

    [Serializable]
    [XmlRoot("movie")]
    public class MovieMetadata : IEquatable<MovieMetadata>
    {
        public MovieMetadata()
        {
            UniqueID = new UniqueID();
        }

        public MovieMetadata(string uniqueID) : this()
        {
            UniqueID = new UniqueID(uniqueID);
        }

        #region Standard Properties

        [XmlElement("id")]
        public string ID { get; set; } = string.Empty;

        [XmlElement("uniqueid")]
        public UniqueID UniqueID { get; set; }

        [XmlElement("title")]
        public string Title { get; set; } = string.Empty;

        [XmlElement("originaltitle")]
        public string OriginalTitle { get; set; } = string.Empty;

        // Primary date format for modern media players (YYYY-MM-DD)
        [XmlElement("premiered")]
        public string Premiered { get; set; } = string.Empty;

        // Note: Kept for legacy compatibility, but newer systems prefer relying purely on Premiered
        [XmlElement("year")]
        public int Year { get; set; }

        [XmlElement("director")]
        public string Director { get; set; } = string.Empty;

        [XmlElement("studio")]
        public string Studio { get; set; } = string.Empty;

        [XmlElement("label")]
        public string Label { get; set; } = string.Empty;

        [XmlElement("series")]
        public string Series { get; set; } = string.Empty;

        [XmlElement("plot")]
        public string Plot { get; set; } = string.Empty;

        [XmlElement("runtime")]
        public int Runtime { get; set; }

        [XmlElement("mpaa")]
        public string MPAA { get; set; } = string.Empty;

        [XmlElement("tagline")]
        public string Tagline { get; set; } = string.Empty;

        [XmlElement("set")]
        public string Set { get; set; } = string.Empty;

        [XmlElement("thumb")]
        public string Thumb { get; set; } = string.Empty;

        #endregion

        #region Collections & Complex Objects

        // Modernized Wrapper for Multiple Scraper Ratings (e.g., TMDB + Trakt)
        [XmlArray("ratings")]
        [XmlArrayItem("rating")]
        public List<RatingData> Ratings { get; set; } = new List<RatingData>();

        // Legacy Flat Rating preserved for Javinizer support
        [XmlElement("rating")]
        public float LegacyRating { get; set; }

        [XmlElement("userrating")]
        public int UserRating { get; set; }

        [XmlElement("votes")]
        public string Votes { get; set; } = string.Empty;

        [XmlElement("genre")]
        public List<string> Genres { get; set; } = new List<string>();

        [XmlElement("actor")]
        public List<ActorData> Actors { get; set; } = new List<ActorData>();

        [XmlElement("fileinfo")]
        public FileInfoData FileInfo { get; set; } = new FileInfoData();

        [XmlElement("status")]
        public string Status { get; set; } = string.Empty;

        [XmlElement("dateadded")]
        public string DateAdded { get; set; } = string.Empty;

        [XmlAnyElement]
        public XmlElement[] Unknown { get; set; }

        #endregion

        #region Safe Equality Implementations

        public override int GetHashCode()
        {
            // FIXED: Replaced unsafe string bitwise XOR with modern HashCode combiner
            return HashCode.Combine(UniqueID?.Type, UniqueID?.Value);
        }

        public override bool Equals(object obj) => Equals(obj as MovieMetadata);

        public bool Equals(MovieMetadata other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return string.Equals(UniqueID?.Type, other.UniqueID?.Type, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(UniqueID?.Value, other.UniqueID?.Value, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }

    [Serializable]
    public class MovieData : IEquatable<MovieData>
    {
        public MovieData()
        {
            Metadata = new MovieMetadata();
        }

        public MovieData(string uniqueID) : this()
        {
            Metadata = new MovieMetadata(uniqueID);
        }

        public MovieData(MovieData movieData) : this()
        {
            if (movieData == null) return;

            Metadata = movieData.Metadata; // Note: Shallow copy. Implement deep cloning if needed.
            Path = movieData.Path;
            SharedPath = movieData.SharedPath;
            Folder = movieData.Folder;
            MovieResolution = movieData.MovieResolution;
            CoverFileName = movieData.CoverFileName;
            MetadataFileName = movieData.MetadataFileName;
            MetadataChanged = movieData.MetadataChanged;

            MovieFileNames.AddRange(movieData.MovieFileNames);
            ExtraMovieFileNames.AddRange(movieData.ExtraMovieFileNames);
            ThumbnailsFileNames.AddRange(movieData.ThumbnailsFileNames);
            SubtitleFileNames.AddRange(movieData.SubtitleFileNames);
        }

        #region Properties

        public string Path { get; set; } = string.Empty;
        public bool SharedPath { get; set; }
        public string Folder { get; set; } = string.Empty;
        public MovieMetadata Metadata { get; set; }
        public string MovieResolution { get; set; } = string.Empty;
        public string CoverFileName { get; set; } = string.Empty;
        public string MetadataFileName { get; set; } = string.Empty;
        public bool MetadataChanged { get; set; }

        public List<string> MovieFileNames { get; set; } = new List<string>();
        public List<string> ExtraMovieFileNames { get; set; } = new List<string>();
        public List<string> ThumbnailsFileNames { get; set; } = new List<string>();
        public List<string> SubtitleFileNames { get; set; } = new List<string>();

        #endregion

        #region Equality

        public override int GetHashCode() => Metadata?.GetHashCode() ?? 0;

        public override bool Equals(object obj) => Equals(obj as MovieData);

        public bool Equals(MovieData other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return EqualityComparer<MovieMetadata>.Default.Equals(Metadata, other.Metadata);
        }

        #endregion

        public static void Filter(XElement element) { /* Implementation logic */ }

        public static void Filter(XDocument doc)
        {
            if (doc?.Root == null) return;
            foreach (var element in doc.Root.Elements())
            {
                Filter(element);
            }
        }
    }
}
