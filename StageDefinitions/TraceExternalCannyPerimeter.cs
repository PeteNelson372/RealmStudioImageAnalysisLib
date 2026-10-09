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
    public sealed class TraceExternalCannyPerimeter : IPerimeterStage
    {
        public string Id => "TraceExternalCannyPerimeter";

        private const double MinimumArea = 5000.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            // this stage requires exactly one input artifact.
            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, but {inputNames.Count} were provided.");
            }

            Mat cannyEdges =
                data.Get<Mat>(
                    context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an input artifact."));

            if (cannyEdges.NumberOfChannels != 1 ||
                cannyEdges.Depth != DepthType.Cv8U)
            {
                throw new InvalidOperationException(
                    "TraceExternalCannyPerimeter requires an 8-bit single-channel Canny image.");
            }

            Debug.WriteLine(
                $"TraceExternalCannyPerimeter: " +
                $"image={cannyEdges.Width}x{cannyEdges.Height}");

            // ------------------------------------------------------------
            // Find only the external contours of the Canny edge network.
            //
            // The important difference from V13 is that we are NOT
            // constructing a contour tree and looking for depth-2 regions.
            //
            // We are asking:
            //
            //     "What are the outermost boundaries represented by
            //      this edge network?"
            //
            // CannyEdges is binary: non-zero pixels are treated as
            // foreground by FindContours.
            // ------------------------------------------------------------

            using Mat contourSource =
                cannyEdges.Clone();

            using VectorOfVectorOfPoint contours =
                new();

            CvInvoke.FindContours(
                contourSource,
                contours,
                null,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            Debug.WriteLine(
                $"TraceExternalCannyPerimeter: " +
                $"external contours={contours.Size}");

            if (contours.Size == 0)
            {
                data.Set(
                    "ImportRegions",
                    new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "No external contours were found in the Canny edge network."
                };
            }

            // ------------------------------------------------------------
            // Select the largest external contour.
            //
            // For the Washington test case, the desired landform should
            // be the dominant outer contour. Internal county boundaries
            // should not be selected.
            // ------------------------------------------------------------

            int largestIndex = -1;
            double largestArea = 0.0;

            for (int i = 0; i < contours.Size; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                using VectorOfPoint contour =
                    contours[i];

                if (contour.Size < 3)
                    continue;

                double area =
                    Math.Abs(
                        CvInvoke.ContourArea(
                            contour));

                Debug.WriteLine(
                    $"TraceExternalCannyPerimeter: " +
                    $"contour {i}, " +
                    $"area={area:F0}, " +
                    $"points={contour.Size}");

                if (area > largestArea)
                {
                    largestArea = area;
                    largestIndex = i;
                }
            }

            if (largestIndex < 0 ||
                largestArea < MinimumArea)
            {
                data.Set(
                    "ImportRegions",
                    new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "No sufficiently large external Canny contour was found."
                };
            }

            using VectorOfPoint selectedContour =
                contours[largestIndex];

            Rectangle bounds =
                CvInvoke.BoundingRectangle(
                    selectedContour);

            double perimeterLength =
                CvInvoke.ArcLength(
                    selectedContour,
                    true);

            Debug.WriteLine(
                $"TraceExternalCannyPerimeter: " +
                $"selected contour={largestIndex}, " +
                $"area={largestArea:F0}, " +
                $"perimeter={perimeterLength:F1}, " +
                $"bounds={bounds}");

            // ------------------------------------------------------------
            // Convert the contour to an SKPath.
            // ------------------------------------------------------------

            SKPath geometry =
                CreatePath(
                    selectedContour);

            if (geometry.IsEmpty ||
                geometry.Bounds.Width <= 0 ||
                geometry.Bounds.Height <= 0)
            {
                geometry.Dispose();

                data.Set(
                    "ImportRegions",
                    new List<ImportRegion>());

                return new PerimeterStageResult
                {
                    Succeeded = false,
                    FailureReason =
                        "The selected Canny contour produced invalid geometry."
                };
            }

            // ------------------------------------------------------------
            // Create the ImportRegion.
            //
            // Confidence is deliberately left null. This pipeline is not
            // using the V13 contour-tree confidence calculation.
            // The pipeline evaluator should judge the resulting candidate.
            // ------------------------------------------------------------

            ImportRegion region =
                new(geometry)
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

            List<ImportRegion> importRegions = [region];

            // this stage produces two output artifacts: the ImportRegions list and a diagnostic image of the traced contour.
            // because it produces two outputs, leave the hardcoded output names,
            // as the current design only allows one output artifact name to be specified in the pipeline definition.

            data.Set("ImportRegions", importRegions);

            // ------------------------------------------------------------
            // Also create a diagnostic image containing the selected
            // contour. This is useful while developing this pipeline.
            // ------------------------------------------------------------

            using Mat tracedPerimeter =
                new(
                    cannyEdges.Rows,
                    cannyEdges.Cols,
                    DepthType.Cv8U,
                    1);

            tracedPerimeter.SetTo(
                new MCvScalar(0));

            CvInvoke.DrawContours(
                tracedPerimeter,
                contours,
                largestIndex,
                new MCvScalar(255),
                1,
                LineType.EightConnected);

            data.Set(
                "CannyExternalPerimeter",
                tracedPerimeter.Clone());

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        "Traced external Canny perimeter."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static SKPath CreatePath(
            VectorOfPoint contour)
        {
            Point[] points =
                contour.ToArray();

            using SKPathBuilder builder =
                new();

            if (points.Length == 0)
                return builder.Detach();

            builder.MoveTo(
                points[0].X,
                points[0].Y);

            for (int i = 1; i < points.Length; i++)
            {
                builder.LineTo(
                    points[i].X,
                    points[i].Y);
            }

            builder.Close();

            return builder.Detach();
        }
    }
}
