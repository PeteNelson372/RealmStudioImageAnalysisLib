namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageDefinition
    {
        public string Id { get; init; } = string.Empty;

        public List<string> InputArtifacts { get; } = [];

        public string? OutputArtifact { get; init; }

        public bool SaveOutput { get; init; }

        public string? OutputName { get; init; }

        public Dictionary<string, string> Parameters { get; } = [];
    }
}
