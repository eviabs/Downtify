using NAudio.Lame;
using Newtonsoft.Json.Linq;
using SpotifyAPI.Local;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TagLib;
using File = TagLib.File;

namespace Downtify
{
    /// <summary>
    /// Record tracks from spotify in MP3 format and download its tags
    /// </summary>
    public class Downtify
    {
        // ##################################################################
        // ############################ CONSTANTS ###########################
        // ##################################################################
        private const string AuthClientID = "7e974cde5f5f4c62bd62751da0c9f6bf";
        private const string AuthClientSecret = "68f4d9eae11a4bc3b55229a1f6f94975";
        private const string SpotifyProcessName = "spotify";
        private const string TempImageName = @"\\cover.jpeg";
        private const string TempWavName = @"\\record.wav";


        // ##################################################################
        // ########################## Data members ##########################
        // ##################################################################
        /// <summary>
        /// Controls Spotify process. 
        /// Note: Can be used freely outside Downtify, hence public
        /// </summary>
        public SpotifyLocalAPI LocalSpotifyAPI;

        /// <summary>
        /// Get date from Spotify's DB 
        /// </summary>
        private readonly SpotifyWebAPI _webSpotifyAPI;

        /// <summary>
        /// Spotify's credentials
        /// </summary>
        private ClientCredentialsAuth _auth;

        /// <summary>
        /// Control audio-out sessions
        /// </summary>
        private readonly Sound _soundControl;

        /// <summary>
        /// Record audio from audio-out 
        /// </summary>
        private readonly Recorder _recorder;

        /// <summary>
        /// MP3 Preset 
        /// </summary>
        private LAMEPreset _mp3Preset;

        /// <summary>
        /// Temp folder for recorded WAV audio and downloaded cover image
        /// </summary>
        private string _tempFolder;

        /// <summary>
        /// The folder that keeps the MP3 files
        /// </summary>
        private string _mp3FilesFolder;

        /// <summary>
        /// Indicates whether Downtify is recording or not. 
        /// This affects the handlers behavior (e.g. DownloadSingleSongFinalize).
        /// </summary>
        private bool _isRecording;

        /// <summary>
        /// Holds the tags of the song that is being recorded.
        /// Will be set by the method that starts the recording action, and
        /// will be used by the relevant handler.
        /// </summary>
        private Tags _tagsOfCurrentSongToDownload;

        private int _indexOfCurrentSongToDownload;

        private List<Tags> _songsToDownload;

        private readonly WebClient _webClient = new WebClient();

        private bool _isYouTubeDownloaded = false;

        private bool _isAdvertisement = false;

        // ##################################################################
        // ############################# Methods ############################
        // ##################################################################
        /// <summary>
        /// Default constructor  
        /// </summary>
        public Downtify(string tempFolder, string mp3FilesFolder, LAMEPreset mp3Preset)
        {
            // load Spotify
            LocalSpotifyAPI = new SpotifyLocalAPI();
            _webSpotifyAPI = new SpotifyWebAPI();
            DoAuth();

            // load other controllers
            _soundControl = new Sound(SpotifyProcessName);
            _mp3Preset = mp3Preset;
            _mp3FilesFolder = mp3FilesFolder;
            _tempFolder = tempFolder;
            _recorder = new Recorder(_tempFolder + TempWavName, _mp3Preset);

            // register events
            _isRecording = false;
            LocalSpotifyAPI.OnPlayStateChange += _finishDownloadSongOnPlayStateChange;
            LocalSpotifyAPI.OnTrackChange += _finishDownloadSongOnTrackChange;
        }

        /// <summary>
        /// Set the temp folder used by Downtify
        /// </summary>
        /// <param name="tempFolder">temp folder path</param>
        public void SetTempFolder(string tempFolder)
        {
            _tempFolder = tempFolder;
            _recorder.SetTempWavFilePath(_tempFolder + TempWavName);
        }

