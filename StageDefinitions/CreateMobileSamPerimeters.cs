using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateMobileSamPerimeters : IPerimeterStage
    {
        public string Id => "CreateMobileSamPerimeters";

        public PerimeterStageResult Process(PerimeterPipelineData data, PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            MobileSamSelectedContourSet selectedContourSet =
                data.Get<MobileSamSelectedContourSet>("MobileSamSelectedContours");

            List<SKPath> perimeters = [];

            foreach (MobileSamSelectedContour? selectedContour
                     in selectedContourSet.Contours)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (selectedContour == null)
                    continue;

                SKPath perimeter = CreateSKPathFromContour(selectedContour.Contour);

                if (perimeter != null)
                {
                    perimeters.Add(perimeter);
                }
            }

            data.Set("RefinedPerimeters", perimeters);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Created {perimeters.Count} MobileSAM perimeters."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static SKPath CreateSKPathFromContour(VectorOfPoint contour)
        {
            System.Drawing.Point[] points = contour.ToArray();

            SKPath path = Utilities.BuildClosedPath(points);

            return path;
        }
    }
}
