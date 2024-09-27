using System;
using System.Windows;
using System.Windows.Controls;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Core;
using BuildSoft.OscCore;
using Melanchall.DryWetMidi.Interaction;
using Melanchall.DryWetMidi.Common;

namespace MidiPlayerWPF
{
    public partial class MainWindow : Window
    {
        private OutputDevice outputDevice;
        private Playback playback;
        private double playbackSpeed = 1.0;
        private OscServer oscServer;
        private MidiFile midiFile;
        private Random random = new Random();
        private Boolean randomizeNotes;

        public MainWindow()
        {
            InitializeComponent();
            InitializeMidiDevices();
            InitializeOsc();
            UpdateButtonStates();
        }

        private void InitializeMidiDevices()
        {
            try
            {
                var midiDevices = OutputDevice.GetAll();

                // Populate the ComboBox with the names of the MIDI devices
                MidiDevicesComboBox.ItemsSource = midiDevices.Select(device => device.Name).ToList();

                // Select the second device by default
                if (MidiDevicesComboBox.Items.Count > 0)
                {
                    MidiDevicesComboBox.SelectedIndex = 1;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing MIDI devices: {ex.Message}");
            }
        }

        private void InitializeOsc()
        {
            try
            {
                // Initialize the OSC server on port 9001
                oscServer = new OscServer(9001);
                oscServer.TryAddMethod("/play", HandlePlayMessage);
                oscServer.TryAddMethod("/stop", HandleStopMessage);
                oscServer.TryAddMethod("/speed", HandleSpeedMessage);
                oscServer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing OSC server: {ex.Message}");
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            StartPlayback();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }
        private void RandomizeNotes_Click(object sender, RoutedEventArgs e)
        {
            var IsActive = (bool)(sender as CheckBox).IsChecked;
            SetRandomizeNotes(IsActive);
        }

        

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            playbackSpeed = e.NewValue;
            if (SpeedLabel != null) {
                SpeedLabel.Text = $"{playbackSpeed:F2}×";
            }

            // If playback is running, update its speed in real-time
            if (playback != null && playback.IsRunning)
            {
                playback.Speed = playbackSpeed;
            }
        }
        private void MidiDevicesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedDeviceName = MidiDevicesComboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedDeviceName))
            {
                try
                {
                    outputDevice?.Dispose(); // Dispose the previous device if any
                    outputDevice = OutputDevice.GetByName(selectedDeviceName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error selecting MIDI device: {ex.Message}");
                }
            }
        }

        private void StartPlayback()
        {
            if (playback != null && playback.IsRunning)
            {
                playback.Stop();
            }
            var files = System.IO.Directory.GetFiles(@".\\assets", "*.mid");
            if (files.Length < 1) return;
            
            Random rnd = new Random();
            var midiFile = MidiFile.Read(files[rnd.Next(0, files.Length)]);

            //In the movie Amadeus, Mozart was criticized for using too many notes, let's remove some.
            //midiFile.RemoveNotes(n => n.NoteName == NoteName.CSharp);          
            
            //Make it sound lame by randomizing 
            if (randomizeNotes) midiFile = RandomizeNotes(midiFile);
            playback = midiFile.GetPlayback(outputDevice);
            
            playback.Finished += Playback_PlaybackFinished;
            playback.Speed = playbackSpeed;
            playback.Start();
            Dispatcher.Invoke(UpdateButtonStates);
        }
        //TODO, Nice-to-have: Apply this during the playback only for the notes in the upcoming time interval
        private MidiFile RandomizeNotes(MidiFile midiFile)
        {
            var random = new Random();
            var deltaTime = midiFile.GetTrackChunks().SelectMany(chunk => chunk.Events)
                .OfType<NoteOnEvent>()
                .Select(note => note.DeltaTime)
                .ToList();
            
            foreach (var trackChunk in midiFile.GetTrackChunks())
            {
                foreach (var noteEvent in trackChunk.Events.OfType<NoteOnEvent>())
                {
                    // Randomly shift each note's start time by up to 10ms
                    noteEvent.DeltaTime += (long)(random.NextDouble() * 10);
                    if (random.NextDouble() < 0.9)
                    {
                        noteEvent.NoteNumber = (SevenBitNumber)((int)noteEvent.NoteNumber + (random.Next(-1, 1)));
                    }
                    if (noteEvent.Velocity > 4) {
                        noteEvent.Velocity  = (SevenBitNumber)(noteEvent.Velocity + (int)(random.Next(-4, 4)));
                    }
                }
            }
            return midiFile;
        }

        private void SetRandomizeNotes(bool randomize)
        {
            randomizeNotes = randomize;
        }
        private void StopPlayback()
        {
            if (playback != null)
            {
                playback.Finished -= Playback_PlaybackFinished;
                playback.Stop();
            }
            Dispatcher.Invoke(UpdateButtonStates);
        }

        private void HandlePlayMessage(OscMessageValues values)
        {
            Dispatcher.Invoke(StartPlayback);
            Dispatcher.Invoke(UpdateButtonStates);
        }

        private void HandleStopMessage(OscMessageValues values)
        {
            Dispatcher.Invoke(StopPlayback);
            Dispatcher.Invoke(UpdateButtonStates);
        }

        private void HandleSpeedMessage(OscMessageValues values)
        {
            if (values.ElementCount >0)
            {
                Dispatcher.Invoke(() =>
                {
                    playbackSpeed = values.ReadFloatElement(0);
                    SpeedSlider.Value = playbackSpeed;
                    SpeedLabel.Text = $"{playbackSpeed:F2}×";
                    if (playback != null && playback.IsRunning)
                    {
                        playback.Speed = playbackSpeed;
                        var currenttime = playback.GetCurrentTime(TimeSpanType.Midi);
                    }
                });
            }
        }
        private void Playback_PlaybackFinished(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateButtonStates();
            });
        }
        private void UpdateButtonStates()
        {
            if (playback != null && playback.IsRunning)
            {
                PlayButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                CheckboxRandomizeNotes.IsEnabled = false;
            }
            else
            {
                PlayButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                CheckboxRandomizeNotes.IsEnabled = true;
            }
        }
        protected override void OnClosed(EventArgs e)
        {
            playback?.Dispose();
            outputDevice?.Dispose();
            oscServer?.Dispose();
            base.OnClosed(e);
        }
    }
}