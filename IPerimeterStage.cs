namespace RealmStudioImageAnalysisLib
{
    public interface IPerimeterStage
    {
        string Id { get; }

        PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context);
    }
}
