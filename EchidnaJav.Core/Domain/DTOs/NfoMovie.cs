using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace EchidnaJav.Core.Domain.DTOs
{
    using System.Xml.Serialization;

    [XmlRoot("movie")]
    public class NfoMovie
    {
        [XmlElement("title")]
        public string? Title { get; set; }

        [XmlElement("originaltitle")]
        public string? OriginalTitle { get; set; }

        [XmlElement("premiered")]
        public string? Premiered { get; set; } // 🔥 string

        [XmlElement("year")]
        public string? Year { get; set; } // 🔥 string

        [XmlElement("director")]
        public string? Director { get; set; }

        [XmlElement("studio")]
        public string? Studio { get; set; }

        [XmlElement("label")]
        public string? Label { get; set; }

        [XmlElement("plot")]
        public string? Plot { get; set; }

        [XmlElement("runtime")]
        public string? Runtime { get; set; } // 🔥 string

        [XmlElement("genre")]
        public List<string>? Genres { get; set; }

        [XmlElement("actor")]
        public List<NfoActor>? Actors { get; set; }

        [XmlElement("uniqueid")]
        public NfoId? UniqueId { get; set; }

        [XmlElement("fileinfo")]
        public NfoFileInfo? FileInfo { get; set; }

        [XmlElement("dateadded")]
        public string? DateAdded { get; set; } // keep string
    }

    public class NfoActor
    {
        [XmlElement("name")]
        public string? Name { get; set; }

        [XmlElement("order")]
        public int? Order { get; set; } // 🔥 nullable
    }

    public class NfoId
    {
        [XmlText]
        public string? Value { get; set; }

        [XmlAttribute("type")]
        public string? Type { get; set; }
    }

    public class NfoFileInfo
    {
        [XmlElement("streamdetails")]
        public NfoStreamDetails? StreamDetails { get; set; }
    }

    public class NfoStreamDetails
    {
        [XmlElement("video")]
        public NfoVideo? Video { get; set; }
    }

    public class NfoVideo
    {
        [XmlElement("width")]
        public int? Width { get; set; }

        [XmlElement("height")]
        public int? Height { get; set; }
    }   
}
