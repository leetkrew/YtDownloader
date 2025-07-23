using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace LeetkrewYtDownloader;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UrlBox.Text = @"https://www.youtube.com/watch?v=0v37NNdjWKU";
    }

    private async void Download_Click(object? sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        UrlBox.IsEnabled = false;
        Logs.Text = string.Empty;

        try
        {
            // 1) Validate URL
            var url = UrlBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                Logs.Text += "[Error] Please enter a valid YouTube URL.\n";
                return;
            }

            // 2) Prepare YouTube client
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 13_0) " +
                "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Safari/605.1.15"
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
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
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
    