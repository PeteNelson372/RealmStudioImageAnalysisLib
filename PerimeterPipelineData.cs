using RealmStudioShapeRenderingLib;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineData
    {
        private readonly List<NamedStageArtifact> _artifacts = [];

        public SKBitmap OriginalBitmap { get; }

        public PerimeterPipelineData(SKBitmap originalBitmap)
        {
            ArgumentNullException.ThrowIfNull(originalBitmap);

            OriginalBitmap = originalBitmap;
        }

        public PerimeterPipelineData(SKBitmap originalBitmap, IReadOnlyList<ImportRegion> importRegions)
            : this(originalBitmap)
        {
            ArgumentNullException.ThrowIfNull(importRegions);

            Set("ImportRegions", importRegions);
        }

        public void Set<T>(string name, T artifact)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            NamedStageArtifact namedArtifact = new(name, artifact);

            int index = _artifacts.FindIndex(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
            {
                _artifacts[index] = namedArtifact;
            }
            else
            {
                _artifacts.Add(namedArtifact);
            }
        }

        public T Get<T>(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            NamedStageArtifact? artifact = _artifacts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Artifact '{name}' was not found.");

            if (artifact.Artifact is not T typedArtifact)
            {
                throw new InvalidOperationException(
                    $"Artifact '{name}' is of type " +
                    $"{artifact.Artifact?.GetType().Name ?? "null"}, " +
                    $"not {typeof(T).Name}.");
            }

            return typedArtifact;
        }

        public bool Contains(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return _artifacts.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}