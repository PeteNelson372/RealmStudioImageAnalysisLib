using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib;

public sealed class ImageLandformAnalyzer
{
    private const int LandformBlurSize = 7;

    private const double LandformCannyThreshold1 = 40.0;
    private const double LandformCannyThreshold2 = 100.0;


    // Keep the 5x5 dilation from V9 for this experiment.
    // We can test 4x4 later if the coastline detail still needs
    // improvement.
    private const int LandformEdgeDilateSize = 3;

    // Maximum distance between endpoints considered for repair.
    private const double LandformEndpointMaxJoinDistance = 18.0;

    // Maximum allowed angle between an endpoint's local direction
    // and the direction toward the other endpoint.
    private const double LandformEndpointMaxJoinAngleDegrees = 65.0;

    // Sample spacing along a proposed repair line.
    private const double LandformJoinSampleSpacing = 1.0;

    // A proposed join should not pass through a substantial amount
    // of existing scaffold. This prevents long-range "shortcuts"
    // through unrelated terrain boundaries.
    private const double LandformJoinMaximumScaffoldFraction = 0.35;

    // Radius around each endpoint that is allowed to contain
    // scaffold. This prevents the validation from rejecting the
    // actual endpoint neighborhood.
    private const int LandformJoinEndpointClearance = 4;

    private const int LandformArtificialBorderSize = 2;

    private const double LandformWhiteColorDistance = 25.0;

    private const int ExternalMaterialColorQuantization = 16;

    private const int ExternalMaterialMinimumBoundaryPixels = 100;

    private const int ExternalMaterialMinimumComponentArea = 5000;

    private const int ImportRegionMinimumArea = 5000;

    private const double ImportRegionContourApproximation = 2.0;

    private const int ImportRegionScaffoldClosingSize = 5;

    private const int ImportRegionScaffoldSupportRadius = 3;

    private const double ImportRegionAreaWeight = 0.25;

    private const double ImportRegionSizeWeight = 0.20;

    private const double ImportRegionShapeWeight = 0.15;

    private const double ImportRegionScaffoldWeight = 0.40;

    private const double ImportRegionHighConfidenceThreshold = 75.0;

    private const double ImportRegionDetailWeight = 0.10;


