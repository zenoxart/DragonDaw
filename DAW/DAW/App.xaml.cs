using System.IO;
using System.Windows;
using DAW.Views;
using NAudio.Wave;

namespace DAW
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize the global app logger early
            _ = Services.AppLogger.Instance;

            // Load persisted theme, language and UI scale
            MVVM.Views.OptionsWindow.LoadAndApplyPersistedSettings();
        }

        private async void App_Startup(object sender, StartupEventArgs e)
        {
            // Show splash immediately
            var splash = new MVVM.Views.SplashScreenWindow();
            splash.Show();

            // Step 1 – basic init already done in OnStartup
            splash.SetStatus("Initialisiere …");
            splash.SetProgress(0.15);
            await Task.Delay(80);

            // Step 2 – build main window (ViewModel ctor, services, etc.)
            splash.SetStatus("Lade Projekt-Services …");
            splash.SetProgress(0.40);
            await Task.Delay(80);

            MainWindow mainWindow = null!;
            await Task.Run(() =>
            {
                // Run on UI thread via dispatcher since WPF controls
                // must be created on the UI thread
                Dispatcher.Invoke(() =>
                {
                    splash.SetStatus("Erstelle Benutzeroberfläche …");
                    splash.SetProgress(0.65);
                    mainWindow = new MainWindow();
                });
            });

            // Step 3 – finish loading
            splash.SetStatus("Starte Audio-Engine …");
            splash.SetProgress(0.85);
            await Task.Delay(80);

            splash.SetStatus("Bereit.");
            splash.SetProgress(1.0);
            await Task.Delay(180);

            // Show main window, then fade splash out
            // Start the sound at the same time as the fade so it lands ~halfway through
            mainWindow.Show();
            MainWindow = mainWindow;

            PlayStartupSound();
            await splash.FadeOutAsync();
        }

        /// <summary>
        /// Plays DragonSound.wav once after the splash screen closes.
        /// Uses NAudio so the same dependency already in the project handles it.
        /// Self-disposing: the WaveOutEvent is released when playback finishes.
        /// </summary>
        private static void PlayStartupSound()
        {
            try
            {
                var exeDir   = AppContext.BaseDirectory;
                var wavPath  = Path.Combine(exeDir, "Assets", "Audio", "DragonSound.wav");
                if (!File.Exists(wavPath)) return;

                var reader = new AudioFileReader(wavPath);
                var output = new WaveOutEvent();
                output.Init(reader);

                // Release resources as soon as playback finishes
                output.PlaybackStopped += (_, _) =>
                {
                    output.Dispose();
                    reader.Dispose();
                };

                output.Play();
            }
            catch
            {
                // Never crash the app over a startup sound
            }
        }
    }
}
