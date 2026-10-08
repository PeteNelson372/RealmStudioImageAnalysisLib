using Microsoft.Win32;
using RealmStudioImageAnalysisLib.StageDefinitions;
using RealmStudioX.Infrastructure;

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
        public PerimeterPipelineRunner PipelineRunner = new();

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
    }
}