    public static LandformAnalysisResult Analyze(SKBitmap importImage)
    {
        ArgumentNullException.ThrowIfNull(importImage);

        using SKBitmap bitmap =
            importImage.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to convert image to BGRA8888.");

        Debug.WriteLine($"Landform V13 source image: " + $"{bitmap.Width} x {bitmap.Height}");

        // ============================================================
        // 1. Detect and remove substantial external material.
        //
        // This operates on the ORIGINAL image, before the artificial
        // border is added.
        //
        // The purpose is to remove ocean / surrounding material so
        // that its internal cartographic detail does not subsequently
        // become part of the grayscale edge network.
        // ============================================================

        using Mat externalMaterialMask = DetectExternalMaterialMask(bitmap);

        SaveMaskDiagnostic(
            externalMaterialMask,
            "TracingV13_00_ExternalMaterialMask.png");

        SaveExternalMaterialOverlayDiagnostic(
            bitmap,
            externalMaterialMask,
            "TracingV13_00_ExternalMaterialOverlay.png");

        using SKBitmap externalMaterialRemoved = ReplaceExternalMaterialWithWhite(bitmap, externalMaterialMask);

        SaveBitmapDiagnostic(
            externalMaterialRemoved,
            "TracingV13_00_ExternalMaterialRemoved.png");

        // ============================================================
        // 2. Add the artificial exterior border.
        //
        // IMPORTANT:
        // The border is added AFTER external material has been removed.
        // ============================================================

        using SKBitmap paddedBitmap =
            CreateArtificialBorderBitmap(
                externalMaterialRemoved,
                LandformArtificialBorderSize);

        int width =
            paddedBitmap.Width;

        int height =
            paddedBitmap.Height;

        Debug.WriteLine(
            $"Landform V13 padded image: " +
            $"{width} x {height}");

        // ============================================================
        // 3. Convert padded image to Mat.
        // ============================================================

        using Mat sourceMat = CreateMat(paddedBitmap);

        // ============================================================
        // 4. Create a near-white mask.
        //
        // This is NOT the ocean mask. It is simply every pixel
        // sufficiently close to white.
        // ============================================================

        using Mat nearWhiteMask =
            CreateNearWhiteMask(
                sourceMat,
                width,
                height,
                LandformWhiteColorDistance);

        SaveMaskDiagnostic(
            nearWhiteMask,
            "TracingV13_01_NearWhiteMask.png");

        // ============================================================
        // 5. Keep only near-white pixels connected to the exterior.
        //
        // Because the image has an artificial white border, the
        // exterior is guaranteed to be connected to the outside.
        // ============================================================

        using Mat connectedWhite =
            CreateConnectedWhiteMask(
                nearWhiteMask,
                width,
                height);

        SaveMaskDiagnostic(
            connectedWhite,
            "TracingV13_02_ConnectedWhite.png");

        // ============================================================
        // 6. Extract the boundary of that connected white region.
        //
        // This should recover coastline sections where grayscale
        // Canny sees little or nothing.
        // ============================================================

        using Mat whiteBoundary =
            CreateMaskBoundary(
                connectedWhite,
                width,
                height);

        SaveMaskDiagnostic(
            whiteBoundary,
            "TracingV13_03_WhiteBoundary.png");

        // ============================================================
        // 7. Normal grayscale processing.
        //
        // This is the V12/V13 perimeter pipeline.
        // ============================================================

        using Mat gray =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        CvInvoke.CvtColor(
            sourceMat,
            gray,
            ColorConversion.Bgra2Gray);

        using Mat blurred =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        CvInvoke.GaussianBlur(
            gray,
            blurred,
            new System.Drawing.Size(
                LandformBlurSize,
                LandformBlurSize),
            0);

        using Mat cannyEdges =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        CvInvoke.Canny(
            blurred,
            cannyEdges,
            LandformCannyThreshold1,
            LandformCannyThreshold2);

        SaveMaskDiagnostic(
            cannyEdges,
            "TracingV13_04_GrayscaleEdges.png");

        // ============================================================
        // 8. Combine the two independent edge sources.
        //
        // A pixel is an edge if either:
        //
        //   - grayscale Canny found it
        //   - connected near-white background found it
        //
        // Everything downstream remains unchanged.
        // ============================================================

        using Mat combinedEdges =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        CvInvoke.BitwiseOr(
            cannyEdges,
            whiteBoundary,
            combinedEdges);

        SaveMaskDiagnostic(
            combinedEdges,
            "TracingV13_05_CombinedEdges.png");

        // ============================================================
        // 9. Dilate the combined edge network.
        // ============================================================

        int dilationSize =
            LandformEdgeDilateSize;

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

        using Mat dilated =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        CvInvoke.Dilate(
            combinedEdges,
            dilated,
            dilationKernel,
            new System.Drawing.Point(
                -1,
                -1),
            1,
            BorderType.Constant,
            new MCvScalar(0));

        SaveMaskDiagnostic(
            dilated,
            "TracingV13_06_Dilated.png");

        // ============================================================
        // 10. Find endpoints from the combined thin edge network.
        // ============================================================

        List<LandformEndpoint> endpoints =
            FindLandformEndpoints(
                combinedEdges);

        Debug.WriteLine(
            $"V13 endpoints found: " +
            $"{endpoints.Count}");

        SaveLandformEndpointDiagnostic(
            paddedBitmap,
            endpoints,
            "TracingV13_07_Endpoints.png");

        // ============================================================
        // 11. Find proposed endpoint joins.
        // ============================================================

        List<LandformEndpointJoin> proposedJoins =
            FindProposedEndpointJoins(
                endpoints,
                width,
                height);

        Debug.WriteLine(
            $"V13 proposed joins: " +
            $"{proposedJoins.Count}");

        // ============================================================
        // 12. Validate joins against the dilated scaffold.
        // ============================================================

        List<LandformEndpointJoin> acceptedJoins = [];

        foreach (LandformEndpointJoin join in proposedJoins)
        {
            bool accepted =
                IsScaffoldAwareJoinValid(
                    dilated,
                    join);

            join.Accepted =
                accepted;

            if (accepted)
            {
                acceptedJoins.Add(join);

                Debug.WriteLine(
                    $"V13 accepted join: " +
                    $"({join.Start.X:F1}, " +
                    $"{join.Start.Y:F1}) -> " +
                    $"({join.End.X:F1}, " +
                    $"{join.End.Y:F1}), " +
                    $"distance={join.Distance:F1}, " +
                    $"angle={join.AngleDegrees:F1}");
            }
        }

        Debug.WriteLine(
            $"V13 accepted joins: " +
            $"{acceptedJoins.Count}");

        // ============================================================
        // 13. Save proposed joins.
        // ============================================================

        SaveLandformJoinDiagnostic(
            paddedBitmap,
            endpoints,
            proposedJoins,
            "TracingV13_08_ProposedJoins.png");

        // ============================================================
        // 14. Save accepted joins.
        // ============================================================

        SaveLandformJoinDiagnostic(
            paddedBitmap,
            endpoints,
            acceptedJoins,
            "TracingV13_09_AcceptedJoins.png");

        // ============================================================
        // 15. Add accepted joins to the scaffold.
        // ============================================================

        using Mat repairedScaffold =
            dilated.Clone();

        foreach (LandformEndpointJoin join in acceptedJoins)
        {
            CvInvoke.Line(
                repairedScaffold,

                new System.Drawing.Point(
                    (int)Math.Round(
                        join.Start.X),
                    (int)Math.Round(
                        join.Start.Y)),

                new System.Drawing.Point(
                    (int)Math.Round(
                        join.End.X),
                    (int)Math.Round(
                        join.End.Y)),

                new MCvScalar(255),

                1,

                LineType.EightConnected,

                0);
        }

        SaveMaskDiagnostic(
            repairedScaffold,
            "TracingV13_10_RepairedScaffold.png");

        // ============================================================
        // 16. Extract candidate enclosed regions.
        //
        // The contour tree is intentionally used here rather than
        // external contours. Internal cartographic detail must not
        // prevent a large enclosing landform from being detected.
        // ============================================================

        List<ImportRegion> importRegions =
            ExtractImportRegionCandidates(
                repairedScaffold,
                bitmap.Width,
                bitmap.Height);

        Debug.WriteLine(
            $"V13 import regions found: " +
            $"{importRegions.Count}");

        // ============================================================
        // 17. Save ImportRegion diagnostic.
        // ============================================================

        SaveImportRegionDiagnostic(
            bitmap,
            importRegions,
            "TracingV13_11_ImportRegions.png");

        // ============================================================
        // 18. Return analysis result.
        // ============================================================

        LandformAnalysisResult result = new()
        {
            ImageWidth = bitmap.Width,
            ImageHeight = bitmap.Height,

            RepairedScaffold =
                repairedScaffold.Clone(),

            ImportRegions = importRegions
        };

        return result;
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

        SaveMaskDiagnostic(
            inverted,
            "TracingV13_11_InvertedScaffold.png");

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

    private static bool IsLikelyMapFrameInterior(SKRect bounds, double area, int imageWidth, int imageHeight)
    {
        double imageArea =
            (double)imageWidth *
            imageHeight;

        double areaFraction =
            area / imageArea;

        // A frame interior should be very large.
        if (areaFraction < 0.50)
            return false;

        float edgeToleranceX =
            Math.Max(
                10.0f,
                imageWidth * 0.03f);

        float edgeToleranceY =
            Math.Max(
                10.0f,
                imageHeight * 0.03f);

        int edgesTouched = 0;

        if (bounds.Left <= edgeToleranceX)
            edgesTouched++;

        if (bounds.Top <= edgeToleranceY)
            edgesTouched++;

        if (bounds.Right >= imageWidth - edgeToleranceX)
            edgesTouched++;

        if (bounds.Bottom >= imageHeight - edgeToleranceY)
            edgesTouched++;

        return edgesTouched >= 3;
    }

    private static Mat CreateRegionExtractionScaffold(Mat repairedScaffold)
    {
        int closingSize =
            ImportRegionScaffoldClosingSize;

        if (closingSize < 1)
            closingSize = 1;

        if ((closingSize & 1) == 0)
            closingSize++;

        using Mat kernel =
            CvInvoke.GetStructuringElement(
                MorphShapes.Ellipse,
                new System.Drawing.Size(
                    closingSize,
                    closingSize),
                new System.Drawing.Point(
                    -1,
                    -1));

        Mat closed =
            new(
                repairedScaffold.Rows,
                repairedScaffold.Cols,
                DepthType.Cv8U,
                1);

        CvInvoke.MorphologyEx(
            repairedScaffold,
            closed,
            MorphOp.Close,
            kernel,
            new System.Drawing.Point(
                -1,
                -1),
            1,
            BorderType.Constant,
            new MCvScalar(0));

        return closed;
    }

    private static bool TouchesArtificialBorder(System.Drawing.Rectangle bounds, Mat extractionScaffold)
    {
        return
            bounds.Left <= 0 ||
            bounds.Top <= 0 ||
            bounds.Right >= extractionScaffold.Cols ||
            bounds.Bottom >= extractionScaffold.Rows;
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

    private static SKPoint CalculatePathCentroid(SKPath path)
    {
        SKRect bounds =
            path.Bounds;

        return new SKPoint(
            bounds.MidX,
            bounds.MidY);
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

    private static void SaveImportRegionDiagnostic(
        SKBitmap bitmap,
        IReadOnlyList<ImportRegion> importRegions,
        string filename)
    {
        using SKBitmap diagnostic =
            bitmap.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create diagnostic bitmap.");

        using SKCanvas canvas =
            new(diagnostic);

        // ------------------------------------------------------------
        // Draw the original image.
        // ------------------------------------------------------------

        canvas.DrawBitmap(
            bitmap,
            0,
            0,
            SKSamplingOptions.Default);

        using SKPaint outlinePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3.0f,
            IsAntialias = true
        };

        using SKPaint textPaint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            IsAntialias = true
        };

        using SKPaint textBackgroundPaint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(
                0,
                0,
                0,
                180)
        };

        using SKFont font = new()
        {
            Size = 24.0f
        };

        int index = 0;

        foreach (ImportRegion region in importRegions)
        {
            index++;

            SKColor color =
                GetImportRegionDiagnosticColor(index);

            outlinePaint.Color = color;

            // --------------------------------------------------------
            // Draw the proposed region perimeter.
            // --------------------------------------------------------

            canvas.DrawPath(
                region.HitPath,
                outlinePaint);

            // --------------------------------------------------------
            // Create the label.
            // --------------------------------------------------------

            string confidenceText =
                region.Confidence.HasValue
                    ? $"{region.Confidence.Value:F0}%"
                    : "user";

            string label =
                $"{region.Id}: {confidenceText}";

            // --------------------------------------------------------
            // Measure the text using SKFont.
            //
            // SkiaSharp 4.x uses SKFont for text metrics.
            // --------------------------------------------------------

            float textWidth =
                font.MeasureText(
                    label,
                    out SKRect textBounds,
                    textPaint);

            SKRect bounds =
                region.Bounds;

            float x =
                bounds.MidX -
                textWidth / 2.0f;

            float baseline =
                bounds.MidY;

            // --------------------------------------------------------
            // Background rectangle around the label.
            // --------------------------------------------------------

            SKRect background =
                new(
                    x - 6.0f,
                    baseline + textBounds.Top - 6.0f,
                    x + textWidth + 6.0f,
                    baseline + textBounds.Bottom + 6.0f);

            canvas.DrawRect(
                background,
                textBackgroundPaint);

            // --------------------------------------------------------
            // Draw the label.
            //
            // SkiaSharp 4.x:
            //   DrawText(string, x, y, SKTextAlign, SKFont, SKPaint)
            // --------------------------------------------------------

            canvas.DrawText(
                label,
                x,
                baseline,
                SKTextAlign.Left,
                font,
                textPaint);

            Debug.WriteLine(
                $"Import region diagnostic: " +
                $"ID={region.Id}, " +
                $"State={region.State}, " +
                $"Confidence={confidenceText}, " +
                $"Area={region.Area:F0}, " +
                $"Bounds={region.Bounds}, " +
                $"ScaffoldSupport=" +
                $"{region.ScaffoldPerimeterSupport:P1}");
        }

        SaveBitmapDiagnostic(
            diagnostic,
            filename);
    }

