using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineRunner
    {
        public PerimeterPipelineResult Run(
            PerimeterPipeline pipeline,
            SKBitmap bitmap,
            PerimeterAlgorithmContext context)
        {
            // Create pipeline data
            // Run each stage
            // Handle failures
            // Produce result

            return new PerimeterPipelineResult
            {
                Succeeded = true,
                Candidates = []
            };
        }
    }
}
