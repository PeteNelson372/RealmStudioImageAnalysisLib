namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineDefinition
    {
        public string Name { get; init; } = "";

        public List<PerimeterStageDefinition> Stages { get; init; } = [];
    }
}
