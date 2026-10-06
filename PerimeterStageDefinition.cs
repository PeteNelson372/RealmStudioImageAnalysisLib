namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageDefinition
    {
        public string Id { get; init; } = "";

        public Dictionary<string, string> Parameters { get; init; } = [];

        public bool SaveOutput { get; init; }

        public string? OutputName { get; init; }
    }
}
