
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

            List<ImportRegion> regions = data.Get<List<ImportRegion>>(inputName);

            int initialCount = regions.Count;
            int mergeCount = 0;

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
                                out ImportRegion? combined,
                                out double sharedBorder,
                                out double sharedFraction))
                        {
                            continue;
                        }

                        Debug.WriteLine(
                            $"{Id}: merging regions {first.Id} and {second.Id}; " +
                            $"shared border={sharedBorder:F1}, " +
                            $"shared fraction={sharedFraction:P1}");

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
                        $"Merged {mergeCount} pairs; " +
                        $"{initialCount} candidates reduced to " +
                        $"{mergedRegions.Count}."
                });

            Debug.WriteLine(
                $"{Id}: {initialCount} candidates, " +
                $"{mergeCount} merges, " +
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
            // The one-pixel expansion is only a broad-phase test.
            // It does not itself qualify two regions for merging.
            first.Inflate(1.0f, 1.0f);

            return
                first.Left <= second.Right &&
                first.Right >= second.Left &&
                first.Top <= second.Bottom &&
                first.Bottom >= second.Top;
        }


        private static bool TryMerge(
            ImportRegion first,
            ImportRegion second,
            out ImportRegion? combined,
            out double sharedBorder,
            out double sharedFraction)
        {
            combined = null;
            sharedBorder = 0.0;
            sharedFraction = 0.0;

            using SKPath firstPath = first.CloneGeometry();
            using SKPath secondPath = second.CloneGeometry();

            using SKPath? unionPath = firstPath.Op(secondPath, SKPathOp.Union);

            if (unionPath is null || unionPath.IsEmpty)
                return false;

            double firstPerimeter = GetPathLength(firstPath);
            double secondPerimeter = GetPathLength(secondPath);
            double unionPerimeter = GetPathLength(unionPath);

            if (firstPerimeter <= 0.0 ||
                secondPerimeter <= 0.0 ||
                unionPerimeter <= 0.0)
            {
                return false;
            }

            sharedBorder =
                (firstPerimeter + secondPerimeter - unionPerimeter) / 2.0;

            if (sharedBorder < MinimumSharedBorderLength)
                return false;

            sharedFraction =
                sharedBorder / Math.Min(firstPerimeter, secondPerimeter);

            if (sharedFraction < MinimumSharedPerimeterFraction)
                return false;

            double totalArea = first.Area + second.Area;

            double? confidence =
                WeightedConfidence(first, second, totalArea);

            double scaffoldSupport = totalArea > 0.0
                ? (first.ScaffoldPerimeterSupport * first.Area +
                   second.ScaffoldPerimeterSupport * second.Area) / totalArea
                : Math.Max(
                    first.ScaffoldPerimeterSupport,
                    second.ScaffoldPerimeterSupport);


            combined = new ImportRegion(unionPath)
            {
                Index = first.Index,
                Source = ImportRegionSource.Automatic,
                State = ImportRegionState.Proposed,
                Confidence = confidence,
                Area = totalArea,
                PerimeterLength = unionPerimeter,
                ScaffoldPerimeterSupport = scaffoldSupport
            };

            return true;
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
                return (first.Confidence.Value + second.Confidence.Value) / 2.0;

            return
                (first.Confidence.Value * first.Area +
                 second.Confidence.Value * second.Area) / totalArea;
        }
    }
}

