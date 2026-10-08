using Emgu.CV;
using Emgu.CV.Util;
using System.Diagnostics;
using System.Drawing;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class SelectLargestMobileSamContours : IPerimeterStage
    {
        public string Id => "SelectLargestMobileSamContours";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            MobileSamContourSet contourSet = data.Get<MobileSamContourSet>("MobileSamContours");

            List<MobileSamSelectedContour?> selectedContours = [];

            for (int i = 0; i < contourSet.Contours.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                VectorOfVectorOfPoint contours = contourSet.Contours[i];

                if (contours.Size == 0)
                {
                    selectedContours.Add(null);

                    context.Progress?.Report(
                        new PerimeterAlgorithmProgress
                        {
                            Stage = Id,
                            Progress =
                                (double)(i + 1) /
                                Math.Max(
                                    1,
                                    contourSet.Contours.Count),
                            Status =
                                $"No external contours found for " +
                                $"region {i + 1}."
                        });

                    continue;
                }

                int largestIndex = 0;
                double largestArea = 0.0;

                for (int contourIndex = 0; contourIndex < contours.Size; contourIndex++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    double area =
                        CvInvoke.ContourArea(
                            contours[contourIndex]);

                    if (area > largestArea)
                    {
                        largestArea = area;
                        largestIndex = contourIndex;
                    }
                }

                if (largestArea <= 0)
                {
                    selectedContours.Add(null);

                    context.Progress?.Report(
                        new PerimeterAlgorithmProgress
                        {
                            Stage = Id,
                            Progress = (double)(i + 1) / Math.Max(1, contourSet.Contours.Count),
                            Status = $"No valid contour found for region {i + 1}."
                        });

                    continue;
                }

                VectorOfPoint contour = contours[largestIndex];

                Rectangle bounds = CvInvoke.BoundingRectangle(contour);

                Debug.WriteLine(
                    "Selected contour bounds: " +
                    $"{bounds.X}, " +
                    $"{bounds.Y}, " +
                    $"{bounds.Width} x " +
                    $"{bounds.Height}");

                Debug.WriteLine(
                    "Selected contour area: " +
                    $"{largestArea}");

                selectedContours.Add(
                    new MobileSamSelectedContour(
                        contour,
                        largestArea,
                        bounds));

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress = (double)(i + 1) / Math.Max(1, contourSet.Contours.Count),
                        Status =
                            $"Selected largest contour for " +
                            $"region {i + 1} of " +
                            $"{contourSet.Contours.Count}."
                    });
            }

            data.Set("MobileSamSelectedContours", new MobileSamSelectedContourSet(selectedContours));

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
