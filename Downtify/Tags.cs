using SpotifyAPI.Web.Models;
using System.IO;

namespace Downtify
{
    /// <summary>
    /// A collection of MP3 tags and Spotify Properties
    /// </summary>
    public class Tags
    {
        public enum TagsType
        {
            Spotify,
            Youtube
        }

        public TagsType Type { get; set; }
        public string SongName { get; set; }
        public string[] Artist { get; set; }
        public string Album { get; set; }
        public string AlbumID { get; set; }
        public int DiscNumber { get; set; }
        public int Year { get; set; }
        public int TrackNumber { get; set; }
        public int Length { get; set; }
        public string Uri { get; set; }
        public string DownloadLink { get; set; }
        public FullTrack FullTrack { get; set; }
        // This actually holds most of the attributes above, use this if you need some extra attributes

        public Tags()
        {
            Type = TagsType.Spotify;
            SongName = null;
            Artist = new string[] { null };
            Album = null;
            AlbumID = null;
            DiscNumber = 0;
            Year = 0;
            TrackNumber = 0;
            Length = 0;
            Uri = null;
            DownloadLink = "";
            FullTrack = null;
        }

        /// <summary>
        /// Get the title format of an MP3 file
        /// </summary>
        /// <returns>MP3 title format: "Artist - Name"</returns>
        public string GetMP3DisplayName()
        {
            var fileName = "";

            if (Type != TagsType.Spotify)
            {
                fileName = SongName;
            }
            else
            {
                var artist = (this.Artist[0] ?? "Unknown");
                var songName = (this.SongName ?? "Unknown");
                fileName = artist + " - " + songName;
            }
            // Return a valid windows file name
            return string.Join("_", fileName.Split(Path.GetInvalidFileNameChars())) + ".mp3";
        }
    }
}
