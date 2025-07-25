using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LeetkrewYtDownloader.Models;

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
    private double _videoDurationSec;
    
    public MainWindow()
    {
        _videoDurationSec = 0;
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

            // 8) figure out ffmpeg.exe location (e.g. bundled or Homebrew)
            var ffmpegExe = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg");
            // you can also detect /opt/homebrew/bin/ffmpeg if you prefer

            // 9) probe your video to get its duration via ffprobe
            Logs.Text += "[Info] Probing video duration…\n";
            _videoDurationSec = await GetVideoDurationAsync(tempVideo);
            Logs.Text += $"[Info] Duration: {_videoDurationSec:F1}s\n";

            // 10) run the external ffmpeg
            var mergeProgress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => ProgressBar.Value = p * 100)
            );
 
            await RunFfmpegMergeAsync(
                ffmpegExe,
                tempVideo,
                tempAudio,
                muxedFile ?? throw new InvalidOperationException(),
                mergeProgress,
                _ => Dispatcher.UIThread.Post(() => { })
            );
            
            File.Delete(tempVideo);
            File.Delete(tempAudio);
            
            Dispatcher.UIThread.Post(() =>
                Logs.Text += "[Info] Deleted temporary video/audio files.\n"
            );
            
            ProgressBar.Value = 100;
            Logs.Text += $"[Info] Remux complete: {muxedFile}\n";
        }
        catch (Exception ex)
        {
            await ShowDialog("Error:\n" + ex.Message);
            Logs.Text += $"[Error] {ex.Message}\n";
            Logs.Text += $"[Stack Trace] {ex}\n";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
            UrlBox.IsEnabled = true;
        }
    }

    private async Task<double> GetVideoDurationAsync(string videoPath)
    {
        // Build ffprobe args: JSON output with format.duration
        var psi = new ProcessStartInfo
        {
            FileName            = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffprobe"),
            Arguments           = $"-v quiet -print_format json -show_format \"{videoPath}\"",
            RedirectStandardOutput = true,
            UseShellExecute     = false,
            CreateNoWindow      = true
        };
    
        using var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("Could not start ffprobe.");
    
        string json = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
    
        using var doc = JsonDocument.Parse(json);
        // JSON: { "format": { "duration": "123.456", … } }
        var durProp = doc.RootElement
            .GetProperty("format")
            .GetProperty("duration")
            .GetString();
        if (!double.TryParse(durProp, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds))
            throw new InvalidOperationException("Failed to parse ffprobe duration.");
    
        return seconds;
    }

    
    private Task RunFfmpegMergeAsync(
        string ffmpegExe,
        string videoPath,
        string audioPath,
        string outputPath,
        IProgress<double> progress,
        Action<string> log)
    {
        var tcs = new TaskCompletionSource<bool>();

        // ffmpeg arguments: two inputs, stream‐copy codec, overwrite output
        var args = $"-i \"{videoPath}\" -i \"{audioPath}\" -c copy -y \"{outputPath}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = ffmpegExe,
            Arguments              = args,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            log($"[ffmpeg] {e.Data}\n");

            // Look for "time=HH:MM:SS.millis" in stderr
            var m = Regex.Match(e.Data, @"time=(\d+):(\d+):(\d+\.?\d*)");
            if (m.Success)
            {
                double hours   = double.Parse(m.Groups[1].Value);
                double minutes = double.Parse(m.Groups[2].Value);
                double seconds = double.Parse(m.Groups[3].Value);
                // total seconds elapsed
                double elapsed = hours * 3600 + minutes * 60 + seconds;

                // We need the total video duration to compute percent.
                // Let's assume you captured that earlier:
                double totalSec = _videoDurationSec; 
                progress.Report(elapsed / totalSec);
            }
        };

        proc.Exited += (_, _) =>
        {
            if (proc.ExitCode == 0)
                tcs.SetResult(true);
            else
                tcs.SetException(new Exception($"ffmpeg exited with code {proc.ExitCode}"));
            proc.Dispose();
        };

        proc.Start();
        proc.BeginErrorReadLine();
        return tcs.Task;
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
    