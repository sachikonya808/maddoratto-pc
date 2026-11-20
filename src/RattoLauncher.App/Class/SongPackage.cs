using Avalonia.Media.Imaging;

namespace RattoLauncher.App.Class
{
    public class SongPackage
    {
        public string ZipPath { get; set; } = string.Empty;
        public string Title { get; set; } = "Unknown Song";
        public string Composer { get; set; } = "Unknown Artist";
        public string BPM { get; set; } = "?";
        public string BaseID { get; set; } = "?";
        public Bitmap? Image { get; set; }
        public byte[]? ImageData { get; set; }
        public override string ToString() => Title;
    }
}
