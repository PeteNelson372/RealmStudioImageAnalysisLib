using RealmStudioImageAnalysisLib.StageDefinitions;
using RealmStudioShapeRenderingLib;
using RealmStudioX.Infrastructure;
using SkiaSharp;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib
{
    public class LandformPerimeterExtractionService
    {
        private readonly static string rootRealmStudioXDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RealmStudioX");

        private static string assetsDirectory = Path.Combine(rootRealmStudioXDirectory, "Assets");

        private static string imageAnalysisDirectory = Path.Combine(assetsDirectory, "ImageAnalysis");

        private static string imageAnalysisDebugDirectory = Path.Combine(imageAnalysisDirectory, "Debug");

        public  static string ImageAnalysisDebugDirectory => imageAnalysisDebugDirectory;

        public PerimeterPipelineManager PipelineManager = new();
        public PerimeterStageRegistry StageRegistry = new();
        public PerimeterPipelineLoader PipelineLoader = new();

        public LandformPerimeterExtractionService()
        {
            assetsDirectory = AssetManager.RootAssetDirectory;

            imageAnalysisDirectory = Path.Combine(assetsDirectory, "ImageAnalysis");

            imageAnalysisDebugDirectory = Path.Combine(imageAnalysisDirectory, "Debug");

            // create and register pipeline stages
            StageRegistry.Register(new ConvertToBgraStage());
            StageRegistry.Register(new RemoveExternalMaterialStage());
            StageRegistry.Register(new AddArtificialBorderStage());
            StageRegistry.Register(new ConvertToMatStage());
            StageRegistry.Register(new CreateNearWhiteMaskStage());
            StageRegistry.Register(new KeepExteriorConnectedWhiteStage());
            StageRegistry.Register(new ExtractWhiteBoundaryStage());
            StageRegistry.Register(new ConvertToGrayscaleStage());
            StageRegistry.Register(new GaussianBlurStage());
            StageRegistry.Register(new CannyEdgesStage());
            StageRegistry.Register(new CombineEdgeSourcesStage());
            StageRegistry.Register(new DilateEdgeScaffoldStage());
            StageRegistry.Register(new FindEndpointsStage());
            StageRegistry.Register(new FindProposedJoinsStage());
            StageRegistry.Register(new ValidateJoinsStage());
            StageRegistry.Register(new AddAcceptedJoinsStage());
            StageRegistry.Register(new ExtractCandidateRegionsStage());
            StageRegistry.Register(new FilterAcceptedRegions());
            StageRegistry.Register(new EncodeMobileSam());
            StageRegistry.Register(new RunMobileSamSegmentation());
            StageRegistry.Register(new CreateMobileSamMasks());
            StageRegistry.Register(new FindMobileSamContours());
            StageRegistry.Register(new SelectLargestMobileSamContours());
            StageRegistry.Register(new CreateMobileSamPerimeters());
            StageRegistry.Register(new TraceExternalCannyPerimeter());
            StageRegistry.Register(new TraceExternalWhiteBoundary());

            // load image analysis pipelines
            var files = Directory.EnumerateFiles(imageAnalysisDirectory, "*.xml", SearchOption.AllDirectories).ToList();

            if (files.Count > 0)
            {
                foreach (var file in files)
                {
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension == ".xml")
                    {
                        var definition = PipelineLoader.LoadDefinition(file);
                        var pipeline = new PerimeterPipeline(definition, StageRegistry);
                        PipelineManager.Add(pipeline);
                    }
                }
            }
        }

        public PerimeterExtractionResult RunExtraction(SKBitmap bitmap, PerimeterAlgorithmContext context)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(context);

            List<PerimeterPipeline> pipelines =
                [.. PipelineManager.Pipelines
                    .Where(p => p.Type == PerimeterPipelineType.Extraction).OrderByDescending(p => p.Priority)];

            List<PerimeterPipelineAttempt> attempts = [];

            foreach (PerimeterPipeline pipeline in pipelines)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                Debug.WriteLine(
                    $"==================================================");
                Debug.WriteLine(
                    $"Running extraction pipeline: {pipeline.Name}");
                Debug.WriteLine(
                    $"Priority: {pipeline.Priority}");

                PerimeterPipelineResult result =
                    PerimeterPipelineRunner.Run(
                        pipeline,
                        bitmap,
                        context);

                PerimeterEvaluationResult evaluation =
                    PerimeterResultEvaluator.Evaluate(
                        result,
                        bitmap.Width,
                        bitmap.Height);

                attempts.Add(
                    new PerimeterPipelineAttempt
                    {
                        PipelineName = pipeline.Name,
                        Priority = pipeline.Priority,
                        Result = result,
                        Evaluation = evaluation
                    });

                Debug.WriteLine(
                    $"Pipeline '{pipeline.Name}' " +
                    $"evaluation: " +
                    $"{evaluation.Score:F1}");

                Debug.WriteLine(
                    $"Accepted: {evaluation.Accepted}");

                Debug.WriteLine(
                    $"Reason: {evaluation.Reason}");

                if (!evaluation.Accepted)
                    continue;

                return new PerimeterExtractionResult
                {
                    Succeeded = true,
                    SelectedPipeline = pipeline.Name,
                    ImportRegions = result.ImportRegions,
                    Attempts = attempts
                };
            }

            return new PerimeterExtractionResult
            {
                Succeeded = false,
                Attempts = attempts,
                FailureReason =
                    "No extraction pipeline produced an acceptable result."
            };
        }
    }
}
