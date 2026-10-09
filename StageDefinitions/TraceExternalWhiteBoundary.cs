using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class TraceExternalWhiteBoundary : IPerimeterStage
    {
        public string Id => "TraceExternalWhiteBoundary";

        private const double MinimumArea = 5000.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            // This stage requires exactly one input artifact.
            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, " +
                    $"but {inputNames.Count} were provided.");
            }

            // Retrieve the declared input from the blackboard.
            Mat whiteBoundary = data.Get<Mat>(context.GetInputArtifact(inputNames[0]));

            if (whiteBoundary.NumberOfChannels != 1 ||
                whiteBoundary.Depth != DepthType.Cv8U)
            {
                throw new InvalidOperationException(
                    $"{Id} requires an 8-bit, single-channel boundary image.");
            }

            Debug.WriteLine($"{Id}: image={whiteBoundary.Width}x{whiteBoundary.Height}");

            // Find external contours in the already-extracted white boundary.
            // No grayscale conversion, blur, or Canny processing is needed.
            using Mat contourSource = whiteBoundary.Clone();
            using VectorOfVectorOfPoint contours = new();

            CvInvoke.FindContours(
                contourSource,
                contours,
                null,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            Debug.WriteLine($"{Id}: external contours={contours.Size}");

            if (contours.Size == 0)
            {
                data.Set("ImportRegions", new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "No external contours were found in the white boundary."
                };
            }

            // Select the largest external contour.
            int largestIndex = -1;
            double largestArea = 0.0;

            for (int i = 0; i < contours.Size; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                using VectorOfPoint contour = contours[i];

                if (contour.Size < 3)
                    continue;

                double area = Math.Abs(CvInvoke.ContourArea(contour));

                Debug.WriteLine(
                    $"{Id}: contour {i}, area={area:F0}, " +
                    $"points={contour.Size}");

                if (area > largestArea)
                {
                    largestArea = area;
                    largestIndex = i;
                }
            }

            if (largestIndex < 0 || largestArea < MinimumArea)
            {
                data.Set("ImportRegions", new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "No sufficiently large external white-boundary contour was found."
                };
            }

            using VectorOfPoint selectedContour = contours[largestIndex];

            Rectangle bounds = CvInvoke.BoundingRectangle(selectedContour);

            double perimeterLength =
                CvInvoke.ArcLength(selectedContour, true);

            Debug.WriteLine(
                $"{Id}: selected contour={largestIndex}, " +
                $"area={largestArea:F0}, perimeter={perimeterLength:F1}, " +
                $"bounds={bounds}");

            // Convert the selected contour into the landform path.
            SKPath geometry = CreatePath(selectedContour);

            if (geometry.IsEmpty ||
                geometry.Bounds.Width <= 0 ||
                geometry.Bounds.Height <= 0)
            {
                geometry.Dispose();

                data.Set("ImportRegions", new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "The selected white-boundary contour produced invalid geometry."
                };
            }

            ImportRegion region = new(geometry)
            {
                Index = 1,
                Source = ImportRegionSource.Automatic,
                State = ImportRegionState.ProposedLowConfidence,
                Confidence = null,
                Area = largestArea,
                PerimeterLength = perimeterLength,
                ScaffoldPerimeterSupport = 1.0
            };

            geometry.Dispose();

            // Primary output: candidate regions consumed by the extraction workflow.
            List<ImportRegion> importRegions = [region];
            data.Set("ImportRegions", importRegions);

            // Secondary output: diagnostic image of the selected contour.
            using Mat tracedBoundary = new(
                whiteBoundary.Rows,
                whiteBoundary.Cols,
                DepthType.Cv8U,
                1);

            tracedBoundary.SetTo(new MCvScalar(0));

            CvInvoke.DrawContours(
                tracedBoundary,
                contours,
                largestIndex,
                new MCvScalar(255),
                1,
                LineType.EightConnected);

            data.Set(
                "WhiteBoundaryExternalPerimeter",
                tracedBoundary.Clone());

            context.Progress?.Report(new PerimeterAlgorithmProgress
            {
                Stage = Id,
                Progress = 1.0,
                Status = "Traced external white boundary."
            });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static SKPath CreatePath(VectorOfPoint contour)
        {
            Point[] points = contour.ToArray();

            using SKPathBuilder builder = new();

            if (points.Length == 0)
                return builder.Detach();

            builder.MoveTo(points[0].X, points[0].Y);

            for (int i = 1; i < points.Length; i++)
            {
                builder.LineTo(points[i].X, points[i].Y);
            }

            builder.Close();

            return builder.Detach();
        }
    }
}