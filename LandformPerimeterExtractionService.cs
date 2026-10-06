namespace RealmStudioImageAnalysisLib
{
    public class LandformPerimeterExtractionService
    {
        public PerimeterPipelineManager PipelineManager = new();
        public PerimeterStageRegistry StageRegistry = new();
        public PerimeterPipelineLoader PipelineLoader = new();

        public LandformPerimeterExtractionService()
        {
            // TODO: create and register pipeline stages
            // Register stages
            //registry.Register(new PerimeterStageA());
            //registry.Register(new PerimeterStageB());
            //registry.Register(new PerimeterStageC());
        }
    }
}