    private static SKColor GetImportRegionDiagnosticColor(
    int index)
    {
        SKColor[] colors =
        [
            SKColors.Red,
            SKColors.Lime,
            SKColors.Magenta,
            SKColors.Cyan,
            SKColors.Yellow,
            SKColors.Orange,
            SKColors.Blue,
            SKColors.DeepPink,
            SKColors.Chartreuse,
            SKColors.Aqua
        ];

        return colors[
            (index - 1) %
            colors.Length];
    }

    private static void SaveBitmapDiagnostic(SKBitmap bitmap, string filename)
    {
        using SKImage image =
            SKImage.FromBitmap(bitmap);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        string path =
            Path.Combine(
                GetDiagnosticDirectory(),
                filename);

        using FileStream stream =
            File.Create(path);

        data.SaveTo(stream);

        Debug.WriteLine(
            $"Saved bitmap diagnostic: {path}");
    }

    private static Mat DetectExternalMaterialMask(SKBitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        Debug.WriteLine(
            "==================================================");
        Debug.WriteLine(
            "Detecting external material V13...");
        Debug.WriteLine(
            $"Image: {width} x {height}");
        Debug.WriteLine(
            "==================================================");

        int pixelCount =
            checked(width * height);

        // ------------------------------------------------------------
        // Read the source pixels.
        // ------------------------------------------------------------

        byte[] sourcePixels =
            new byte[bitmap.RowBytes * height];

        Marshal.Copy(
            bitmap.GetPixels(),
            sourcePixels,
            0,
            sourcePixels.Length);

        // ------------------------------------------------------------
        // Every quantized RGB color is treated as a material family.
        //
        // For quantization = 16, each channel is reduced from
        // 0-255 to 0-15.
        // ------------------------------------------------------------

        int quantization =
            ExternalMaterialColorQuantization;

        int familyCount =
            quantization *
            quantization *
            quantization;

        int[] families =
            new int[pixelCount];

        // ------------------------------------------------------------
        // Build the material-family image.
        // ------------------------------------------------------------

        for (int y = 0; y < height; y++)
        {
            int rowOffset =
                y * bitmap.RowBytes;

            int familyOffset =
                y * width;

            for (int x = 0; x < width; x++)
            {
                int pixelOffset =
                    rowOffset + x * 4;

                byte blue =
                    sourcePixels[pixelOffset + 0];

                byte green =
                    sourcePixels[pixelOffset + 1];

                byte red =
                    sourcePixels[pixelOffset + 2];

                families[familyOffset + x] =
                    GetMaterialColorFamily(
                        red,
                        green,
                        blue);
            }
        }

        // ------------------------------------------------------------
        // Count how many pixels of each material family touch the
        // ORIGINAL image boundary.
        //
        // This is important:
        //
        // We are not assuming that the ocean is the largest region.
        // We are not assuming that it is blue.
        // We are not assuming that it is in a particular corner.
        //
        // A material must actually occur on the image boundary to be
        // considered external.
        // ------------------------------------------------------------

        int[] boundaryFamilyCounts =
            new int[familyCount];

        for (int x = 0; x < width; x++)
        {
            int topIndex = x;

            boundaryFamilyCounts[
                families[topIndex]]++;

            if (height > 1)
            {
                int bottomIndex =
                    (height - 1) * width + x;

                boundaryFamilyCounts[
                    families[bottomIndex]]++;
            }
        }

        for (int y = 1; y < height - 1; y++)
        {
            int leftIndex =
                y * width;

            boundaryFamilyCounts[
                families[leftIndex]]++;

            if (width > 1)
            {
                int rightIndex =
                    y * width + width - 1;

                boundaryFamilyCounts[
                    families[rightIndex]]++;
            }
        }

        // ------------------------------------------------------------
        // Identify material families with substantial boundary support.
        // ------------------------------------------------------------

        List<int> candidateFamilies = [];

        for (int family = 0;
             family < familyCount;
             family++)
        {
            int boundaryCount =
                boundaryFamilyCounts[family];

            if (boundaryCount <
                ExternalMaterialMinimumBoundaryPixels)
            {
                continue;
            }

            candidateFamilies.Add(
                family);

            Debug.WriteLine(
                $"External material family candidate: " +
                $"{family}, " +
                $"boundary pixels={boundaryCount}");
        }

        Debug.WriteLine(
            $"External material candidate families: " +
            $"{candidateFamilies.Count}");

        // ------------------------------------------------------------
        // Final mask.
        // ------------------------------------------------------------

        byte[] finalPixels =
            new byte[pixelCount];

        // ------------------------------------------------------------
        // Process each candidate material family independently.
        //
        // We perform a connected-component search over pixels belonging
        // to that exact quantized material family.
        // ------------------------------------------------------------

        foreach (int candidateFamily in candidateFamilies)
        {
            bool[] visited =
                new bool[pixelCount];

            Queue<int> queue =
                new();

            // --------------------------------------------------------
            // Seed the search from every boundary pixel belonging to
            // this material family.
            // --------------------------------------------------------

            for (int x = 0; x < width; x++)
            {
                int topIndex =
                    x;

                if (!visited[topIndex] &&
                    families[topIndex] ==
                    candidateFamily)
                {
                    visited[topIndex] = true;
                    queue.Enqueue(topIndex);
                }

                if (height > 1)
                {
                    int bottomIndex =
                        (height - 1) * width + x;

                    if (!visited[bottomIndex] &&
                        families[bottomIndex] ==
                        candidateFamily)
                    {
                        visited[bottomIndex] = true;
                        queue.Enqueue(bottomIndex);
                    }
                }
            }

            for (int y = 1;
                 y < height - 1;
                 y++)
            {
                int leftIndex =
                    y * width;

                if (!visited[leftIndex] &&
                    families[leftIndex] ==
                    candidateFamily)
                {
                    visited[leftIndex] = true;
                    queue.Enqueue(leftIndex);
                }

                if (width > 1)
                {
                    int rightIndex =
                        y * width + width - 1;

                    if (!visited[rightIndex] &&
                        families[rightIndex] ==
                        candidateFamily)
                    {
                        visited[rightIndex] = true;
                        queue.Enqueue(rightIndex);
                    }
                }
            }

            // --------------------------------------------------------
            // Grow the connected component.
            // --------------------------------------------------------

            List<int> componentPixels =
                [];

            int boundaryPixelCount =
                0;

            while (queue.Count > 0)
            {
                int index =
                    queue.Dequeue();

                // Every seeded pixel belongs to this family.
                componentPixels.Add(index);

                int x =
                    index % width;

                int y =
                    index / width;

                if (x == 0 ||
                    y == 0 ||
                    x == width - 1 ||
                    y == height - 1)
                {
                    boundaryPixelCount++;
                }

                // ----------------------------------------------------
                // 8-connected neighborhood.
                // ----------------------------------------------------

                for (int dy = -1;
                     dy <= 1;
                     dy++)
                {
                    for (int dx = -1;
                         dx <= 1;
                         dx++)
                    {
                        if (dx == 0 &&
                            dy == 0)
                        {
                            continue;
                        }

                        int nx =
                            x + dx;

                        int ny =
                            y + dy;

                        if (nx < 0 ||
                            nx >= width ||
                            ny < 0 ||
                            ny >= height)
                        {
                            continue;
                        }

                        int neighborIndex =
                            ny * width + nx;

                        if (visited[neighborIndex])
                            continue;

                        if (families[neighborIndex] !=
                            candidateFamily)
                        {
                            continue;
                        }

                        visited[neighborIndex] = true;

                        queue.Enqueue(
                            neighborIndex);
                    }
                }
            }

            int componentArea =
                componentPixels.Count;

            bool accepted =
                componentArea >=
                    ExternalMaterialMinimumComponentArea &&
                boundaryPixelCount >=
                    ExternalMaterialMinimumBoundaryPixels;

            Debug.WriteLine(
                $"External material family {candidateFamily}: " +
                $"area={componentArea}, " +
                $"boundary={boundaryPixelCount}, " +
                $"accepted={accepted}");

            if (!accepted)
                continue;

            // --------------------------------------------------------
            // Add the accepted external component to the final mask.
            // --------------------------------------------------------

            foreach (int index in componentPixels)
            {
                finalPixels[index] =
                    255;
            }
        }

        // ------------------------------------------------------------
        // Create the one-channel OpenCV mask.
        // ------------------------------------------------------------

        Mat result =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        Marshal.Copy(
            finalPixels,
            0,
            result.DataPointer,
            finalPixels.Length);

        int finalPixelCount =
            0;

        foreach (byte value in finalPixels)
        {
            if (value != 0)
                finalPixelCount++;
        }

        Debug.WriteLine(
            $"Final external material pixels: " +
            $"{finalPixelCount}");

        Debug.WriteLine(
            "==================================================");

        return result;
    }