        /// <summary>
        /// Get the temp folder used by Downtify
        /// </summary>
        /// <returns>temp folder used by Downtify</returns>
        public string GetTempFolder()
        {
            return _tempFolder;
        }

        /// <summary>
        /// Set the folder that keeps the MP3 files
        /// </summary>
        /// <param name="mp3FilesFolder">MP3 files folder path</param>
        public void SetMP3FilesFolder(string mp3FilesFolder)
        {
            _mp3FilesFolder = mp3FilesFolder;
        }

        /// <summary>
        /// Get the folder that keeps the MP3 files
        /// </summary>
        /// <returns>MP3 files folder path</returns>
        public string GetMP3FilesFolder()
        {
            return _mp3FilesFolder;
        }

        /// <summary>
        /// Set the MP3 preset used by the recorder
        /// </summary>
        /// <param name="mp3Preset">MP3 preset</param>
        public void SetMP3Preset(LAMEPreset mp3Preset)
        {
            _mp3Preset = mp3Preset;
            _recorder.SetMP3Preset(mp3Preset);
        }

        /// <summary>
        /// Get the MP3 preset used by the recorder
        /// </summary>
        /// <returns>MP3 preset</returns>
        public LAMEPreset GetMP3Preset()
        {
            return _mp3Preset;
        }

        /// <summary>
        /// Authenticate Spotify API calls using the generated client id and secret key.
        /// Get them at https://developer.spotify.com/my-applications/#!/applications
        /// </summary>
        private void DoAuth()
        {
            _auth = new ClientCredentialsAuth
            {
                ClientId = AuthClientID,
                ClientSecret = AuthClientSecret,
                Scope =
                    Scope.UserReadPrivate | Scope.UserReadEmail |
                    Scope.PlaylistReadPrivate |
                    Scope.UserReadPrivate | Scope.UserFollowRead |
                    Scope.UserReadBirthdate
            };

            var authKey = _auth.DoAuth();
            _webSpotifyAPI.AccessToken = authKey.AccessToken;
            _webSpotifyAPI.TokenType = authKey.TokenType;
        }

        /// <summary>
        /// Extracts user_id and playlist_id from a playlist URI/URL
        /// </summary>
        /// <param name="id">Spotify playlist URI or URL address</param>
        /// <returns>An array with 2 strings: user_id at position 0 and playlist_id at position 1. Filled with nulls in case of failure.</returns>
        public string[] GetPlaylistDetails(string id)
        {
            const char playlistUrlDelimiter = '/';
            const char playlistUriDelimiter = ':';

            string[] ret = { null, null };

            var tmp = id.Split(playlistUrlDelimiter);
            if (tmp.Length == 7)
            {
                ret[0] = tmp[4];
                ret[1] = tmp[6];
            }

            tmp = id.Split(playlistUriDelimiter);
            if (tmp.Length == 5)
            {
                ret[0] = tmp[2];
                ret[1] = tmp[4];
            }

            return ret;
        }

        /// <summary>
        /// Extracts songs from a playlist. This is the core function (hence private).
        /// </summary>
        /// <param name="userid">The user id</param>
        /// <param name="playlistId">The playlist id</param>
        /// <returns>A list of Tags objects corresponding to the playlist songs</returns>
        private List<Tags> _GetSongsFromPlaylist(string userid, string playlistId)
        {
            var list = new List<Tags>();

            if (userid != null && playlistId != null)
            {
                var songs = _webSpotifyAPI.GetPlaylistTracks(userid, playlistId).Items.ToList();


                foreach (var playlistTrack in songs)
                {
                    var tags = new Tags();
                    var track = playlistTrack.Track;

                    tags.FullTrack = track;
                    tags.SongName = track.Name;
                    tags.Artist = track.Artists.Select(name => name.Name.ToString()).Take(1).ToArray();
                    tags.Album = track.Album.Name;
                    tags.AlbumID = track.Album.Id;
                    tags.DiscNumber = track.DiscNumber;
                    tags.Year = int.Parse(_webSpotifyAPI.GetAlbum(track.Album.Id).ReleaseDate.Substring(0, 4));
                    tags.TrackNumber = track.TrackNumber;
                    tags.Length = track.DurationMs;
                    tags.Uri = track.Uri;

                    list.Add(tags);
                }
            }
            return list;
        }

