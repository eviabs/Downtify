using NAudio.Lame;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;

namespace Downtify
{
    /// <summary>
    /// A recorder that records the computer's out-line in
    ///  WAV format, and converts it to mp3 file. the "state"
    /// attribute of the class should be checked every time a
    /// method is called.
    /// </summary>
    internal class Recorder : IDisposable
    {
        /// <summary> 
        /// The state of the current recording object.
        /// </summary>
        public enum RecorderState
        {
            New, // recorder was only instantiated, and am MP3 file should be set 
            EmptyFile, // file does not exists and ready no be written
            WavFileExists, // WAV file already exists
            Mp3FileExists, // mp3 file already exists
            Recording, // file is being written
            RecordingStopped, // file is not being written at the moment
            RecordingFinsihed, // file was being written, and is closed.
            ConvertingInProgress, // conversion is in progress 
            ConvertingFinsihed, // conversion completed, temp file is not needed anymore and mp3 file is ready for tagging
            ConvertError // conversion failed        
        }

        // ##################################################################
        // ########################## Data members ##########################
        // ##################################################################
        private RecorderState _state = RecorderState.New;
        private WasapiLoopbackCapture _captureInput;
        private WaveFileWriter _waveWriter;
        private bool _isRecording;
        private string _mp3FilePath;
        private string _tempWavFilePath;
        private LAMEPreset _mp3Preset;

        // ##################################################################
        // ############################# Methods ############################
        // ##################################################################
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="tempWavFilePath">The filename of the temp raw WAV file</param> 
        /// <param name="mp3Quality">The MP3 quality. Check NAudio.Lame.LAMEPreset enum for details </param> 
        public Recorder(string tempWavFilePath, LAMEPreset mp3Quality)
        {
            // check if WAV file already exists
            if (File.Exists(_tempWavFilePath = tempWavFilePath))
            {
                _state = RecorderState.WavFileExists;
            }

            _mp3Preset = mp3Quality;
        }

        /// <summary>
        /// Getter for TempWavFilePath
        /// </summary>
        /// <returns>Current TempWavFilePath string</returns>
        public string GetTempWavFilePath()
        {
            return _tempWavFilePath;
        }

        /// <summary>
        /// Setter for TempWavFilePath
        /// </summary>
        /// <param name="tempWavFilePath">path to temp file</param>
        public void SetTempWavFilePath(string tempWavFilePath)
        {
            // Change path only when safe to change
            if (_state != RecorderState.Recording ||
                _state != RecorderState.RecordingStopped ||
                _state != RecorderState.RecordingFinsihed ||
                _state != RecorderState.ConvertingInProgress)
            {
                _tempWavFilePath = tempWavFilePath;
            }
        }

        /// <summary>
        /// Setter for MP3Preset
        /// </summary>
        /// <param name="mp3Preset">the MP3 preset</param>
        public void SetMP3Preset(LAMEPreset mp3Preset)
        {
            _mp3Preset = mp3Preset;
        }

        /// <summary>
        /// Getter for MP3Preset
        /// </summary>
        /// <returns>Current MP3Preset string</returns>
        public LAMEPreset GetMP3Preset()
        {
            return _mp3Preset;
        }

        /// <summary>
        /// Getter for state attribute
        /// </summary>
        /// <returns>The state of the recorder</returns>
        public RecorderState GetState()
        {
            return _state;
        }

        /// <summary>
        /// Initializes the recorder. 
        /// This must be called before recording.
        /// </summary>
        private void _Init()
        {
            _captureInput = new WasapiLoopbackCapture();
            WaveFormat saveWaveFormat = _captureInput.WaveFormat;
            _waveWriter = new WaveFileWriter(_tempWavFilePath, saveWaveFormat);
            _captureInput.DataAvailable += _OnDataAvailable;
        }

        /// <summary>
        /// This methods sets a new MP3 file to the recorder. You must set an MP3 file
        /// before any recording takes place.
        /// </summary>
        /// <param name="mp3FilePath">full path to the mp3 destination file</param>
        public void SetMP3File(string mp3FilePath)
        {
            _state = RecorderState.EmptyFile;

            // check if mp3 file already exists
            //this.MP3FilePath = MP3FilePath;
            if (File.Exists(_mp3FilePath = mp3FilePath))
            {
                _state = RecorderState.Mp3FileExists;
            }
        }

        /// <summary>
        /// Start recording sound to the WAV file.
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording) return;

            _state = RecorderState.RecordingStopped;
            if (_captureInput == null || _waveWriter == null)
            {
                _Init();
            }

            try
            {
                _captureInput?.StartRecording();
                _isRecording = true;
                _state = RecorderState.Recording;
                Debug.WriteLine("Recording started.\n\tFile=" + _tempWavFilePath + "\n");
            }
            catch (Exception e)
            {
                Debug.WriteLine("Error occurred while start Recording:" + e.Message);
            }
        }

        /// <summary>
        /// Finish recording and close WAV file.
        /// </summary>
        public void StopRecording()
        {
            if (!_isRecording) return;

            if (_captureInput != null)
            {
                _captureInput.DataAvailable -= _OnDataAvailable;
                _captureInput.StopRecording();
                GC.SuppressFinalize(_captureInput);
                _captureInput.Dispose();
                _captureInput = null;
            }
            if (_waveWriter != null)
            {
                _waveWriter.Flush();
                _waveWriter.Close();
                _waveWriter.Dispose();
                GC.SuppressFinalize(_waveWriter);
                Debug.WriteLine("Recording finished.\n\tFile=" + _tempWavFilePath + "\n");
                _waveWriter = null;
            }
            _isRecording = false;
            _state = RecorderState.RecordingFinsihed;
            Debug.WriteLine("Recording of " + _tempWavFilePath + " stopped");
        }

        /// <summary>
        /// Converts the recorded WAV file to MP3. The WAV file will be converted
        /// ONLY if the recording was finished successfully (using StopRecording).
        /// </summary>
        public void ConvertToMP3()
        {
            if (!_isRecording)
            {
                // Convert WAV to MP3 using libmp3lame library
                try
                {
                    _state = RecorderState.ConvertingInProgress;
                    using (var reader = new AudioFileReader(_tempWavFilePath))
                    using (var writer = new LameMP3FileWriter(_mp3FilePath, reader.WaveFormat, _mp3Preset))
                        reader.CopyTo(writer);
                }
                catch (Exception)
                {


                    _state = RecorderState.ConvertError;

                    return;
                }
            }
            else
            {
                _state = RecorderState.ConvertError;
                return;
            }

            _state = RecorderState.ConvertingFinsihed;
        }

        /// <summary>
        /// Write handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void _OnDataAvailable(object sender, WaveInEventArgs e)
        {
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        /// <summary>
        /// Disposer. Any unfinished recording will be finished without conversion.
        /// </summary>
        public void Dispose()
        {
            StopRecording();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Destructor
        /// </summary>
        ~Recorder()
        {
            Dispose();
        }
    }
}