    private static void SaveExternalMaterialOverlayDiagnostic(SKBitmap source, Mat mask, string filename)
    {
        using SKBitmap diagnostic =
            source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create diagnostic bitmap.");

        int width =
            diagnostic.Width;

        int height =
            diagnostic.Height;

        byte[] maskPixels =
            new byte[width * height];

        Marshal.Copy(
            mask.DataPointer,
            maskPixels,
            0,
            maskPixels.Length);

        using SKCanvas canvas =
            new(diagnostic);

        using SKPaint overlayPaint =
            new()
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(
                    255,
                    0,
                    0,
                    80),
                IsAntialias = false
            };

        for (int y = 0; y < height; y++)
        {
            int xStart = -1;

            for (int x = 0; x < width; x++)
            {
                bool masked =
                    maskPixels[
                        y * width + x] != 0;

                if (masked && xStart < 0)
                {
                    xStart = x;
                }

                bool endOfRun =
                    xStart >= 0 &&
                    (!masked || x == width - 1);

                if (!endOfRun)
                    continue;

                int xEnd =
                    masked && x == width - 1
                        ? x
                        : x - 1;

                canvas.DrawRect(
                    xStart,
                    y,
                    xEnd + 1,
                    y + 1,
                    overlayPaint);

                xStart = -1;
            }
        }

