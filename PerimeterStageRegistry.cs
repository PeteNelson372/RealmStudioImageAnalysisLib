namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageRegistry
    {
        private readonly Dictionary<string, IPerimeterStage> _stages =
            new(StringComparer.OrdinalIgnoreCase);

        public void Register(IPerimeterStage stage)
        {
            ArgumentNullException.ThrowIfNull(stage);

            if (string.IsNullOrWhiteSpace(stage.Id))
            {
                throw new ArgumentException(
                    "Stage ID cannot be null or whitespace.",
                    nameof(stage));
            }

            if (!_stages.TryAdd(stage.Id, stage))
            {
                throw new InvalidOperationException(
                    $"A stage with ID '{stage.Id}' is already registered.");
            }
        }

        public IPerimeterStage Get(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            if (_stages.TryGetValue(id, out IPerimeterStage? stage))
            {
                return stage;
            }

            throw new InvalidOperationException(
                $"No stage found with ID '{id}'.");
        }
    }
}
