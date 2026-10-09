namespace RealmStudioImageAnalysisLib
{
    public sealed class NamedStageArtifact
    {
        public string Name { get; }

        public object? Artifact { get; }

        public NamedStageArtifact(string name, object? artifact)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Name = name;
            Artifact = artifact;
        }
    }
}
