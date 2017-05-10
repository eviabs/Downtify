using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Downtify
{
    /// <summary>
    /// Controls the audio channels:
    /// 1) Change volume
    /// 2) Mute all channels except a single source (called RecordingSource)
    /// </summary>
    class Sound : IDisposable
    {
        /// <summary>
        /// A single audio session (source)
        /// </summary>
        public class AudioSession : IDisposable
        {
            // public AudioSessionControl SessionControl { get; set; }
            public string SessionInstanceIdentifier { get; set; }
            public bool isMuted { get; set; }
            public AudioSessionControl Session { get; set; }
            public string SessionName { get { return sSessionName; } }
            public int Volume
            {
                get { return (int)(Session.SimpleAudioVolume.Volume * 100); }
                set { Session.SimpleAudioVolume.Volume = value / 100; }
            }
            private string sSessionName = "";
            public EventClient eventClient;

            public AudioSession(AudioSessionControl Session)
            {
                EventClient eventClient = new EventClient();
                eventClient.VolumeChanged += OnVolumeChanged;
                eventClient.StateChanged += OnStateChanged;
                eventClient.SessionDisconnect += OnSessionDisconnect;
                SessionInstanceIdentifier = Session.GetSessionInstanceIdentifier;
                this.eventClient = eventClient;
                sSessionName = Session.DisplayName;
                this.Session = Session;
                Session.RegisterEventClient(eventClient);

                // Refresh if DLL Path
                sSessionName = LoadStringFromDLL.LoadStringFromDLLByPath(Session.DisplayName);
                if (string.IsNullOrEmpty(sSessionName))
                {
                    sSessionName = GetNameByID(Session.GetProcessID);
                }
                isMuted = Session.SimpleAudioVolume.Mute;
                // System.Diagnostics.Debug.WriteLine("New Audio Session: " + sSessionName + " PID:" + Session.GetProcessID.ToString() + " IsMuted:" + this.isMuted);
            }

            public void OnStateChanged(object sender, StateChangedEventArgs e)
            {
                OnStateChanged(e);
            }
            public event EventHandler<StateChangedEventArgs> StateChanged;
            protected virtual void OnStateChanged(StateChangedEventArgs e)
            {
                EventHandler<StateChangedEventArgs> handler = StateChanged;
                if (handler != null)
                {
                    handler(this, e);
                }
            }
            public void OnVolumeChanged(object sender, VolumeEventArgs e)
            {
                isMuted = Session.SimpleAudioVolume.Mute;
                OnSessionVolumeChanged(e);
            }
            public event EventHandler<VolumeEventArgs> SessionVolumeChanged;
            protected virtual void OnSessionVolumeChanged(VolumeEventArgs e)
            {
                EventHandler<VolumeEventArgs> handler = SessionVolumeChanged;
                if (handler != null)
                {
                    handler(this, e);
                }
            }
            public void OnSessionDisconnect(object sender, DisconnectSessionEventArgs e)
            {
                Debug.WriteLine("SESSION DISCONNECTED: " + e.DisconnectReason);
                OnSessionDisconnect(e);
            }
            public event EventHandler<DisconnectSessionEventArgs> SessionDisconnect;
            protected virtual void OnSessionDisconnect(DisconnectSessionEventArgs e)
            {
                EventHandler<DisconnectSessionEventArgs> handler = SessionDisconnect;
                if (handler != null)
                {
                    handler(this, e);
                }
            }
            public void Dispose()
            {
                GC.SuppressFinalize(this);
            }
            ~AudioSession()
            {
                // Dispose();
            }
        }

        /// <summary>
        /// StringLoader
        /// </summary>
        public class StringLoader : IDisposable
        {
            [DllImport("kernel32", CharSet = CharSet.Auto)]
            static extern IntPtr LoadLibrary(string filename);
            [DllImport("kernel32", CharSet = CharSet.Auto)]
            static extern bool FreeLibrary(IntPtr lib);
            [DllImport("user32", CharSet = CharSet.Auto)]
            static extern int LoadString(IntPtr hInstance, int uID, out IntPtr buffer, int BufferMax);
            IntPtr plibPtr;
            public StringLoader(string sFileName)
            {
                string sDLL = Environment.ExpandEnvironmentVariables(sFileName);
                plibPtr = LoadLibrary(sDLL);
                if (plibPtr == IntPtr.Zero)
                    throw new Win32Exception();
            }
            public string Load(int id)
            {
                IntPtr resource;
                int length = LoadString(plibPtr, id, out resource, 0);
                if (length == 0)
                    return null;
                return Marshal.PtrToStringAuto(resource, length);
            }
            public void Dispose()
            {
                // http://blogs.msdn.com/b/jonathanswift/archive/2006/10/03/dynamically-calling-an-unmanaged-dll-from-.net-_2800_c_23002900_.aspx
                FreeLibrary(plibPtr);
                GC.SuppressFinalize(this);
            }
            ~StringLoader()
            {
                Dispose();
            }
        }

        /// <summary>
        /// Loads a String from a DLL
        /// </summary>
        public class LoadStringFromDLL
        {
            public static string LoadStringFromDLLByPath(string StringPath)
            {
                if (StringPath.StartsWith(@"@"))
                {
                    string[] aPathDetails = StringPath.Split(',');
                    string DllString = string.Empty;
                    if (aPathDetails.Length == 2)
                    {
                        string sDLLPath = aPathDetails[0].Replace(@"@", "");
                        if (!File.Exists(Environment.ExpandEnvironmentVariables(sDLLPath)))
                        {
                            return string.Empty;
                        }
                        StringLoader sl = new StringLoader(sDLLPath);
                        try
                        {
                            DllString = sl.Load(Convert.ToInt32(aPathDetails[1].Replace("-", "")));
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("Could not extract String from DLL Path " + StringPath + ". Error:" + e.Message);
                            DllString = string.Empty;
                        }
                        sl.Dispose();
                        return DllString;
                    }
                    return string.Empty;
                }
                return StringPath;
            }
        }

        private MMDeviceEnumerator aMMDevices;
        private SessionCollection coSessionsDefaultAudioEndPointDevice;
        private AudioSessionManager oSessionDefaultAudioEndPointDevice;

        public MMDeviceEnumerator MMDevices { get { return aMMDevices; } }
        public MMDevice DefaultAudioEndPointDevice { get; set; }
        public AudioSessionManager SessionDefaultAudioEndPointDevice { get { return oSessionDefaultAudioEndPointDevice; } }
        public SessionCollection SessionsDefaultAudioEndPointDevice { get { return coSessionsDefaultAudioEndPointDevice; } }
        public string SessionInstanceIdentifierRecordingSource { get; set; }
        public string RecordingSourceProcessName { get; set; }
        public AudioSessionControl RecordingSourceAudioSession { get; set; }
        public List<AudioSession> AudioSessions { get; set; }
        public int DefaultAudioDeviceVolume { get; set; }

        public Sound()
        {
            RecordingSourceProcessName = "";
            AudioSessions = new List<AudioSession>();
            aMMDevices = new MMDeviceEnumerator();
            DefaultAudioEndPointDevice = aMMDevices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            DefaultAudioEndPointDevice.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolumeDelegate;
            oSessionDefaultAudioEndPointDevice = DefaultAudioEndPointDevice.AudioSessionManager;
            coSessionsDefaultAudioEndPointDevice = oSessionDefaultAudioEndPointDevice.Sessions;
            oSessionDefaultAudioEndPointDevice.OnSessionCreated += AudioSessionCreatedDelegate;
            DefaultAudioDeviceVolume = (int)(DefaultAudioEndPointDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
            LoadAudioSessions();
        }

        public Sound(string RecordingSourceProcessName) : this()
        {
            this.RecordingSourceProcessName = RecordingSourceProcessName;
        }

        void AudioEndpointVolumeDelegate(AudioVolumeNotificationData data)
        {
            // System.Diagnostics.Debug.WriteLine("Master Volume changed" );
            VolumeEventArgs oVolEvntArgs = new VolumeEventArgs(data.MasterVolume, data.Muted);
            DefaultAudioDeviceVolume = (int)(data.MasterVolume * 100);
            OnMasterVolumeChangedOccured(oVolEvntArgs);
        }

        void AudioSessionCreatedDelegate(object sender, IAudioSessionControl data)
        {
            Debug.WriteLine("Session created");
            LoadAudioSessions();
            OnNewAudioSessionOccured(new SessionCreatedEventArgs(data));
        }

        /// <summary>
        /// Load all audio sessions
        /// </summary>
        public void LoadAudioSessions()
        {
            // Get all current sessions
            Hashtable MissingInstanceIdentifier = new Hashtable();
            foreach (AudioSession Current in AudioSessions)
            {
                MissingInstanceIdentifier.Add(Current.SessionInstanceIdentifier, true);
            }
            try
            {
                oSessionDefaultAudioEndPointDevice.RefreshSessions();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Cannot refresh Audio Sessions." + e.Message);
            }
            coSessionsDefaultAudioEndPointDevice = oSessionDefaultAudioEndPointDevice.Sessions;
            for (int iSession = 0; iSession < coSessionsDefaultAudioEndPointDevice.Count; iSession++)
            {
                if (MissingInstanceIdentifier.ContainsKey(coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier))
                {
                    MissingInstanceIdentifier.Remove(coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier);
                }
                // System.Diagnostics.Debug.WriteLine("Session:" + coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier);
                if (Process.GetCurrentProcess().Id != coSessionsDefaultAudioEndPointDevice[iSession].GetProcessID)
                {
                    AudioSession FindSessions = AudioSessions.Find(s => s.SessionInstanceIdentifier == coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier);
                    if (FindSessions == null)
                    {
                        AudioSession CurrentSession = new AudioSession(coSessionsDefaultAudioEndPointDevice[iSession]);
                        AudioSessions.Add(CurrentSession);
                        CurrentSession.SessionVolumeChanged += OnAllVolumeChanged;
                        CurrentSession.StateChanged += OnAllStateChanged;
                        coSessionsDefaultAudioEndPointDevice[iSession].RegisterEventClient(CurrentSession.eventClient);
                        Debug.WriteLine("Add Audio session:" + coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier);
                    }
                }
                Process[] aProcesses = Process.GetProcessesByName(RecordingSourceProcessName);
                int ProcessCount = aProcesses.Count(s => s.Id == (coSessionsDefaultAudioEndPointDevice[iSession].GetProcessID));
                if (ProcessCount >= 1)
                {
                    // Spotify detected
                    RecordingSourceAudioSession = coSessionsDefaultAudioEndPointDevice[iSession];
                    SessionInstanceIdentifierRecordingSource = coSessionsDefaultAudioEndPointDevice[iSession].GetSessionInstanceIdentifier;
                }
            }
            // Remove Left Sessions
            foreach (string OldSession in MissingInstanceIdentifier.Keys)
            {
                AudioSessions.Remove(AudioSessions.Find(s => s.SessionInstanceIdentifier == OldSession));
            }
            AudioSessionsReload(new EventArgs());
        }

        public int GetRecordingSourceVolume()
        {
            if (!(string.IsNullOrEmpty(SessionInstanceIdentifierRecordingSource)))
            {
                AudioSession ASession = AudioSessions.Find(s => s.SessionInstanceIdentifier == SessionInstanceIdentifierRecordingSource);
                if (ASession != null)
                {
                    try
                    {
                        return ASession.Volume;
                    }
                    catch
                    {
                        return -1;
                    }

                }
                return -1;
            }
            return -1;
        }

        public void SetRecordingSourceVolume(int Volume)
        {
            if (RecordingSourceAudioSession != null)
            {
                try
                {
                    RecordingSourceAudioSession.SimpleAudioVolume.Volume = ((float)Volume / 100);
                }
                catch
                {
                    Debug.WriteLine("Could not set Volume on Spotify Audiosession");
                }
            }
        }

        public void SetDefaultAudioDeviceVolume(int Volume)
        {
            float fNewVolume = (float)0.0;
            if (float.TryParse(Volume.ToString(), out fNewVolume))
            {
                DefaultAudioEndPointDevice.AudioEndpointVolume.MasterVolumeLevelScalar = (fNewVolume / 100);
            }
        }

        public void MuteAllSessionsExceptRecordingSource()
        {
            MuteAllSessionsExceptThisProcessName(RecordingSourceProcessName);
        }

        public void MuteAllSessionsExceptThisProcessName(string ProcessName)
        {
            Process[] aProcesses = Process.GetProcessesByName(ProcessName);
            //for (int iSession = 0; iSession < this.SessionsDefaultAudioEndPointDevice.Count; iSession++)
            foreach (AudioSession ASession in AudioSessions)
            {
                if (ASession != null)
                {
                    int ProcessCount = aProcesses.Count(s => s.Id == (ASession.Session.GetProcessID));
                    // IEnumerable<int> ProcessIDs = from s in aProcesses where s.Id == (this.SessionsDefaultAudioEndPointDevice[iSession].GetProcessID) select s.Id;
                    if (ProcessCount >= 1)
                    {
                        Debug.WriteLine("Process " + ASession.SessionName + " found in Audiosesions => not muting");
                    }
                    else
                    {
                        Debug.WriteLine("Muting " + ASession.SessionName);
                        ASession.Session.SimpleAudioVolume.Mute = true;
                    }
                }
            }
            LoadAudioSessions();
        }

        public void UnMuteAllSessions()
        {
            foreach (AudioSession ASession in AudioSessions)
            {
                if (ASession != null)
                {
                    Debug.WriteLine("Unmuting " + ASession.SessionName);
                    ASession.Session.SimpleAudioVolume.Mute = false;
                }
            }
            LoadAudioSessions();
        }


        public event EventHandler<EventArgs> OnAudioSessionReloaded;

        protected virtual void AudioSessionsReload(EventArgs e)
        {
            Debug.WriteLine("AUDIO SESSIONS RELAODED");
            EventHandler<EventArgs> handler = OnAudioSessionReloaded;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public void OnAllVolumeChanged(object sender, VolumeEventArgs e)
        {
            OnVolumeChangedOccured(e);
        }

        public void OnAllStateChanged(object sender, StateChangedEventArgs e)
        {
            //System.Diagnostics.Debug.WriteLine("A SESSION CHANGED HIS STATE:" + e.NewState);
            LoadAudioSessions();
            OnStateChangedOccured(e);
        }

        private static string GetNameByID(UInt32 iPID)
        {
            try
            {
                Process oProcess = Process.GetProcessById((int)iPID);
                return (oProcess.ProcessName);
            }
            catch (Exception e)
            {
                Debug.WriteLine("GetProcessById:" + e.Message);
                return (string.Empty);
            }
        }

        protected virtual void OnNewAudioSessionOccured(SessionCreatedEventArgs e)
        {
            EventHandler<SessionCreatedEventArgs> handler = OnNewAudioSession;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public event EventHandler<SessionCreatedEventArgs> OnNewAudioSession;

        public class SessionCreatedEventArgs : EventArgs
        {
            public SessionCreatedEventArgs(IAudioSessionControl Session)
            {
                this.Session = Session;
            }
            IAudioSessionControl Session { get; set; }
        }

        public event EventHandler<VolumeEventArgs> OnMasterVolumeChanged;

        protected virtual void OnMasterVolumeChangedOccured(VolumeEventArgs e)
        {
            EventHandler<VolumeEventArgs> handler = OnMasterVolumeChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public event EventHandler<VolumeEventArgs> OnVolumeChanged;

        protected virtual void OnVolumeChangedOccured(VolumeEventArgs e)
        {
            EventHandler<VolumeEventArgs> handler = OnVolumeChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnStateChangedOccured(StateChangedEventArgs e)
        {
            EventHandler<StateChangedEventArgs> handler = OnStateChangedGlobal;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        public event EventHandler<StateChangedEventArgs> OnStateChangedGlobal;

        public class StateChangedEventArgs : EventArgs
        {
            public StateChangedEventArgs(AudioSessionState NewState)
            {
                this.NewState = NewState;
            }
            public AudioSessionState NewState { get; set; }
        }

        public event EventHandler<DisconnectSessionEventArgs> OnSessionDisconnect;

        public class DisconnectSessionEventArgs : EventArgs
        {
            public DisconnectSessionEventArgs(AudioSessionDisconnectReason DisconnectReason)
            {
                this.DisconnectReason = DisconnectReason;
            }
            public AudioSessionDisconnectReason DisconnectReason { get; set; }
        }

        public class VolumeEventArgs : EventArgs
        {
            public VolumeEventArgs(float volume, bool isMuted)
            {
                this.volume = volume;
                this.isMuted = isMuted;
                eventContext = eventContext;
            }
            public float volume { get; set; }
            public bool isMuted { get; set; }
            public Guid eventContext { get; set; }
        }

        internal class EventClient : IAudioSessionEventsHandler
        {
            public delegate void VolumeEventHandler(object sender, VolumeEventArgs e);
            public event VolumeEventHandler VolumeChanged;
            public delegate void DisconnectSessionEventHandler(object sender, DisconnectSessionEventArgs e);
            public event DisconnectSessionEventHandler SessionDisconnect;
            public delegate void StateChangedEventHandler(object sender, StateChangedEventArgs e);
            public event StateChangedEventHandler StateChanged;
            public void OnSessionDisconnected(AudioSessionDisconnectReason DisconnectReason)
            {
                // System.Diagnostics.Debug.WriteLine("SESSION DISCONNECTED. EVENT CLIENT");
                if (SessionDisconnect != null)
                {
                    DisconnectSessionEventArgs oDisConnectSessionEventArgs = new DisconnectSessionEventArgs(DisconnectReason);
                    SessionDisconnect(this, oDisConnectSessionEventArgs);
                }
            }
            public void OnDisplayNameChanged(string NewDisplayName) { }
            public void OnGroupingParamChanged(ref Guid NewGroupingParam) { Debug.WriteLine("GROUP CHANGED"); }
            public void OnIconPathChanged(string NewIconPath) { }
            public void OnStateChanged(AudioSessionState NewState)
            {
                // System.Diagnostics.Debug.WriteLine("STATE CHANGED. EVENT CLIENT");
                if (StateChanged != null)
                {
                    StateChangedEventArgs oDisConnectSessionEventArgs = new StateChangedEventArgs(NewState);
                    StateChanged(this, oDisConnectSessionEventArgs);
                }
            }
            public void OnChannelVolumeChanged(uint ChannelCount, IntPtr NewChannelVolumeArray, uint ChangedChannel) { }
            public void OnVolumeChanged(float Volume, bool Muted)
            {
                // System.Diagnostics.Debug.WriteLine("VOLUME CHANGED. EVENT CLIENT");
                if (VolumeChanged != null)
                {
                    VolumeEventArgs oEventArgsVolume = new VolumeEventArgs(Volume, Muted);
                    VolumeChanged(this, oEventArgsVolume);
                }
            }
        }


        public void Dispose()
        {
            // http://blogs.msdn.com/b/jonathanswift/archive/2006/10/03/dynamically-calling-an-unmanaged-dll-from-.net-_2800_c_23002900_.aspx
            GC.SuppressFinalize(this);
        }

        ~Sound()
        {
            Dispose();
        }

    }
}
