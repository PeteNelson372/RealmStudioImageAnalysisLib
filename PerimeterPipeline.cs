namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipeline
    {
        public string Name { get; }

        public IReadOnlyList<PerimeterPipelineStage> Stages { get; }

        public PerimeterPipeline(PerimeterPipelineDefinition definition, PerimeterStageRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(registry);

            Name = definition.Name;

            Stages = [.. definition.Stages.Select(stageDefinition =>
                {
                    var stage = registry.Get(stageDefinition.Id);

                    return new PerimeterPipelineStage(
                        stage,
                        stageDefinition);
                })];
        }
    }
}
