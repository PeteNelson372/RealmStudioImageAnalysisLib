namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageContext
    {
        public CancellationToken CancellationToken { get; }

        public IProgress<PerimeterAlgorithmProgress>? Progress { get; }

        public IReadOnlyList<string> InputArtifacts { get; }

        public string? OutputArtifact { get; }

        public Dictionary<string, string> Parameters { get; }

        public bool SaveOutput { get; }

        public string? OutputName { get; }

        public string StageId { get; }

        public PerimeterStageContext(
            string stageId,
            IProgress<PerimeterAlgorithmProgress>? progress,
            IReadOnlyList<string> inputArtifacts,
            string? outputArtifact,
            Dictionary<string, string> parameters,
            bool saveOutput,
            string? outputName,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
            ArgumentNullException.ThrowIfNull(inputArtifacts);
            ArgumentNullException.ThrowIfNull(parameters);

            StageId = stageId;
            CancellationToken = cancellationToken;
            Progress = progress;
            InputArtifacts = inputArtifacts;
            OutputArtifact = outputArtifact;
            Parameters = parameters;
            SaveOutput = saveOutput;
            OutputName = outputName;
        }

        public string GetParameter(
            string name,
            string defaultValue)
        {
            if (Parameters.TryGetValue(
                    name,
                    out string? value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return defaultValue;
        }

        public string GetInputArtifact(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            string? artifactName = InputArtifacts.FirstOrDefault(
                x => string.Equals(
                    x,
                    name,
                    StringComparison.OrdinalIgnoreCase));

            if (artifactName == null)
            {
                throw new InvalidOperationException(
                    $"Stage '{StageId}' does not declare input artifact '{name}'.");
            }

            return artifactName;
        }
    }
}
