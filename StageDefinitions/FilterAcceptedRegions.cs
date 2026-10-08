using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public class FilterAcceptedRegions : IPerimeterStage
    {
        public string Id => "FilterAcceptedRegions";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ImportRegion> importRegions =
                data.Get<IReadOnlyList<ImportRegion>>(
                    "ImportRegions");

            IReadOnlyList<ImportRegion> acceptedRegions =
                importRegions
                    .Where(r =>
                        r.State == ImportRegionState.Accepted)
                    .ToList();

            data.Set(
                "AcceptedImportRegions",
                acceptedRegions);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Selected {acceptedRegions.Count} accepted import regions."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