        using SKImage image =
            SKImage.FromBitmap(diagnostic);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        string path =
            Path.Combine(
                GetDiagnosticDirectory(),
                filename);

        using FileStream stream =
            File.Create(path);

        data.SaveTo(stream);

        Debug.WriteLine(
            $"Saved external material overlay: {path}");
    }

    private static SKBitmap ReplaceExternalMaterialWithWhite(SKBitmap source, Mat externalMaterialMask)
    {
        SKBitmap result =
            source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create external-material image.");

        int width =
            result.Width;

        int height =
            result.Height;

        byte[] maskPixels =
            new byte[width * height];

        Marshal.Copy(
            externalMaterialMask.DataPointer,
            maskPixels,
            0,
            maskPixels.Length);

        IntPtr pixelPointer =
            result.GetPixels();

        if (pixelPointer == IntPtr.Zero)
        {
            result.Dispose();

            throw new InvalidOperationException(
                "Unable to access bitmap pixels.");
        }

        byte[] pixels =
            new byte[
                result.RowBytes *
                result.Height];

        Marshal.Copy(
            pixelPointer,
            pixels,
            0,
            pixels.Length);

        for (int y = 0; y < height; y++)
        {
            int rowOffset =
                y * result.RowBytes;

            int maskOffset =
                y * width;

            for (int x = 0; x < width; x++)
            {
                if (maskPixels[maskOffset + x] == 0)
                    continue;

                int pixelOffset =
                    rowOffset + x * 4;

                // BGRA
                pixels[pixelOffset + 0] = 255;
                pixels[pixelOffset + 1] = 255;
                pixels[pixelOffset + 2] = 255;
                pixels[pixelOffset + 3] = 255;
            }
        }

        Marshal.Copy(
            pixels,
            0,
            pixelPointer,
            pixels.Length);

        return result;
    }

    private static Mat CreateNearWhiteMask(Mat source, int width, int height, double maximumColorDistance)
    {
        byte[] pixels =
            new byte[width * height * 4];

        Marshal.Copy(
            source.DataPointer,
            pixels,
            0,
            pixels.Length);

        byte[] mask =
            new byte[width * height];

        double maximumDistanceSquared =
            maximumColorDistance *
            maximumColorDistance;

        for (int y = 0; y < height; y++)
        {
            int rowOffset =
                y * width * 4;

            int maskOffset =
                y * width;

            for (int x = 0; x < width; x++)
            {
                int offset =
                    rowOffset + x * 4;

                // BGRA ordering.
                int b = pixels[offset];
                int g = pixels[offset + 1];
                int r = pixels[offset + 2];

                double dr = 255.0 - r;
                double dg = 255.0 - g;
                double db = 255.0 - b;

                double distanceSquared =
                    dr * dr +
                    dg * dg +
                    db * db;

                if (distanceSquared <=
                    maximumDistanceSquared)
                {
                    mask[maskOffset + x] = 255;
                }
            }
        }

        Mat result =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        Marshal.Copy(
            mask,
            0,
            result.DataPointer,
            mask.Length);

        return result;
    }

    private static Mat CreateConnectedWhiteMask(Mat whiteMask, int width, int height)
    {
        byte[] source =
            new byte[width * height];

        Marshal.Copy(
            whiteMask.DataPointer,
            source,
            0,
            source.Length);

        byte[] connected =
            new byte[width * height];

        Queue<int> queue = new();

        void AddSeed(int x, int y)
        {
            int index =
                y * width + x;

            if (source[index] == 0 ||
                connected[index] != 0)
            {
                return;
            }

            connected[index] = 255;
            queue.Enqueue(index);
        }

        // Top and bottom edges.
        for (int x = 0; x < width; x++)
        {
            AddSeed(x, 0);
            AddSeed(x, height - 1);
        }

        // Left and right edges.
        for (int y = 0; y < height; y++)
        {
            AddSeed(0, y);
            AddSeed(width - 1, y);
        }

        // 8-connected flood fill.
        int[] dx =
        {
            -1, 0, 1,
            -1,     1,
            -1, 0, 1
        };

        int[] dy =
        {
            -1, -1, -1,
             0,      0,
             1,  1,  1
        };

        while (queue.Count > 0)
        {
            int index =
                queue.Dequeue();

            int x =
                index % width;

            int y =
                index / width;

            for (int i = 0; i < 8; i++)
            {
                int nx =
                    x + dx[i];

                int ny =
                    y + dy[i];

                if (nx < 0 ||
                    nx >= width ||
                    ny < 0 ||
                    ny >= height)
                {
                    continue;
                }

                int neighbor =
                    ny * width + nx;

                if (source[neighbor] == 0 ||
                    connected[neighbor] != 0)
                {
                    continue;
                }

                connected[neighbor] = 255;
                queue.Enqueue(neighbor);
            }
        }

        Mat result =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        Marshal.Copy(
            connected,
            0,
            result.DataPointer,
            connected.Length);

        return result;
    }

    private static Mat CreateMaskBoundary(Mat connectedMask, int width, int height)
    {
        byte[] source =
            new byte[width * height];

        Marshal.Copy(
            connectedMask.DataPointer,
            source,
            0,
            source.Length);

        byte[] boundary =
            new byte[width * height];

        for (int y = 1; y < height - 1; y++)
        {
            int rowOffset =
                y * width;

            for (int x = 1; x < width - 1; x++)
            {
                int index =
                    rowOffset + x;

                if (source[index] == 0)
                    continue;

                bool isBoundary =
                    source[index - width - 1] == 0 ||
                    source[index - width] == 0 ||
                    source[index - width + 1] == 0 ||

                    source[index - 1] == 0 ||
                    source[index + 1] == 0 ||

                    source[index + width - 1] == 0 ||
                    source[index + width] == 0 ||
                    source[index + width + 1] == 0;

                if (isBoundary)
                {
                    boundary[index] = 255;
                }
            }
        }

        Mat result =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        Marshal.Copy(
            boundary,
            0,
            result.DataPointer,
            boundary.Length);

        return result;
    }

    private static SKBitmap CreateArtificialBorderBitmap(SKBitmap source, int borderSize)
    {
        if (borderSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(borderSize));

        int width =
            source.Width +
            borderSize * 2;

        int height =
            source.Height +
            borderSize * 2;

        SKBitmap padded =
            new(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

        using SKCanvas canvas =
            new(padded);

        // The artificial exterior of the map.
        canvas.Clear(SKColors.White);

        // Put the original image inside the artificial border.
        canvas.DrawBitmap(
            source,
            borderSize,
            borderSize,
            SKSamplingOptions.Default);

        return padded;
    }

    private static void SaveLandformEndpointDiagnostic(
    SKBitmap source,
    List<LandformEndpoint> endpoints,
    string filename)
    {
        using SKBitmap diagnostic =
            source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create diagnostic bitmap.");

        using SKCanvas canvas =
            new(diagnostic);

        using SKPaint endpointPaint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Red,
            IsAntialias = true
        };

        using SKPaint directionPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Yellow,
            StrokeWidth = 2.0f,
            IsAntialias = true
        };

        foreach (LandformEndpoint endpoint in endpoints)
        {
            canvas.DrawCircle(
                endpoint.Position,
                4.0f,
                endpointPaint);

            SKPoint directionEnd =
                new(
                    endpoint.Position.X +
                    endpoint.Direction.X * 12.0f,

                    endpoint.Position.Y +
                    endpoint.Direction.Y * 12.0f);

            canvas.DrawLine(
                endpoint.Position,
                directionEnd,
                directionPaint);
        }

        using SKImage image =
            SKImage.FromBitmap(diagnostic);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        string path =
            Path.Combine(
                GetDiagnosticDirectory(),
                filename);

        using FileStream stream =
            File.Create(path);

        data.SaveTo(stream);

        Debug.WriteLine(
            $"Saved endpoint diagnostic: {path}");
    }

    private static void SaveLandformJoinDiagnostic(SKBitmap source, List<LandformEndpoint> endpoints, List<LandformEndpointJoin> joins, string filename)
    {
        using SKBitmap diagnostic =
            source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to create diagnostic bitmap.");

        using SKCanvas canvas =
            new(diagnostic);

        using SKPaint endpointPaint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Red,
            IsAntialias = true
        };

        using SKPaint acceptedJoinPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Lime,
            StrokeWidth = 3.0f,
            IsAntialias = true
        };

        using SKPaint rejectedJoinPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Magenta,
            StrokeWidth = 2.0f,
            IsAntialias = true
        };

        foreach (LandformEndpoint endpoint in endpoints)
        {
            canvas.DrawCircle(
                endpoint.Position,
                3.0f,
                endpointPaint);
        }

        foreach (LandformEndpointJoin join in joins)
        {
            SKPaint paint =
                join.Accepted
                    ? acceptedJoinPaint
                    : rejectedJoinPaint;

            canvas.DrawLine(
                join.Start,
                join.End,
                paint);
        }

        using SKImage image =
            SKImage.FromBitmap(diagnostic);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        string path =
            Path.Combine(
                GetDiagnosticDirectory(),
                filename);

        using FileStream stream =
            File.Create(path);

        data.SaveTo(stream);

        Debug.WriteLine(
            $"Saved join diagnostic: {path}");
    }

    private static List<LandformEndpoint> FindLandformEndpoints(Mat edges)
    {
        int width = edges.Cols;
        int height = edges.Rows;

        byte[] pixels =
            new byte[width * height];

        Marshal.Copy(
            edges.DataPointer,
            pixels,
            0,
            pixels.Length);

        List<LandformEndpoint> endpoints = [];

        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                int index =
                    y * width + x;

                if (pixels[index] == 0)
                    continue;

                int neighborCount = 0;

                int neighborXSum = 0;
                int neighborYSum = 0;

                for (int offsetY = -1;
                     offsetY <= 1;
                     offsetY++)
                {
                    for (int offsetX = -1;
                         offsetX <= 1;
                         offsetX++)
                    {
                        if (offsetX == 0 &&
                            offsetY == 0)
                        {
                            continue;
                        }

                        int nx =
                            x + offsetX;

                        int ny =
                            y + offsetY;

                        if (pixels[
                                ny * width + nx] == 0)
                        {
                            continue;
                        }

                        neighborCount++;

                        neighborXSum += offsetX;
                        neighborYSum += offsetY;
                    }
                }

                // A true endpoint normally has exactly one
                // neighboring edge pixel.
                if (neighborCount != 1)
                    continue;

                double length =
                    Math.Sqrt(
                        neighborXSum * neighborXSum +
                        neighborYSum * neighborYSum);

                if (length < 0.001)
                    continue;

                // Direction points AWAY from the neighboring pixel.
                SKPoint direction =
                    new(
                        (float)(-neighborXSum / length),
                        (float)(-neighborYSum / length));

                endpoints.Add(
                    new LandformEndpoint
                    {
                        Position =
                            new SKPoint(x, y),

                        Direction =
                            direction,

                        NeighborCount =
                            neighborCount
                    });
            }
        }

        return endpoints;
    }

    private static List<LandformEndpointJoin> FindProposedEndpointJoins(List<LandformEndpoint> endpoints, int imageWidth, int imageHeight)
    {
        List<LandformEndpointJoin> joins = [];

        bool[] used =
            new bool[endpoints.Count];

        List<(double Distance, int A, int B)> candidates = [];

        for (int i = 0;
             i < endpoints.Count;
             i++)
        {
            for (int j = i + 1;
                 j < endpoints.Count;
                 j++)
            {
                double dx =
                    endpoints[j].Position.X -
                    endpoints[i].Position.X;

                double dy =
                    endpoints[j].Position.Y -
                    endpoints[i].Position.Y;

                double distance =
                    Math.Sqrt(
                        dx * dx +
                        dy * dy);

                if (distance >
                    LandformEndpointMaxJoinDistance)
                {
                    continue;
                }

                if (distance < 1.0)
                    continue;

                candidates.Add(
                    (distance, i, j));
            }
        }

        // Examine the closest endpoint pairs first.
        candidates.Sort(
            (a, b) =>
                a.Distance.CompareTo(b.Distance));

        foreach (var candidate in candidates)
        {
            int indexA =
                candidate.A;

            int indexB =
                candidate.B;

            if (used[indexA] ||
                used[indexB])
            {
                continue;
            }

            LandformEndpoint endpointA =
                endpoints[indexA];

            LandformEndpoint endpointB =
                endpoints[indexB];

            SKPoint toB =
                Normalize(
                    new SKPoint(
                        endpointB.Position.X -
                        endpointA.Position.X,
                        endpointB.Position.Y -
                        endpointA.Position.Y));

            SKPoint toA =
                Normalize(
                    new SKPoint(
                        endpointA.Position.X -
                        endpointB.Position.X,
                        endpointA.Position.Y -
                        endpointB.Position.Y));

            double angleA =
                AngleBetween(
                    endpointA.Direction,
                    toB);

            double angleB =
                AngleBetween(
                    endpointB.Direction,
                    toA);

            if (angleA >
                LandformEndpointMaxJoinAngleDegrees)
            {
                continue;
            }

            if (angleB >
                LandformEndpointMaxJoinAngleDegrees)
            {
                continue;
            }

            joins.Add(
                new LandformEndpointJoin
                {
                    Start =
                        endpointA.Position,

                    End =
                        endpointB.Position,

                    Distance =
                        candidate.Distance,

                    AngleDegrees =
                        Math.Max(
                            angleA,
                            angleB)
                });

            // Preserve the one-join-per-endpoint rule.
            used[indexA] = true;
            used[indexB] = true;
        }

        return joins;
    }

    private static bool IsScaffoldAwareJoinValid(Mat scaffold, LandformEndpointJoin join)
    {
        double dx =
            join.End.X -
            join.Start.X;

        double dy =
            join.End.Y -
            join.Start.Y;

        double distance =
            Math.Sqrt(
                dx * dx +
                dy * dy);

        if (distance < 1.0)
            return false;

        int sampleCount =
            Math.Max(
                2,
                (int)Math.Ceiling(
                    distance /
                    LandformJoinSampleSpacing));

        int scaffoldSamples = 0;

        int totalSamples = 0;

        int width =
            scaffold.Cols;

        int height =
            scaffold.Rows;

        byte[] pixels =
            new byte[width * height];

        Marshal.Copy(
            scaffold.DataPointer,
            pixels,
            0,
            pixels.Length);

        for (int i = 0;
             i <= sampleCount;
             i++)
        {
            double t =
                (double)i /
                sampleCount;

            double x =
                join.Start.X +
                dx * t;

            double y =
                join.Start.Y +
                dy * t;

            int ix =
                (int)Math.Round(x);

            int iy =
                (int)Math.Round(y);

            if (ix < 0 ||
                ix >= width ||
                iy < 0 ||
                iy >= height)
            {
                return false;
            }

            // Don't count the endpoint neighborhoods against
            // the "existing scaffold" test. Those areas are
            // supposed to contain scaffold.
            if (i <= LandformJoinEndpointClearance ||
                i >= sampleCount -
                     LandformJoinEndpointClearance)
            {
                continue;
            }

            totalSamples++;

            if (pixels[
                    iy * width + ix] != 0)
            {
                scaffoldSamples++;
            }
        }

        if (totalSamples <= 0)
            return true;

        double scaffoldFraction =
            (double)scaffoldSamples /
            totalSamples;

        return
            scaffoldFraction <=
            LandformJoinMaximumScaffoldFraction;
    }

    private static Mat CreateMat(SKBitmap source)
    {
        using SKBitmap bitmap = source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to convert image to BGRA8888.");

        Mat mat = new(
            bitmap.Height,
            bitmap.Width,
            DepthType.Cv8U,
            4);

        int byteCount = bitmap.RowBytes * bitmap.Height;

        byte[] pixels = bitmap.Bytes;

        Marshal.Copy(
            pixels,
            0,
            mat.DataPointer,
            byteCount);

        return mat;
    }

    private static int GetMaterialColorFamily(byte red, byte green, byte blue)
    {
        int r = red >> 4;

        int g = green >> 4;

        int b = blue >> 4;

        return r | (g << 4) | (b << 8);
    }

    private static SKPoint Normalize(SKPoint point)
    {
        // TODO: there is at least one Normalize method already in one of the libraries
        float length =
            MathF.Sqrt(
                point.X * point.X +
                point.Y * point.Y);

        if (length < 0.0001f)
            return new SKPoint(0, 0);

        return new SKPoint(
            point.X / length,
            point.Y / length);
    }

    private static double AngleBetween(SKPoint first, SKPoint second)
    {
        // TODO: thre might be an AngleBetween implementation already in one of the libraries
        first = Normalize(first);
        second = Normalize(second);

        float dotProduct =
            first.X * second.X +
            first.Y * second.Y;

        dotProduct =
            Math.Clamp(
                dotProduct,
                -1.0f,
                1.0f);

        return
            Math.Acos(dotProduct) *
            180.0 /
            Math.PI;
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

    private sealed class LandformEndpoint
    {
        public SKPoint Position { get; init; }

        public SKPoint Direction { get; init; }

        public int NeighborCount { get; init; }
    }


    private sealed class LandformEndpointJoin
    {
        public SKPoint Start { get; init; }

        public SKPoint End { get; init; }

        public double Distance { get; init; }

        public double AngleDegrees { get; init; }

        public bool Accepted { get; set; }
    }

    private sealed class EndpointInfo
    {
        public System.Drawing.Point Position { get; }

        public SKPoint Direction { get; }

        public EndpointInfo(
            System.Drawing.Point position,
            SKPoint direction)
        {
            Position = position;
            Direction = direction;
        }
    }

    private sealed class EndpointJoin
    {
        public System.Drawing.Point Start { get; }

        public System.Drawing.Point End { get; }

        public double Distance { get; }

        public double Alignment { get; }

        public EndpointJoin(
            System.Drawing.Point start,
            System.Drawing.Point end,
            double distance,
            double alignment)
        {
            Start = start;
            End = end;
            Distance = distance;
            Alignment = alignment;
        }
    }

    private static void SaveMaskDiagnostic(
        Mat mask,
        string filename)
    {
        int width = mask.Cols;
        int height = mask.Rows;

        byte[] maskPixels =
            new byte[width * height];

        Marshal.Copy(
            mask.DataPointer,
            maskPixels,
            0,
            maskPixels.Length);

        using SKBitmap bitmap =
            new(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque);

        byte[] bitmapPixels =
            new byte[width * height * 4];

        for (int i = 0; i < maskPixels.Length; i++)
        {
            byte value =
                maskPixels[i];

            int offset =
                i * 4;

            bitmapPixels[offset + 0] = value;
            bitmapPixels[offset + 1] = value;
            bitmapPixels[offset + 2] = value;
            bitmapPixels[offset + 3] = 255;
        }

        Marshal.Copy(
            bitmapPixels,
            0,
            bitmap.GetPixels(),
            bitmapPixels.Length);

        string directory =
            GetDiagnosticDirectory();

        string path =
            Path.Combine(
                directory,
                filename);

        using SKImage image =
            SKImage.FromBitmap(bitmap);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        using FileStream stream =
            File.Create(path);

        data.SaveTo(stream);

        Debug.WriteLine(
            $"Saved diagnostic mask: {path}");
    }


    private static string GetDiagnosticDirectory()
    {
        string desktop =
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

        string directory =
            Path.Combine(
                desktop,
                "RealmStudioX Image Analysis");

        Directory.CreateDirectory(directory);

        return directory;
    }

    //====================================================================================
    //====================================================================================
    //====================================================================================
    //====================================================================================
}