        /// <summary>
        /// Extracts songs from a playlist.
        /// </summary>
        /// <param name="userid">The user id</param>
        /// <param name="playlistId">The playlist id</param>
        /// <returns>A task that holds a list of Tags objects corresponding to the playlist songs</returns>
        public Task<List<Tags>> GetSongsFromPlaylist(string userid, string playlistId)
        {
            return Task.Run(() => _GetSongsFromPlaylist(userid, playlistId));
        }

        /// <summary>
        /// Extracts songs from a playlist.
        /// </summary>
        /// <param name="path">The playlist URL or URI</param>
        /// <returns>A task that holds a list of Tags objects corresponding to the playlist songs</returns>
        public Task<List<Tags>> GetSongsFromPlaylist(string path)
        {
            return GetSongsFromPlaylist(GetPlaylistDetails(path));
        }

        /// <summary>
        /// Extracts songs from a playlist
        /// </summary>
        /// <param name="arrDetails">An array that holds the user_is and playlist_id</param>
        /// <returns>A task that holds a list of Tags objects corresponding to the playlist songs</returns>
        public Task<List<Tags>> GetSongsFromPlaylist(string[] arrDetails)
        {
            return GetSongsFromPlaylist(arrDetails[0], arrDetails[1]);
        }

        /// <summary>
        /// Connect to Spotify app.
        /// </summary>
        public bool Connect()
        {
            return LocalSpotifyAPI.Connect();
        }

        /// <summary>
        /// Writes the tags to a file
        /// </summary>
        /// <param name="tags">The tags file to read from</param>
        /// <param name="filePath">The file to weite to</param>
        public void WriteTagsToFile(Tags tags, string filePath)
        {

            var file = File.Create(filePath);
            file.Tag.Title = tags.SongName;
            file.Tag.Performers = tags.Artist;
            file.Tag.Disc = (uint)tags.DiscNumber;
            file.Tag.Year = (uint)tags.Year;
            file.Tag.Track = (uint)tags.TrackNumber;
            file.Tag.Album = tags.Album;
            if (tags.FullTrack.Album.Images.Count > 0)
            {
                try
                {
                    file.Tag.Pictures = _getCoverFromURL(tags.FullTrack.Album.Images[0].Url);
                }
                catch (Exception)
                {
                    // ignored //TODO add unknown cover image
                }
            }
            file.Tag.Comment = tags.Uri;
            file.Save();


        }

        /// <summary>
        /// Downloads a picture from a URL and returns  it as Picture object
        /// </summary>
        /// <param name="url">The image location</param>
        /// <returns>Picture object with the image</returns>
        private IPicture[] _getCoverFromURL(string url)
        {
            var tempImagePath = _tempFolder + TempImageName;
            // download image to temp location
            var webClient = new WebClient();
            webClient.DownloadFile(new Uri(url), tempImagePath);

            //create picture from the downloaded image
            var pic = new Picture(tempImagePath)
            {
                Type = PictureType.FrontCover,
                Description = "Cover"
            };
            return new IPicture[] { pic };
        }

        /// <summary>
        /// Downloads a single song.
        /// </summary>
        /// <param name="tag">Tag object to write</param>
        public void StartDownloadSong(Tags tag)
        {
            _tagsOfCurrentSongToDownload = tag;

            if (tag.Type == Tags.TagsType.Spotify)
            {
                _soundControl.MuteAllSessionsExceptRecordingSource();
                _recorder.StartRecording();
                LocalSpotifyAPI.PlayURL(tag.FullTrack.Uri);
                _isRecording = true;
                _statusChanged(SongDownloadStatus.Recording);
            }
            else if (tag?.Uri != null)
            {
                _statusChanged(SongDownloadStatus.Downloading);
                _downloadFromYouTube();
                _isYouTubeDownloaded = true;
                _finishDownloadSong();
            }
        }

