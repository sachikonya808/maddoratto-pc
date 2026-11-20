namespace RattoLauncher.Core.Models
{
    public class SongMetadata
    {
        public string? Title { get; set; }
        public string? Composer { get; set; }
        public int? BPM { get; set; }
        public int? BaseID { get; set; }
        public bool CustomCharts { get; set; } = false;
    }
}
