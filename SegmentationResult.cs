namespace RealmStudioImageAnalysisLib
{
    public sealed class SegmentationResult
    {
        public float[] Masks { get; init; } = [];
        public int[] MaskDimensions { get; init; } = [];

        public float[] IoU { get; init; } = [];
        public float[] LowResMasks { get; init; } = [];
        public int[] LowResDimensions { get; init; } = [];
    }
}