        private void _downloadFromYouTube()
        {
            //http://www.youtubeinmp3.com/api/
            //TODO move from here to the tags creator
            var url = "http://www.youtubeinmp3.com/fetch/?format=JSON&video=" + _tagsOfCurrentSongToDownload.Uri;
            var json = _webClient.DownloadString(url);
            var jsonObj = JObject.Parse(json);
            _tagsOfCurrentSongToDownload.SongName = jsonObj["title"].ToString(); ;
            _tagsOfCurrentSongToDownload.DownloadLink = jsonObj["link"].ToString();
            //TODO until here
            _webClient.DownloadFile(_tagsOfCurrentSongToDownload.DownloadLink, _mp3FilesFolder + _tagsOfCurrentSongToDownload.GetMP3DisplayName());
        }

        /// <summary>
        /// Organize song files (MP3 only) in "path" folder into folders by artist.
        /// Only "path" will be organized and no sub-folders will be traversed.
        /// </summary>
        /// <param name="path"></param>
        public static void OrganizeSongsInFolders(string path)
        {
            // get all mp3 files in the root dir of path
            var songs = Directory.GetFiles(path, "*.mp3", SearchOption.TopDirectoryOnly);
            foreach (var song in songs)
            {
                // get a valid windows folder name and the filename
                var artist = string.Join("", File.Create(song).Tag.FirstPerformer.Split(Path.GetInvalidFileNameChars()));
                var filename = Path.GetFileName(song);

                // get the final artist folder path
                var artistFolder = Path.Combine(path, artist);
                var newFilename = Path.Combine(artistFolder, filename);

                // create artists folder if it does not exist
                Directory.CreateDirectory(artistFolder);
                System.IO.File.Move(song, newFilename);
            }

        }

