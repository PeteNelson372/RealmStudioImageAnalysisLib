using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class DilateEdgeScaffoldStage : IPerimeterStage
    {
        public string Id => "DilateEdgeScaffold";
        private const int LandformEdgeDilateSize = 3;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat combinedEdges = data.Get<Mat>("CombinedEdges");

            int width = combinedEdges.Width;
            int height = combinedEdges.Height;

            int dilationSize = LandformEdgeDilateSize;

            if (dilationSize < 1)
                dilationSize = 1;

            if ((dilationSize & 1) == 0)
                dilationSize++;

            using Mat dilationKernel =
                CvInvoke.GetStructuringElement(
                    MorphShapes.Ellipse,
                    new System.Drawing.Size(
                        dilationSize,
                        dilationSize),
                    new System.Drawing.Point(
                        -1,
                        -1));

            Mat dilatedScaffold =
                new(
                    height,
                    width,
                    DepthType.Cv8U,
                    1);

            CvInvoke.Dilate(
                combinedEdges,
                dilatedScaffold,
                dilationKernel,
                new System.Drawing.Point(
                    -1,
                    -1),
                1,
                BorderType.Constant,
                new MCvScalar(0));

            data.Set(
                "DilatedScaffold",
                dilatedScaffold);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Edge scaffold dilated."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
