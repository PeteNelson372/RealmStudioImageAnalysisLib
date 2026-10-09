using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipeline
    {
        public string Name { get; }

        public PerimeterPipelineType Type { get; }

        public bool Debug { get; }

        public int Priority { get; init; }

        public IReadOnlyList<PerimeterPipelineStage> Stages { get; }

        public PerimeterPipeline(PerimeterPipelineDefinition definition, PerimeterStageRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(registry);

            Name = definition.Name;

            Type = definition.Type;

            Debug = definition.Debug;

            Priority = definition.Priority;

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
