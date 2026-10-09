using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineDefinition
    {
        public required string Name { get; init; }

        public PerimeterPipelineType Type { get; init; }

        public bool Debug { get; init; }

        public int Priority { get; init; }

        public List<PerimeterStageDefinition> Stages { get; } = [];
    }
}
