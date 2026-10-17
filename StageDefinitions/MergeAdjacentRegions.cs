using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class MergeAdjacentRegions : IPerimeterStage
    {
        public string Id => "MergeAdjacentRegions";

        // Fraction of the smaller region's perimeter that must
        // be shared with the other region.
        private const double MinimumSharedPerimeterFraction = 0.20;

        // Small tolerance for floating-point path measurements.
        private const double MinimumSharedBorderLength = 2.0;

        // Maximum bounding-box separation considered for gap closing.
        private const int MaximumGapPixels = 5;

        // Morphological closing kernel. The radius is deliberately larger
        // than the nominal gap tolerance to accommodate diagonal gaps.
        private const int ClosingKernelSize =
            MaximumGapPixels * 2 + 1;

        private const int MaskPadding =
            ClosingKernelSize + 2;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, " +
                    $"but {inputNames.Count} were provided.");
            }

            string inputName =
                context.GetInputArtifact(inputNames[0])
                ?? throw new InvalidOperationException(
                    $"{Id} requires an input artifact.");

            List<ImportRegion> regions =
                data.Get<List<ImportRegion>>(inputName);

            int initialCount = regions.Count;
            int mergeCount = 0;
            int gapClosingMergeCount = 0;

            // Work on a separate list so that the input collection
            // isn't modified while a merge is being evaluated.
            List<ImportRegion> mergedRegions = [.. regions];

            bool merged;

            do
            {
                merged = false;

                for (int i = 0; i < mergedRegions.Count && !merged; i++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    for (int j = i + 1; j < mergedRegions.Count; j++)
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();

                        ImportRegion first = mergedRegions[i];
                        ImportRegion second = mergedRegions[j];

                        if (!BoundsMayTouch(first.Bounds, second.Bounds))
                            continue;

                        if (!TryMerge(
                                first,
                                second,
                                context.CancellationToken,
                                out ImportRegion? combined,
                                out double sharedBorder,
                                out double sharedFraction,
                                out bool gapClosed))
                        {
                            continue;
                        }

                        if (gapClosed)
                        {
                            gapClosingMergeCount++;

                            Debug.WriteLine(
                                $"{Id}: merging regions {first.Id} and " +
                                $"{second.Id} by closing a gap.");
                        }
                        else
                        {
                            Debug.WriteLine(
                                $"{Id}: merging regions {first.Id} and " +
                                $"{second.Id}; " +
                                $"shared border={sharedBorder:F1}, " +
                                $"shared fraction={sharedFraction:P1}");
                        }

                        mergedRegions[i] = combined!;
                        mergedRegions.RemoveAt(j);

                        mergeCount++;
                        merged = true;

                        // Restart the search after every merge because
                        // the new geometry may now touch other regions.
                        break;
                    }
                }
            }
            while (merged);

            string outputName =
                context.OutputArtifact
                ?? throw new InvalidOperationException(
                    $"{Id} requires an output artifact.");

            data.Set(outputName, mergedRegions);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Merged {mergeCount} pairs " +
                        $"({gapClosingMergeCount} by gap closing); " +
                        $"{initialCount} candidates reduced to " +
                        $"{mergedRegions.Count}."
                });

            Debug.WriteLine(
                $"{Id}: {initialCount} candidates, " +
                $"{mergeCount} merges, " +
                $"{gapClosingMergeCount} gap-closing merges, " +
                $"{mergedRegions.Count} final candidates.");

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static bool BoundsMayTouch(
            SKRect first,
            SKRect second)
        {
            // This is a broad-phase test. It allows pairs separated by
            // a small gap to reach the actual merge tests.
            first.Inflate(
                MaximumGapPixels,
                MaximumGapPixels);

            return
                first.Left <= second.Right &&
                first.Right >= second.Left &&
                first.Top <= second.Bottom &&
                first.Bottom >= second.Top;
        }

        private static bool TryMerge(
            ImportRegion first,
            ImportRegion second,
            CancellationToken cancellationToken,
            out ImportRegion? combined,
            out double sharedBorder,
            out double sharedFraction,
            out bool gapClosed)
        {
            combined = null;
            sharedBorder = 0.0;
            sharedFraction = 0.0;
            gapClosed = false;

            using SKPath firstPath = first.CloneGeometry();
            using SKPath secondPath = second.CloneGeometry();

            // ------------------------------------------------------------
            // 1. Evaluate the ordinary geometric union.
            //
            // SKPath.Op can succeed even when the paths remain separate.
            // Therefore, success alone is NOT sufficient to accept a merge.
            // ------------------------------------------------------------

            using SKPath? unionPath =
                firstPath.Op(secondPath, SKPathOp.Union);

            bool hasSignificantSharedBorder = false;
            double unionPerimeter = 0.0;

            if (unionPath is not null && !unionPath.IsEmpty)
            {
                double firstPerimeter = GetPathLength(firstPath);
                double secondPerimeter = GetPathLength(secondPath);

                unionPerimeter = GetPathLength(unionPath);

                if (firstPerimeter > 0.0 &&
                    secondPerimeter > 0.0 &&
                    unionPerimeter > 0.0)
                {
                    sharedBorder =
                        (firstPerimeter +
                         secondPerimeter -
                         unionPerimeter) / 2.0;

                    if (sharedBorder >= MinimumSharedBorderLength)
                    {
                        sharedFraction =
                            sharedBorder /
                            Math.Min(firstPerimeter, secondPerimeter);

                        hasSignificantSharedBorder =
                            sharedFraction >= MinimumSharedPerimeterFraction;
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // 2. ALWAYS attempt morphological closing.
            //
            // Do not skip this just because SKPath.Op succeeded or the
            // shared-border thresholds passed. Closing can bridge small
            // gaps elsewhere along the merged perimeter.
            // ------------------------------------------------------------

            bool closingSucceeded =
                TryMergeAcrossSmallGap(
                    firstPath,
                    secondPath,
                    cancellationToken,
                    out SKPath? gapClosedPath,
                    out double closedArea,
                    out double closedPerimeter);

            // ------------------------------------------------------------
            // 3. Prefer the gap-closed geometry when it produces exactly
            // one external component.
            // ------------------------------------------------------------

            using (gapClosedPath)
            {
                if (closingSucceeded && gapClosedPath is not null)
                {
                    double totalArea = first.Area + second.Area;

                    double? confidence =
                        WeightedConfidence(
                            first,
                            second,
                            totalArea);

                    double scaffoldSupport =
                        CalculateScaffoldSupport(
                            first,
                            second,
                            totalArea);

                    combined = new ImportRegion(gapClosedPath)
                    {
                        Index = first.Index,
                        Source = ImportRegionSource.Automatic,
                        State = ImportRegionState.Proposed,
                        Confidence = confidence,
                        Area = closedArea,
                        PerimeterLength = closedPerimeter,
                        ScaffoldPerimeterSupport = scaffoldSupport
                    };

                    // This flag identifies merges that would not have
                    // qualified under the shared-border thresholds alone.
                    gapClosed = !hasSignificantSharedBorder;

                    return true;
                }
            }

            // ------------------------------------------------------------
            // 4. If closing could not produce one component, fall back
            // to the geometric union only when BOTH shared-border
            // thresholds are satisfied.
            // ------------------------------------------------------------

            if (!hasSignificantSharedBorder ||
                unionPath is null ||
                unionPath.IsEmpty)
            {
                return false;
            }

            double combinedArea = first.Area + second.Area;

            double? combinedConfidence =
                WeightedConfidence(
                    first,
                    second,
                    combinedArea);

            double combinedScaffoldSupport =
                CalculateScaffoldSupport(
                    first,
                    second,
                    combinedArea);

            combined = new ImportRegion(unionPath)
            {
                Index = first.Index,
                Source = ImportRegionSource.Automatic,
                State = ImportRegionState.Proposed,
                Confidence = combinedConfidence,
                Area = combinedArea,
                PerimeterLength = unionPerimeter,
                ScaffoldPerimeterSupport = combinedScaffoldSupport
            };

            return true;
        }

        private static bool TryMergeAcrossSmallGap(
            SKPath firstPath,
            SKPath secondPath,
            CancellationToken cancellationToken,
            out SKPath? mergedPath,
            out double area,
            out double perimeterLength)
        {
            mergedPath = null;
            area = 0.0;
            perimeterLength = 0.0;

            SKRect firstBounds = firstPath.Bounds;
            SKRect secondBounds = secondPath.Bounds;

            if (firstPath.IsEmpty || secondPath.IsEmpty)
                return false;

            // Calculate the combined bounds without relying on a
            // particular SKRect union overload.
            float minX = Math.Min(
                firstBounds.Left,
                secondBounds.Left);

            float minY = Math.Min(
                firstBounds.Top,
                secondBounds.Top);

            float maxX = Math.Max(
                firstBounds.Right,
                secondBounds.Right);

            float maxY = Math.Max(
                firstBounds.Bottom,
                secondBounds.Bottom);

            int left = (int)Math.Floor(minX);
            int top = (int)Math.Floor(minY);
            int right = (int)Math.Ceiling(maxX);
            int bottom = (int)Math.Ceiling(maxY);

            int width = checked(
                right - left + MaskPadding * 2);

            int height = checked(
                bottom - top + MaskPadding * 2);

            if (width <= 0 || height <= 0)
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Rasterize both paths into one local binary mask.
            // ------------------------------------------------------------

            using SKBitmap bitmap = new(
                new SKImageInfo(
                    width,
                    height,
                    SKColorType.Gray8,
                    SKAlphaType.Opaque));

            using (SKCanvas canvas = new(bitmap))
            using (SKPaint paint = new())
            {
                canvas.Clear(SKColors.Black);

                canvas.Translate(
                    MaskPadding - left,
                    MaskPadding - top);

                paint.Color = SKColors.White;
                paint.Style = SKPaintStyle.Fill;
                paint.IsAntialias = false;

                canvas.DrawPath(firstPath, paint);
                canvas.DrawPath(secondPath, paint);
                canvas.Flush();
            }

            cancellationToken.ThrowIfCancellationRequested();

            IntPtr pixels = bitmap.GetPixels();

            if (pixels == IntPtr.Zero)
                return false;

            // The bitmap remains alive while this Mat wraps its memory.
            using Mat mask = new(
                height,
                width,
                DepthType.Cv8U,
                1,
                pixels,
                bitmap.RowBytes);

            // ------------------------------------------------------------
            // Morphological closing: dilation followed by erosion.
            // This can bridge small gaps while approximately restoring
            // the original outside dimensions.
            // ------------------------------------------------------------

            using Mat kernel = CvInvoke.GetStructuringElement(
                MorphShapes.Ellipse,
                new System.Drawing.Size(
                    ClosingKernelSize,
                    ClosingKernelSize),
                new System.Drawing.Point(-1, -1));

            using Mat closedMask = new();

            CvInvoke.MorphologyEx(
                mask,
                closedMask,
                MorphOp.Close,
                kernel,
                new System.Drawing.Point(-1, -1),
                1,
                BorderType.Constant,
                new MCvScalar(0));

            cancellationToken.ThrowIfCancellationRequested();

            // FindContours can modify its source, so use a clone.
            using Mat contourSource = closedMask.Clone();

            using VectorOfVectorOfPoint contours = new();

            CvInvoke.FindContours(
                contourSource,
                contours,
                null,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            // The pair is only mergeable if gap closing results in
            // exactly one exterior component.
            if (contours.Size != 1)
            {
                Debug.WriteLine(
                    $"{nameof(MergeAdjacentRegions)}: gap closing " +
                    $"left {contours.Size} external components.");

                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            using VectorOfPoint contour = contours[0];

            if (contour.Size < 3)
                return false;

            System.Drawing.Point[] points = contour.ToArray();

            // ------------------------------------------------------------
            // Convert the resulting exterior contour back to world
            // coordinates. This produces one continuous closed perimeter
            // and excludes interior-hole contours.
            // ------------------------------------------------------------

            using SKPathBuilder builder = new();

            float originX = left - MaskPadding;
            float originY = top - MaskPadding;

            builder.MoveTo(
                originX + points[0].X,
                originY + points[0].Y);

            for (int i = 1; i < points.Length; i++)
            {
                builder.LineTo(
                    originX + points[i].X,
                    originY + points[i].Y);
            }

            builder.Close();

            SKPath path = builder.Detach();

            if (path.IsEmpty)
            {
                path.Dispose();
                return false;
            }

            area = Math.Abs(
                CvInvoke.ContourArea(contour));

            perimeterLength =
                CvInvoke.ArcLength(contour, true);

            if (area <= 0.0 || perimeterLength <= 0.0)
            {
                path.Dispose();
                return false;
            }

            mergedPath = path;

            return true;
        }

        private static double CalculateScaffoldSupport(
            ImportRegion first,
            ImportRegion second,
            double totalArea)
        {
            return totalArea > 0.0
                ? (first.ScaffoldPerimeterSupport * first.Area +
                   second.ScaffoldPerimeterSupport * second.Area) /
                  totalArea
                : Math.Max(
                    first.ScaffoldPerimeterSupport,
                    second.ScaffoldPerimeterSupport);
        }

        private static double GetPathLength(SKPath path)
        {
            using SKPathMeasure measure = new(path, true);

            double totalLength = 0.0;

            do
            {
                totalLength += measure.Length;
            }
            while (measure.NextContour());

            return totalLength;
        }

        private static double? WeightedConfidence(
            ImportRegion first,
            ImportRegion second,
            double totalArea)
        {
            if (!first.Confidence.HasValue ||
                !second.Confidence.HasValue)
            {
                return null;
            }

            if (totalArea <= 0.0)
            {
                return (
                    first.Confidence.Value +
                    second.Confidence.Value) / 2.0;
            }

            return
                (first.Confidence.Value * first.Area +
                 second.Confidence.Value * second.Area) / totalArea;
        }
    }
}