namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageRegistry
    {
        public void Register(IPerimeterStage stage)
        {
            ArgumentNullException.ThrowIfNull(stage);

            if (string.IsNullOrWhiteSpace(stage.Id))
            {
                throw new ArgumentException("Stage ID cannot be null or whitespace.", nameof(stage));
            }

        }

        public IPerimeterStage Get(string id)
        {
            ArgumentNullException.ThrowIfNull(id);

            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("ID cannot be null or whitespace.", nameof(id));
            }



            throw new InvalidOperationException($"No stage found with ID '{id}'.");
        }
    }
}
