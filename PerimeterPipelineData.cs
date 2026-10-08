using RealmStudioShapeRenderingLib;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineData : IDisposable
    {
        private readonly Dictionary<string, object> _artifacts =
            new(StringComparer.OrdinalIgnoreCase);

        public SKBitmap OriginalBitmap { get; }

        public PerimeterPipelineData(SKBitmap originalBitmap)
        {
            ArgumentNullException.ThrowIfNull(originalBitmap);

            OriginalBitmap = originalBitmap;
        }

        public PerimeterPipelineData(SKBitmap originalBitmap, IReadOnlyList<ImportRegion> importRegions) : this(originalBitmap)
        {
            ArgumentNullException.ThrowIfNull(importRegions);

            Set("ImportRegions", importRegions);
        }

        public void Set<T>(string name, T value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(value);

            if (_artifacts.TryGetValue(name, out object? existing))
            {
                if (existing is IDisposable disposable)
                    disposable.Dispose();
            }

            _artifacts[name] = value;
        }

        public T Get<T>(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!_artifacts.TryGetValue(name, out object? value))
            {
                throw new InvalidOperationException(
                    $"Pipeline artifact '{name}' was not found.");
            }

            if (value is not T typedValue)
            {
                throw new InvalidOperationException(
                    $"Pipeline artifact '{name}' is not of type " +
                    $"{typeof(T).Name}.");
            }

            return typedValue;
        }

        public bool TryGet<T>(
            string name,
            out T? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (_artifacts.TryGetValue(name, out object? artifact) &&
                artifact is T typedArtifact)
            {
                value = typedArtifact;
                return true;
            }

            value = default;
            return false;
        }

        public bool Contains(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return _artifacts.ContainsKey(name);
        }

        public void Dispose()
        {
            foreach (object artifact in _artifacts.Values)
            {
                if (artifact is IDisposable disposable)
                    disposable.Dispose();
            }

            _artifacts.Clear();
        }
    }
}