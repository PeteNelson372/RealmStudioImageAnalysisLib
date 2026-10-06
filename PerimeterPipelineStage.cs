namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineStage
    {
        public IPerimeterStage Stage { get; }

        public PerimeterStageDefinition Definition { get; }

        public PerimeterPipelineStage(
            IPerimeterStage stage,
            PerimeterStageDefinition definition)
        {
            Stage = stage;
            Definition = definition;
        }
    }
}
