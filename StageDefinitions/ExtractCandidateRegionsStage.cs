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
    public sealed class ExtractCandidateRegionsStage : IPerimeterStage
    {
        public string Id => "ExtractCandidateRegions";

        private const int ImportRegionMinimumArea = 5000;
        private const int LandformArtificialBorderSize = 2;
        private const int ImportRegionScaffoldSupportRadius = 3;

        private const double ImportRegionAreaWeight = 0.25;

        private const double ImportRegionSizeWeight = 0.20;

        private const double ImportRegionShapeWeight = 0.15;

        private const double ImportRegionScaffoldWeight = 0.40;

        private const double ImportRegionHighConfidenceThreshold = 75.0;

        private const double ImportRegionDetailWeight = 0.10;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat repairedScaffold =
                data.Get<Mat>("RepairedScaffold");

            IReadOnlyList<ImportRegion> importRegions =
                ExtractImportRegionCandidates(
                    repairedScaffold,
                    repairedScaffold.Width,
                    repairedScaffold.Height);

            data.Set("ImportRegions", importRegions);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Extracted {importRegions.Count} candidate regions."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static List<ImportRegion> ExtractImportRegionCandidates(
            Mat repairedScaffold,
            int imageWidth,
            int imageHeight)
        {
            ArgumentNullException.ThrowIfNull(repairedScaffold);

            // ------------------------------------------------------------
            // The repaired scaffold is white on black.
            //
            // We invert it so that the enclosed regions become white.
            //
            // We deliberately DO NOT use RetrType.External here.
            //
            // A landform is an enclosed region in the contour tree, and
            // may contain many other enclosed regions representing:
            //
            //   - snow fields
            //   - mountains
            //   - lakes
            //   - rivers
            //   - decorative details
            //
            // Those internal regions must NOT prevent the enclosing
            // landform from being considered a candidate.
            // ------------------------------------------------------------

            using Mat inverted = new();

            CvInvoke.BitwiseNot(
                repairedScaffold,
                inverted);

            using VectorOfVectorOfPoint contours = new();

            int[,] hierarchy =
                CvInvoke.FindContourTree(
                    inverted,
                    contours,
                    ChainApproxMethod.ChainApproxSimple);

            Debug.WriteLine(
                $"V13 contour tree contains {contours.Size} contours.");

            List<RawImportRegionCandidate> rawCandidates = [];

            // ------------------------------------------------------------
            // Examine EVERY contour in the tree.
            // ------------------------------------------------------------

            for (int i = 0; i < contours.Size; i++)
            {
                using VectorOfPoint contour = contours[i];

                int depth =
                    GetContourDepth(
                        hierarchy,
                        i);

                // We are interested in enclosed regions.
                //
                // Depth 0 = exterior
                // Depth 1 = boundary/hole
                // Depth 2 = enclosed region
                // Depth 3 = detail inside that region
                // etc.
                //
                // Therefore use even depths >= 2.
                if (depth < 2 ||
                    (depth & 1) != 0)
                {
                    continue;
                }

                if (contour.Size < 3)
                    continue;

                double area =
                    Math.Abs(CvInvoke.ContourArea(contour));

                if (area < ImportRegionMinimumArea)
                    continue;

                System.Drawing.Rectangle contourBounds =
                    CvInvoke.BoundingRectangle(
                        contour);

                // Reject contours which touch OUR artificial border.
                //
                // This is deliberately different from rejecting contours
                // that touch the ORIGINAL image edge.
                //
                // A real landform is allowed to touch the original image
                // edge.
                if (TouchesArtificialBorder(
                        contourBounds,
                        repairedScaffold))
                {
                    continue;
                }

                // --------------------------------------------------------
                // Create geometry in ORIGINAL image coordinates.
                // --------------------------------------------------------

                SKPath geometry =
                    CreateImportRegionPath(
                        contour,
                        LandformArtificialBorderSize);

                SKRect bounds =
                    geometry.Bounds;

                if (bounds.Width <= 0 ||
                    bounds.Height <= 0)
                {
                    geometry.Dispose();
                    continue;
                }

                // --------------------------------------------------------
                // Measure how much of the contour is supported by the
                // repaired scaffold.
                // --------------------------------------------------------

                double scaffoldSupport =
                    CalculateScaffoldPerimeterSupport(
                        repairedScaffold,
                        contour);

                // --------------------------------------------------------
                // Count internal contour descendants.
                //
                // This is NOT a rejection criterion.
                //
                // In fact, a large enclosing landform having lots of
                // internal contours is useful evidence that it is a
                // complex landform containing snow, mountains, lakes, etc.
                // --------------------------------------------------------

                int childCount =
                    GetContourChildCount(
                        hierarchy,
                        i);

                int descendantCount =
                    GetContourDescendantCount(
                        hierarchy,
                        i);

                // --------------------------------------------------------
                // Scores are used for confidence only.
                // They do NOT determine whether the candidate exists.
                // --------------------------------------------------------

                double areaScore =
                    CalculateImportRegionAreaScore(
                        area,
                        imageWidth,
                        imageHeight);

                double sizeScore =
                    CalculateImportRegionSizeScore(
                        bounds,
                        imageWidth,
                        imageHeight);

                double perimeter =
                    CvInvoke.ArcLength(
                        contour,
                        true);

                double shapeScore =
                    CalculateImportRegionShapeScore(
                        area,
                        perimeter);

                double detailScore =
                    CalculateImportRegionDetailScore(
                        descendantCount);

                double confidence =
                    CalculateImportRegionConfidence(
                        areaScore,
                        sizeScore,
                        shapeScore,
                        scaffoldSupport,
                        detailScore);

                SKPoint centroid =
                    CalculatePathCentroid(
                        geometry);

                RawImportRegionCandidate candidate =
                    new()
                    {
                        ContourIndex = i,
                        Depth = depth,

                        Geometry = geometry,
                        Bounds = bounds,
                        Centroid = centroid,

                        Area = area,
                        PerimeterLength = perimeter,

                        ScaffoldPerimeterSupport =
                            scaffoldSupport,

                        ChildCount =
                            childCount,

                        DescendantCount =
                            descendantCount,

                        AreaScore =
                            areaScore,

                        SizeScore =
                            sizeScore,

                        ShapeScore =
                            shapeScore,

                        DetailScore =
                            detailScore,

                        Confidence =
                            confidence
                    };

                rawCandidates.Add(candidate);

                Debug.WriteLine(
                    $"V13 candidate contour {i}: " +
                    $"depth={depth}, " +
                    $"area={area:F0}, " +
                    $"bounds={bounds}, " +
                    $"children={childCount}, " +
                    $"descendants={descendantCount}, " +
                    $"scaffold={scaffoldSupport:P1}, " +
                    $"confidence={confidence:F1}");
            }

            Debug.WriteLine(
                $"V13 raw import region candidates: " +
                $"{rawCandidates.Count}");

            // ------------------------------------------------------------
            // Sort largest first.
            //
            // This is primarily useful for diagnostics at this stage.
            // We are NOT declaring the largest region to be "the landform".
            // ------------------------------------------------------------

            rawCandidates.Sort((a, b) => b.Area.CompareTo(a.Area));

            List<ImportRegion> candidates = [];

            int id = 1;

            foreach (RawImportRegionCandidate raw in rawCandidates)
            {
                ImportRegionState state = raw.Confidence >= 75.0 ? ImportRegionState.ProposedHighConfidence : ImportRegionState.ProposedLowConfidence;

                ImportRegion region = new(raw.Geometry)
                {
                    Index = id++,
                    Source = ImportRegionSource.Automatic,
                    State = state,
                    Confidence = raw.Confidence,
                    Area = raw.Area,
                    PerimeterLength = raw.PerimeterLength,
                    ScaffoldPerimeterSupport = raw.ScaffoldPerimeterSupport
                };

                candidates.Add(region);

                Debug.WriteLine(
                    $"V13 import region {region.Id}: " +
                    $"area={region.Area:F0}, " +
                    $"bounds={region.Bounds}, " +
                    $"depth={raw.Depth}, " +
                    $"children={raw.ChildCount}, " +
                    $"descendants={raw.DescendantCount}, " +
                    $"confidence={region.Confidence:F1}");
            }

            return candidates;
        }

        private static SKPoint CalculatePathCentroid(SKPath path)
        {
            SKRect bounds =
                path.Bounds;

            return new SKPoint(
                bounds.MidX,
                bounds.MidY);
        }

        private static double CalculateImportRegionConfidence(double areaScore, double sizeScore, double shapeScore, double scaffoldSupport,
            double detailScore)
        {
            double confidence = areaScore * ImportRegionAreaWeight +
                sizeScore * ImportRegionSizeWeight +
                shapeScore * ImportRegionShapeWeight +
                scaffoldSupport * ImportRegionScaffoldWeight +
                detailScore * ImportRegionDetailWeight;

            return Math.Clamp(confidence * 100.0, 0.0, 100.0);
        }

        private static double CalculateImportRegionDetailScore(int descendantCount)
        {
            if (descendantCount <= 0)
                return 0.0;

            // Logarithmic response so that 10 details is useful,
            // 100 details is stronger, but 1000 details isn't allowed
            // to dominate everything else.
            double score =
                Math.Log10(
                    descendantCount + 1.0) /
                Math.Log10(101.0);

            return Math.Clamp(
                score,
                0.0,
                1.0);
        }

        private static double CalculateImportRegionShapeScore(double area, double perimeter)
        {
            if (area <= 0.0 ||
                perimeter <= 0.0)
            {
                return 0.0;
            }

            double compactness =
                (4.0 * Math.PI * area) /
                (perimeter * perimeter);

            return Math.Clamp(
                compactness,
                0.0,
                1.0);
        }

        private static double CalculateImportRegionSizeScore(SKRect bounds, int imageWidth, int imageHeight)
        {
            if (imageWidth <= 0 ||
                imageHeight <= 0)
            {
                return 0.0;
            }

            double widthFraction =
                bounds.Width /
                imageWidth;

            double heightFraction =
                bounds.Height /
                imageHeight;

            double score =
                Math.Max(
                    widthFraction,
                    heightFraction);

            return Math.Clamp(
                score,
                0.0,
                1.0);
        }

        private static double CalculateImportRegionAreaScore(double area, int imageWidth, int imageHeight)
        {
            double imageArea =
                (double)imageWidth *
                imageHeight;

            if (imageArea <= 0)
                return 0.0;

            double fraction =
                area / imageArea;

            if (fraction <= 0.001)
                return 0.0;

            if (fraction >= 0.50)
                return 1.0;

            double normalized =
                (fraction - 0.001) /
                (0.50 - 0.001);

            return Math.Clamp(
                normalized,
                0.0,
                1.0);
        }

        private static int GetContourDescendantCount(int[,] hierarchy, int contourIndex)
        {
            int count = 0;

            int child =
                hierarchy[
                    contourIndex,
                    2];

            while (child >= 0)
            {
                count++;

                count +=
                    GetContourDescendantCount(
                        hierarchy,
                        child);

                child =
                    hierarchy[
                        child,
                        0];
            }

            return count;
        }

        private static int GetContourChildCount(int[,] hierarchy, int contourIndex)
        {
            int count = 0;

            int child =
                hierarchy[
                    contourIndex,
                    2];

            while (child >= 0)
            {
                count++;

                child =
                    hierarchy[
                        child,
                        0];
            }

            return count;
        }

        private static double CalculateScaffoldPerimeterSupport(Mat repairedScaffold, VectorOfPoint contour)
        {
            Point[] contourPoints =
                contour.ToArray();

            if (contourPoints.Length < 2)
                return 0.0;

            int radius =
                ImportRegionScaffoldSupportRadius;

            int kernelSize =
                radius * 2 + 1;

            using Mat expandedScaffold =
                new();

            using Mat kernel =
                CvInvoke.GetStructuringElement(
                    MorphShapes.Ellipse,
                    new System.Drawing.Size(
                        kernelSize,
                        kernelSize),
                    new System.Drawing.Point(
                        -1,
                        -1));

            CvInvoke.Dilate(
                repairedScaffold,
                expandedScaffold,
                kernel,
                new System.Drawing.Point(
                    -1,
                    -1),
                1,
                BorderType.Constant,
                new MCvScalar(0));

            int supported = 0;
            int total = 0;

            for (int i = 0;
                 i < contourPoints.Length;
                 i++)
            {
                Point start =
                    contourPoints[i];

                Point end =
                    contourPoints[
                        (i + 1) %
                        contourPoints.Length];

                double dx =
                    end.X - start.X;

                double dy =
                    end.Y - start.Y;

                double distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                int samples =
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            distance));

                for (int s = 0;
                     s <= samples;
                     s++)
                {
                    double t =
                        (double)s /
                        samples;

                    int x =
                        (int)Math.Round(
                            start.X +
                            dx * t);

                    int y =
                        (int)Math.Round(
                            start.Y +
                            dy * t);

                    if (x < 0 ||
                        x >= expandedScaffold.Cols ||
                        y < 0 ||
                        y >= expandedScaffold.Rows)
                    {
                        continue;
                    }

                    total++;

                    // Emgu Mat.GetData() is not a scalar accessor on all
                    // versions/configurations, so use the byte pointer.
                    IntPtr row =
                        expandedScaffold.DataPointer +
                        y * expandedScaffold.Step;

                    byte value =
                        System.Runtime.InteropServices.Marshal.ReadByte(
                            row,
                            x);

                    if (value != 0)
                        supported++;
                }
            }

            if (total == 0)
                return 0.0;

            return (double)supported / total;
        }

        private static SKPath CreateImportRegionPath(VectorOfPoint contour, int artificialBorder)
        {
            Point[] points =
                contour.ToArray();

            using SKPathBuilder builder =
                new();

            if (points.Length == 0)
                return builder.Detach();

            builder.MoveTo(
                points[0].X - artificialBorder,
                points[0].Y - artificialBorder);

            for (int i = 1; i < points.Length; i++)
            {
                builder.LineTo(
                    points[i].X - artificialBorder,
                    points[i].Y - artificialBorder);
            }

            builder.Close();

            return builder.Detach();
        }

        private static bool TouchesArtificialBorder(System.Drawing.Rectangle bounds, Mat extractionScaffold)
        {
            return
                bounds.Left <= 0 ||
                bounds.Top <= 0 ||
                bounds.Right >= extractionScaffold.Cols ||
                bounds.Bottom >= extractionScaffold.Rows;
        }

        private static int GetContourDepth(int[,] hierarchy, int contourIndex)
        {
            int depth = 0;

            int parent =
                hierarchy[contourIndex, 3];

            while (parent >= 0)
            {
                depth++;

                parent =
                    hierarchy[parent, 3];
            }

            return depth;
        }

        private sealed class RawImportRegionCandidate
        {
            public int ContourIndex { get; init; }

            public int Depth { get; init; }

            public SKPath Geometry { get; init; } = new();

            public SKRect Bounds { get; init; }

            public SKPoint Centroid { get; init; }

            public double Area { get; init; }

            public double PerimeterLength { get; init; }

            public double ScaffoldPerimeterSupport { get; init; }

            public int ChildCount { get; init; }

            public int DescendantCount { get; init; }

            public double AreaScore { get; init; }

            public double SizeScore { get; init; }

            public double ShapeScore { get; init; }

            public double DetailScore { get; init; }

            public double Confidence { get; init; }
        }
    }


}
