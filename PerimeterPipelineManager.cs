namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineManager
    {
        private readonly Dictionary<string, PerimeterPipeline> _pipelines =  new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<PerimeterPipeline> Pipelines => _pipelines.Values;

        public void Add(PerimeterPipeline pipeline)
        {
            ArgumentNullException.ThrowIfNull(pipeline);

            if (!_pipelines.TryAdd(pipeline.Name, pipeline))
            {
                throw new InvalidOperationException($"A perimeter pipeline named '{pipeline.Name}' is already registered.");
            }
        }

        public PerimeterPipeline Get(string name)
        {
            if (_pipelines.TryGetValue(name, out var pipeline))
                return pipeline;

            throw new InvalidOperationException($"No perimeter pipeline named '{name}' is registered.");
        }

        public bool TryGet(string name, out PerimeterPipeline? pipeline)
        {
            return _pipelines.TryGetValue(name, out pipeline);
        }
    }
}
