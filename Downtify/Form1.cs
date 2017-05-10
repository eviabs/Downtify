using NAudio.Lame;
using Newtonsoft.Json.Linq;
using SpotifyAPI.Local;
using SpotifyAPI.Local.Enums;
using SpotifyAPI.Local.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Downtify
{
    public partial class Form1 : Form
    {

        private Downtify _downtify;
        private SpotifyLocalAPI _spotify;
        private List<Tags> songs;
        private Track _currentTrack;

        private static int _sleepBeforeReconnect = 500;


        private string pl = "spotify:user:eviabs:playlist:0H7xiWAPNkLynivnabb4W2"; // Single
        //private string pl = "spotify:user:eviabs:playlist:0h31QZbAkbHLongNLUR8uo"; // A's
        //private string pl = "spotify:user:eviabs:playlist:4bZ7UgmU9vnVjLzPzPgsC5"; // Shorts

        // Set recorder
        Recorder rec = new Recorder(@"C:\Users\Evyatar Ben-Shitrit\Desktop\test.wav", LAMEPreset.INSANE);
        Sound sound = new Sound("Spotify");

        public Form1()
        {
            InitializeComponent();
            _downtify = new Downtify(@"C:\\Users\\Evyatar Ben-Shitrit\\Desktop\\", @"C:\\Users\\Evyatar Ben-Shitrit\\Desktop\\", LAMEPreset.INSANE);
            _downtify.SongDownloadStatusChanged += DSongDownloadStatusChanged;
            _spotify = _downtify.LocalSpotifyAPI;



            // set localapi instance's events
            _spotify.OnPlayStateChange += _spotify_OnPlayStateChange;
            _spotify.OnTrackChange += _spotify_OnTrackChange;
            _spotify.OnTrackTimeChange += _spotify_OnTrackTimeChange;
            _spotify.OnVolumeChange += _spotify_OnVolumeChange;
            _spotify.SynchronizingObject = this;

            textBox1.Text = pl;

            this.Shown += Form1_Shown;
        }

        private void Form1_Shown(object sender, EventArgs e)
        {

            int tries = 3;
            bool a = true;
            bool b = true;
            while (tries > 0)
            {
                tries--;
                //label13.Text = "Connecting to spotify";
                SpotifyLocalAPI.RunSpotify();
                SpotifyLocalAPI.RunSpotifyWebHelper();

                Thread.Sleep(_sleepBeforeReconnect);
                var connectStatus = _downtify.Connect();

                if (!SpotifyLocalAPI.IsSpotifyRunning())
                {
                    //label13.Text = "spotify isnt runnig";
                    a = false;
                }

                if (!SpotifyLocalAPI.IsSpotifyWebHelperRunning())
                {
                    //label13.Text = "spotify helper isnt runnig";
                    b = false;
                }

                if (a && b)
                {
                    //label13.Text = "connected to spotify";
                    tries = 0;

                }
            }

            UpdateInfos();
            _spotify.ListenForEvents = true;

        }

        private async void button1_Click(object sender, EventArgs e)
        {
            button1.Enabled = false;
            songs = await _downtify.GetSongsFromPlaylist(textBox1.Text);

            foreach (Tags tags in songs)
            {
                ListViewItem i = new ListViewItem(tags.SongName);
                i.SubItems.Add(tags.Artist[0]);
                i.SubItems.Add(tags.Album);
                i.SubItems.Add(tags.Year + "");

                playListTracksListView.Items.Add(i);
            }
            button1.Enabled = true;
        }

        private void button9_Click(object sender, EventArgs e)
        {
            var tags = new Tags();
            songs = new List<Tags>() { tags };
            tags.SongName = "from youtube";
            tags.Uri = textBox1.Text;
            tags.Type = Tags.TagsType.Youtube;


            var i = new ListViewItem(tags.SongName);
            i.SubItems.Add("YT");
            i.SubItems.Add("YT");
            i.SubItems.Add("TY");

            playListTracksListView.Items.Add(i);

            //var youTubeVideoInfos = DownloadUrlResolver.GetDownloadUrls(tags.Uri);

            //foreach (var vid in youTubeVideoInfos)
            //{
            //    MessageBox.Show(vid.Title + " " + vid.VideoType.ToString() + " " + vid.CanExtractAudio.ToString());
            //}

            //var video = youTubeVideoInfos.Where(info => info.CanExtractAudio).OrderByDescending(info => info.AudioBitrate).First();
            ////var video = youTubeVideoInfos.First();
            //if (video.RequiresDecryption)
            //{
            //    DownloadUrlResolver.DecryptDownloadUrl(video);
            //}

            //var audioDownloader = new AudioDownloader(video, Path.Combine(@"C:\\Users\\Evyatar Ben-Shitrit\\Desktop\\", video.Title + video.AudioExtension));
            //audioDownloader.Execute();

            var _webClient = new WebClient();
            var url = "http://www.youtubeinmp3.com/fetch/?format=JSON&video=" + tags.Uri;
            var json = _webClient.DownloadString(url);
            var jsonObj = JObject.Parse(json);
            tags.SongName = jsonObj["title"].ToString(); ;
            var link = jsonObj["link"].ToString();
            _webClient.DownloadFile(link, @"C:\\Users\\Evyatar Ben-Shitrit\\Desktop\\" + tags.GetMP3DisplayName());



        }

        public void UpdateInfos()
        {
            StatusResponse status = _spotify.GetStatus();
            if (status == null)
            {
                //MessageBox.Show("no status");
                return;
            }

            //Basic Spotify Infos
            UpdatePlayingStatus(status.Playing);
            clientVersionLabel.Text = status.ClientVersion;
            versionLabel.Text = status.Version.ToString();
            repeatShuffleLabel.Text = status.Repeat + @" and " + status.Shuffle;

            if (status.Track != null)
                //Update track infos
                UpdateTrack(status.Track);
        }

        public async void UpdateTrack(Track track)
        {
            _currentTrack = track;

            if (track.TrackResource != null)
            {
                titleLinkLabel.Text = track.TrackResource.Name;
                titleLinkLabel.Tag = track.TrackResource.Uri;
            }
            else
            {
                titleLinkLabel.Text = "null";
                titleLinkLabel.Tag = "";
            }

            if (track.ArtistResource != null)
            {
                artistLinkLabel.Text = track.ArtistResource.Name;
                artistLinkLabel.Tag = track.ArtistResource.Uri;
            }
            else
            {
                artistLinkLabel.Text = "null";
                artistLinkLabel.Tag = "";
            }

            if (track.AlbumResource != null)
            {
                albumLinkLabel.Text = track.AlbumResource.Name;
                albumLinkLabel.Tag = track.AlbumResource.Uri;
            }
            else
            {
                albumLinkLabel.Text = "null";
                albumLinkLabel.Tag = "";
            }

            timeProgressBar.Maximum = track.Length;

            try
            {
                smallAlbumPicture.Image = await track.GetAlbumArtAsync(AlbumArtSize.Size160);
            }
            catch (Exception e)
            {
                smallAlbumPicture.Image = null;
                AddEvent("2", "Could not load image: " + e.Message);
            }
        }

        public void UpdatePlayingStatus(bool playing)
        {
            isPlayingLabel.Text = playing.ToString();
        }

        void _spotify_OnVolumeChange(object sender, VolumeChangeEventArgs e)
        {
            volumeLabel.Text = (e.NewVolume * 100).ToString(CultureInfo.InvariantCulture);
        }

        void _spotify_OnTrackTimeChange(object sender, TrackTimeChangeEventArgs e)
        {
            try
            {
                if (_currentTrack != null)
                    timeLabel.Text = FormatTime(e.TrackTime) + "/" + FormatTime(_currentTrack.Length);
                timeProgressBar.Value = (int)e.TrackTime;
            }
            catch (Exception er)
            {
                //ignore
            }
        }

        void _spotify_OnTrackChange(object sender, TrackChangeEventArgs e)
        {
            //MessageBox.Show("track chng");
            UpdateTrack(e.NewTrack);
        }

        void _spotify_OnPlayStateChange(object sender, PlayStateEventArgs e)
        {
            UpdatePlayingStatus(e.Playing);
        }

        private static string FormatTime(double sec)
        {
            TimeSpan span = TimeSpan.FromSeconds(sec);
            string secs = span.Seconds.ToString(), mins = span.Minutes.ToString();
            if (secs.Length < 2)
                secs = "0" + secs;
            return mins + ":" + secs;
        }

        private void playUrlBtn_Click(object sender, EventArgs e)
        {
            _spotify.PlayURL(playTextBox.Text, contextTextBox.Text);
        }

        private void playBtn_Click(object sender, EventArgs e)
        {
            _spotify.Play();
        }

        private void pauseBtn_Click(object sender, EventArgs e)
        {
            _spotify.Pause();
        }

        private void prevBtn_Click(object sender, EventArgs e)
        {
            _spotify.Previous();
        }

        private void skipBtn_Click(object sender, EventArgs e)
        {
            _spotify.Skip();
        }

        private void playListTracksListView_DoubleClick(object sender, EventArgs e)
        {
            if (playListTracksListView.SelectedItems.Count == 1)

            {
                _spotify.PlayURL(songs[playListTracksListView.SelectedItems[0].Index].FullTrack.Uri);
            }
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            await AsyncDownloadSongs();
        }


        //private void NonAsyncDownloadSongs()
        //{
        //    if (this.songs != null && this.songs.Count != 0)
        //    {
        //        foreach (Tags tag in this.songs)
        //        {
        //            this._spotify.PlayURL(tag.FullTrack.Uri);
        //            Thread.Sleep(tag.Length + 2000);
        //            string newsest = this.GetNewestFile();
        //            try
        //            {
        //                this._downtify.WriteTagsToFile(tag, newsest);
        //            }
        //            catch (Exception e)
        //            {
        //                MessageBox.Show("Error:" + e.Message);
        //            }
        //        }
        //    }
        //    else
        //    {
        //        MessageBox.Show("No Songs!");
        //    }
        //}

        private async Task AsyncDownloadSongs()
        {
            if (songs != null && songs.Count != 0)
            {
                await Task.Run(() =>
                {
                    //foreach (Tags tag in this.songs)
                    //{
                    //    try
                    //    {
                    //        this._downtify.StartDownloadSong(tag);

                    //        AddEvent("0", tag.SongName + " was added successfully!");
                    //        Thread.Sleep(SLEEP_BEFORE_NEXT_SONG);
                    //    }
                    //    catch (Exception er)
                    //    {
                    //        String err = er.Message;
                    //        AddEvent("1", tag.SongName + " : " + er);
                    //    }
                    //}
                    _downtify.DownloadSongs(songs);
                });
            }
            else
            {
                MessageBox.Show("No Songs!");
            }
        }

        private void AddEvent(string EventType, string EventTest)
        {
            BeginInvoke(new Action(delegate
            {
                ListViewItem i = new ListViewItem(EventType);
                i.SubItems.Add(DateTime.Now.ToString("HH:mm:ss dd-MM-yyyy"));
                i.SubItems.Add(EventTest);
                listView1.Items.Add(i);
            }));
        }

        private void playListTracksListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            //rec.StopRecording();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            rec.SetMP3File(@"C:\Users\Evyatar Ben-Shitrit\Desktop\test_dest.mp3");
            rec.StartRecording();

        }

        private void button4_Click(object sender, EventArgs e)
        {
            rec.StopRecording();
            rec.ConvertToMP3();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void button5_Click(object sender, EventArgs e)
        {
            sound.MuteAllSessionsExceptThisProcessName("Spotify");
        }

        private void button6_Click(object sender, EventArgs e)
        {
            sound.UnMuteAllSessions();
        }

        private void button7_Click(object sender, EventArgs e)
        {
            rec.ConvertToMP3();
        }

        protected void DSongDownloadStatusChanged(object sender, Downtify.SongDownloadStatusChangedEventArgs e)
        {
            var msg = "";


            switch (e.SongDownloadStatus)
            {
                case Downtify.SongDownloadStatus.Recording:
                    msg = "Recording";
                    break;
                case Downtify.SongDownloadStatus.ConvertingToMP3:
                    msg = "ConvertingToMP3";
                    break;
                case Downtify.SongDownloadStatus.WritingTags:
                    msg = "WritingTags";
                    break;
                case Downtify.SongDownloadStatus.Completed:
                    msg = "Completed";
                    break;
                case Downtify.SongDownloadStatus.Error:
                    msg = "Error";
                    break;
                case Downtify.SongDownloadStatus.Downloading:
                    msg = "Downloading";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            //label12.Text = msg;
            //MessageBox.Show(msg);
        }

        private void button8_Click(object sender, EventArgs e)
        {
            //ignore
        }

        private void button3_Click_1(object sender, EventArgs e)
        {
            Downtify.OrganizeSongsInFolders(textBox2.Text);
        }
    }
}