        /// <summary>
        /// This method is called on each "playing status change" event.
        /// When a playing is paused, after calling StartDownloadSong method
        /// we know that a song has just been recorded and we need to finish
        /// working on that song.
        /// </summary>
        /// <param name="sender">Standard sender</param>
        /// <param name="e">PlayStateEventArgs that indicates if Spotify is playing or not</param>
        private void _finishDownloadSongOnPlayStateChange(object sender, PlayStateEventArgs e)
        {
            // Check that the method was invoked after recording (and not after any other operation)
            if (e.Playing || !_isRecording || _recorder == null ||
                (_recorder.GetState() != Recorder.RecorderState.Recording &&
                 _recorder.GetState() != Recorder.RecorderState.RecordingStopped))
                return;

            // Finish download song
            _finishDownloadSong();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _finishDownloadSongOnTrackChange(object sender, TrackChangeEventArgs e)
        {
            //TODO use TrackChangeEventArgs to check for advertisement

            var oldSong = e.OldTrack.TrackResource?.Name;
            var newSong = e.NewTrack.TrackResource?.Name;

            //MessageBox.Show("old is: " + oldSong + "\nnew is: " + newSong + "\n_isAdvertisement is " + _isAdvertisement.ToString());

            // Check that the method was invoked after recording (and not after any other operation)
            if (!_isRecording || _recorder == null ||
                (_recorder.GetState() != Recorder.RecorderState.Recording &&
                 _recorder.GetState() != Recorder.RecorderState.RecordingStopped))
                return;

            // If _isAdvertisement, restart downloading
            if (_isAdvertisement)
            {
                //MessageBox.Show("If _isAdvertisement");
                _isAdvertisement = false;
                _discardAdvertisementRecording();
                StartDownloadSong(_tagsOfCurrentSongToDownload);

            }

            // If ad is being played, discard recording and set _isAdvertisement
            if (newSong != null && oldSong == null)
            {
                //MessageBox.Show("If ad is being played");
                _discardAdvertisementRecording();
                StartDownloadSong(_tagsOfCurrentSongToDownload);
                //_isAdvertisement = true;
                return;
            }

            // If another ad is played
            if (newSong == null && oldSong == null)
            {
                //MessageBox.Show("If another ad is played");
                _discardAdvertisementRecording();
                _isAdvertisement = true;
                return;
            }

            // Finish download song if it wasn't an ad. (oldSong != null if we get here!)
            if (newSong == null)
            {
                _finishDownloadSong();
            }
        }

        private void _discardAdvertisementRecording()
        {
            _isRecording = false;
            _recorder.StopRecording();
            _soundControl.UnMuteAllSessions();
            _statusChanged(SongDownloadStatus.Completed);
        }

        /// <summary>
        /// 
        /// </summary>
        private void _finishDownloadSong()
        {
            if (_tagsOfCurrentSongToDownload.Type == Tags.TagsType.Spotify)
            {
                // finish recording
                _isRecording = false;
                _recorder.StopRecording();
                _soundControl.UnMuteAllSessions();

                // set proper MP3 path
                var mp3Path = $"{_mp3FilesFolder}\\{_tagsOfCurrentSongToDownload.GetMP3DisplayName()}";
                _recorder.SetMP3File(mp3Path);

                // convert to MP3
                _statusChanged(SongDownloadStatus.ConvertingToMP3);
                _recorder.ConvertToMP3();

                // edit tags
                if (_recorder.GetState() == Recorder.RecorderState.ConvertingFinsihed)
                {
                    _statusChanged(SongDownloadStatus.WritingTags);
                    WriteTagsToFile(_tagsOfCurrentSongToDownload, mp3Path);
                }

                // finish downloading process
                _statusChanged(SongDownloadStatus.Completed);

                // check if last recorded song is not an advertisement
                //if (_downloadedSongIsAdvertisement())
                //{
                //    MessageBox.Show("Prsomet");
                //    System.IO.File.Move(mp3Path, mp3Path + "PIRSOMET");
                //}


            }
            else if (_isYouTubeDownloaded)
            {
                _isYouTubeDownloaded = false;
                _statusChanged(SongDownloadStatus.Completed);
            }


            // continue to next song if exists
            if (_songsToDownload == null) return;

            if (_songsToDownload.Count > _indexOfCurrentSongToDownload)
            {
                StartDownloadSong(_songsToDownload[_indexOfCurrentSongToDownload]);
                _indexOfCurrentSongToDownload++;
            }
            else
            {
                _songsToDownload = null;
            }
        }

        /// <summary>
        /// Checks if the recorded song was an advertisement
        /// </summary>
        /// <returns>true if advertisement</returns>
        private bool _downloadedSongIsAdvertisement()
        {
            var prnt = "";
            bool ad = false, trck = false, res = false, name = false;
            if (LocalSpotifyAPI.GetStatus().Track == null)
            {
                trck = true;
                prnt = "track is null";
                //MessageBox.Show(prnt);
                return false;
            }

            ad = LocalSpotifyAPI.GetStatus().Track.IsAd();

            if (LocalSpotifyAPI.GetStatus().Track.TrackResource == null)
            {
                res = true;
                prnt = "res is null and ad is " + ad.ToString();
                //MessageBox.Show(prnt);
                return false;
            }

            if (LocalSpotifyAPI.GetStatus().Track.TrackResource.Name == null)
            {
                name = true;
                prnt = "name is null and ad is " + ad.ToString();
                //MessageBox.Show(prnt);
                return false;
            }

            prnt = "name is " + LocalSpotifyAPI.GetStatus().Track.TrackResource.Name + " and ad is " + ad.ToString();
            //MessageBox.Show(prnt);
            //if (LocalSpotifyAPI.GetStatus().Track.IsAd())
            //    return true;


            return false;
        }

        /// <summary>
        /// Starts downloading a list of songs (represented as Tags objects).
        /// It tells Downtify to keep calling StartDownloadSong + _FinishDownloadSong 
        /// until no more songs are left to be downloaded.
        /// </summary>
        /// <param name="tags">List of songs (represented as Tags objects)</param>
        public void DownloadSongs(List<Tags> tags)
        {
            if (tags == null || !tags.Any()) return;

            _indexOfCurrentSongToDownload = 0;
            _songsToDownload = tags;
            StartDownloadSong(_songsToDownload[_indexOfCurrentSongToDownload]);
            _indexOfCurrentSongToDownload++;
        }

        //// StratProcess
        //public static int PROCESS_START_SUCCESSFULLY = 0;
        //public static int PROCESS_ALREADY_RUNNING = 1;
        //public static int PROCESS_START_FAILURE = 2;

        ///// <summary>
        ///// 
        ///// </summary>
        ///// <param name="processName">The process name in task manager</param>
        ///// <param name="processExe">the process exe file name</param>
        ///// <returns> 0 - process started successfully.
        /////           1 - process already started.
        /////           2 - can't start process.</returns>
        //public static int StratProcess(string processName, string processExe)
        //{
        //    var runningProcessByName = Process.GetProcessesByName(processName);
        //    if (runningProcessByName.Length == 0)
        //    {
        //        // start process
        //        ProcessStartInfo processStartInfo = new ProcessStartInfo(processExe);
        //        Process process = new Process();
        //        process.StartInfo = processStartInfo;

        //        // if failed to start
        //        if (!process.Start())
        //        {
        //            return PROCESS_START_FAILURE;
        //        }
        //        return PROCESS_START_SUCCESSFULLY;
        //    }
        //    return PROCESS_ALREADY_RUNNING;
        //}

        // ##################################################################
        // ############################## EVENTS ############################
        // ##################################################################


        // ######################### DOWNLOAD STATUS ########################

        /// <summary> 
        /// The status of the song
        /// </summary>
        public enum SongDownloadStatus
        {
            Recording,
            Downloading,
            ConvertingToMP3,
            WritingTags,
            Completed,
            Error
        }

        // EventArgs wrapper
        public class SongDownloadStatusChangedEventArgs : EventArgs
        {
            public Tags Tags { get; set; }
            public SongDownloadStatus SongDownloadStatus { get; set; }
            public int IndexOfCurrentSongToDownload { get; set; }
        }

        // Delegation 
        public event EventHandler<SongDownloadStatusChangedEventArgs> SongDownloadStatusChanged;

        // Handler
        protected virtual void OnSongDownloadStatusChanged(SongDownloadStatusChangedEventArgs e)
        {
            SongDownloadStatusChanged?.Invoke(this, e);
        }

        /// <summary>
        /// Invokes the SongDownloadStatusChanged with the desired status 
        /// </summary>
        /// <param name="status">the new status</param>
        private void _statusChanged(SongDownloadStatus status)
        {
            SongDownloadStatusChanged?.Invoke(this, new SongDownloadStatusChangedEventArgs()
            {
                SongDownloadStatus = status,
                Tags = _tagsOfCurrentSongToDownload,
                IndexOfCurrentSongToDownload = _indexOfCurrentSongToDownload
            });

        }


        // ############################ NEW EVENT ###########################
        /// <summary> 
        /// The EventEnum
        /// </summary>
        public enum EventEnum
        {
            One,
        }

        // EventArgs wrapper
        public class EventEventArgs : EventArgs
        {
            public int Member { get; set; }

        }

        // Delegation 
        public event EventHandler<EventEventArgs> EventChanged;

        // Handler
        protected virtual void OnEventChanged(EventEventArgs e)
        {
            EventChanged?.Invoke(this, e);
        }
    }

    public class YouTubeDownloader
    {
    }
}


