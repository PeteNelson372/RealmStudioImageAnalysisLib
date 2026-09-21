namespace RealmStudioImageAnalysisLib
{
    public sealed class ImageAnalysisResult
    {
        public int ImageWidth { get; init; }

        public int ImageHeight { get; init; }

        public List<DetectedRegion> Regions { get; } = [];
    }
}
