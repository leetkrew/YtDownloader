namespace LeetkrewYtDownloader.Models;

// simple POCO for serialization
public record class WindowSizeRecord
{
    public double Width  { get; init; }
    public double Height { get; init; }
}