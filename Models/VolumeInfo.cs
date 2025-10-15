namespace BookInventory.Api.Models
{
    public class VolumeInfo
    {
        public string? ISBN { get; set; }
        public string? Title { get; set; }
        public List<string>? Authors { get; set; }
        public string? PublishedDate { get; set; }
        public string? Description { get; set; }
        public List<string>? Categories { get; set; }
        public ImageLinks? ImageLinks { get; set; }
    }
}
