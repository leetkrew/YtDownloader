using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LeetkrewYtDownloader.Models;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace LeetkrewYtDownloader;
public partial class MainWindow : Window
{
    // where we'll store the JSON
    private static readonly string SettingsDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LeetkrewYtDownloader"
        );
    private static readonly string SettingsFile =
        Path.Combine(SettingsDir, "windowSettings.json");
    
    public MainWindow()
    {
        InitializeComponent();
        // 1) Load last size if it exists
        TryLoadWindowSize();

        // 2) When the user closes or resizes the window, remember it
        this.Closing   += OnWindowClosing;
        // Save on every resize:
        this.SizeChanged += (_, _) => SaveWindowSize();
        
        UrlBox.Text = @"https://www.youtube.com/watch?v=0v37NNdjWKU";
        Logs.IsReadOnly = true;
    }

    private void TryLoadWindowSize()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var s    = JsonSerializer.Deserialize<WindowSizeRecord>(json);
                if (s is not null)
                {
                    // only apply if within your max constraints
                    Width  = Math.Min(s.Width,  MaxWidth);
                    Height = Math.Min(s.Height, MaxHeight);
                }
            }
        }
        catch
        {
            // ignore any errors (corrupt file, etc.)
        }
    }

    private void SaveWindowSize()
    {
        try
        {
            // ensure directory
            Directory.CreateDirectory(SettingsDir);

            var s = new WindowSizeRecord
            {
                Width  = this.Width,
                Height = this.Height
            };
            var json = JsonSerializer.Serialize(s);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // ignore IO errors
        }
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // explicitly save one last time
        SaveWindowSize();
    }
    
    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        Logs.Text = string.Empty;

        try
        {
            #if DEBUG
            Logs.Text += "[Info] Debug Mode\n";
            #endif
            
            // 1) Validate URL
            var url = UrlBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                Logs.Text += "[Error] Please enter a valid YouTube URL.\n";
                return;
            }

            // 2) Prepare YouTube client
            var userAgents = new[]
            {
                // Chrome on Windows
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/115.0.0.0 Safari/537.36",

                // Safari on macOS
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_6) " +
                "AppleWebKit/605.1.15 (KHTML, like Gecko) " +
                "Version/16.5 Safari/605.1.15",

                // Firefox on Linux
                "Mozilla/5.0 (X11; Linux x86_64; rv:117.0) " +
                "Gecko/20100101 Firefox/117.0",

                // Edge on Windows
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Edg/118.0.0.0 Safari/537.36"
            };

            var rng = new Random();
            var selectedUa = userAgents[rng.Next(userAgents.Length)];
            Logs.Text += $"[Info] {selectedUa}\n";
            
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                selectedUa
            );
            var youtube = new YoutubeClient(httpClient);
            Logs.Text += $"[Info] Resolving video metadata…\n";

            // 3) Get metadata + streams
            var video = await youtube.Videos.GetAsync(url);
            var manifest = await youtube.Videos.Streams.GetManifestAsync(video.Id);

            // 4) Build safe title
            var invalids = Path.GetInvalidFileNameChars();
            var titlePart = string.Join("_",
                video.Title
                     .Split(invalids, StringSplitOptions.RemoveEmptyEntries)
            ).Trim();
            if (string.IsNullOrEmpty(titlePart))
                titlePart = "youtube_video";

            // 5) Pick streams
            var videoStream = manifest.GetVideoOnlyStreams()
                .GetWithHighestVideoQuality()
                ?? throw new InvalidOperationException("No video-only stream found.");

            var audioStream = manifest.GetAudioOnlyStreams()
                .Where(s => s.Container.Name.Equals("mp4", StringComparison.OrdinalIgnoreCase))
                .GetWithHighestBitrate()
                ?? throw new InvalidOperationException("No MP4/AAC audio stream found.");

            // 6) Ask user where to save final MP4
            var options = new FilePickerSaveOptions
            {
                Title               = "Save Muxed Video",
                SuggestedFileName   = titlePart + ".mp4",
                DefaultExtension    = "mp4",
                FileTypeChoices     =
                [
                    new FilePickerFileType("MP4 Video")
                    {
                        Patterns = ["*.mp4"]
                    }
                ]
            };
            var result = await this.StorageProvider.SaveFilePickerAsync(options);
            if (result is null)
            {
                Logs.Text += "[Info] Save cancelled by user.\n";
                return;  // user canceled
            }

            var muxedFile = result.TryGetLocalPath();
            
            Logs.Text += $"[Info] Saving to: {muxedFile}\n";

            // Determine temp file locations in same directory
            var outputDir = Path.GetDirectoryName(muxedFile) ?? Directory.GetCurrentDirectory();
            var tempVideo = Path.Combine(outputDir, titlePart + "_video." + videoStream.Container.Name);
            var tempAudio = Path.Combine(outputDir, titlePart + "_audio." + audioStream.Container.Name);

            // 7) Download both to temp
            Logs.Text += "[Info] Starting downloads…\n";
            long vSize = videoStream.Size.Bytes;
            long aSize = audioStream.Size.Bytes;
            long doneV = 0, doneA = 0;

            var vProg = new Progress<double>(p =>
            {
                doneV = (long)(p * vSize);
                Dispatcher.UIThread.Post(() =>
                    ProgressBar.Value = (double)(doneV + doneA) / (vSize + aSize) * 100
                );
            });
            var aProg = new Progress<double>(p =>
            {
                doneA = (long)(p * aSize);
                Dispatcher.UIThread.Post(() =>
                    ProgressBar.Value = (double)(doneV + doneA) / (vSize + aSize) * 100
                );
            });

            await Task.WhenAll(
                youtube.Videos.Streams.DownloadAsync(videoStream, tempVideo, vProg).AsTask(),
                youtube.Videos.Streams.DownloadAsync(audioStream, tempAudio, aProg).AsTask()
            );
            Logs.Text += $"[Info] Downloaded to: {tempVideo} & {tempAudio}\n";

            // 8) Ensure FFmpeg is available
            //Logs.Text += "[Info] Ensuring FFmpeg is available…\n";
            var exeDir   = AppContext.BaseDirectory;
            var ffmpegDir = Path.Combine(exeDir, "ffmpeg‑bin");

            // clear it out so Xabe will re‑populate it:
            if (Directory.Exists(ffmpegDir))
                Directory.Delete(ffmpegDir, recursive: true);

            // now create & fetch into it
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Full);
            
            Logs.Text += $"[Info] Rendering...\n";

            // 9) Mux with FFmpeg
            var conversion = await FFmpeg.Conversions.FromSnippet.AddAudio(tempVideo, tempAudio, muxedFile);
            conversion.OnProgress += (_, prog) => Dispatcher.UIThread.Post(() => ProgressBar.Value = prog.Percent);
            await conversion.Start();
            Dispatcher.UIThread.Post(() => ProgressBar.Value = 100);

            Logs.Text += $"[Info] Mux complete: {muxedFile}\n";

            // 10) Delete temp files
            File.Delete(tempVideo);
            File.Delete(tempAudio);
            Logs.Text += "[Info] Cleanup Complete.\n";
        }
        catch (Exception ex)
        {
            await ShowDialog("Error:\n" + ex.Message);
            Logs.Text += $"[Error] {ex.Message}\n";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            UrlBox.IsEnabled = true;
        }
    }

    /// <summary>
    /// Shows a simple one‐button dialog with the given message.
    /// </summary>
    private async Task ShowDialog(string message, int width =300, int height= 150)
    {
        var dialog = new Window
        {
            Title = "Info",
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(10),
            Spacing = 10
        };

        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        ok.Click += (_, _) => dialog.Close();
        panel.Children.Add(ok);

        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }
}
    