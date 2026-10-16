using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineDefinition
    {
        public required string Name { get; init; }

        public PerimeterPipelineType Type { get; init; }

        public bool Debug { get; init; }

        public int Priority { get; init; }

        public string Description { get; init; } = string.Empty;

        public string BestFor { get; init; } = string.Empty;

        public string Limitations { get; init; } = string.Empty;

        public List<PerimeterStageDefinition> Stages { get; } = [];
    }
}
