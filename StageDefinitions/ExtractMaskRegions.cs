using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;

namespace RealmStudioImageAnalysisLib.StageDefinitions;

public sealed class ExtractMaskRegions : IPerimeterStage
{
    public string Id => "ExtractMaskRegions";

    private const int ArtificialBorderSize = 2;

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

        Mat landMask = data.Get<Mat>(inputName);

        if (landMask.IsEmpty ||
            landMask.NumberOfChannels != 1 ||
            landMask.Depth != DepthType.Cv8U)
        {
            throw new InvalidOperationException(
                $"{Id} requires a non-empty, 8-bit, single-channel mask.");
        }

        string outputName = context.OutputArtifact
            ?? throw new InvalidOperationException(
                $"{Id} requires an output artifact.");

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 0.1,
            Status = "Preparing the mask for contour extraction."
        });

        // Add a black border around the mask.
        // White pixels represent land; black pixels represent ocean.
        using Mat contourInput = new();

        CvInvoke.CopyMakeBorder(
            landMask,
            contourInput,
            ArtificialBorderSize,
            ArtificialBorderSize,
            ArtificialBorderSize,
            ArtificialBorderSize,
            BorderType.Constant,
            new MCvScalar(0));

        // FindContours modifies its input, so contourInput is a separate Mat.
        using VectorOfVectorOfPoint contours = new();
        using Mat hierarchy = new();

        CvInvoke.FindContours(
            contourInput,
            contours,
            hierarchy,
            RetrType.External,
            ChainApproxMethod.ChainApproxSimple);

        Debug.WriteLine(
            $"{Id}: Found {contours.Size} external contours.");

        List<ImportRegion> regions = [];

        for (int i = 0; i < contours.Size; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            using VectorOfPoint contour = contours[i];

            // A usable closed perimeter requires at least three points.
            if (contour.Size < 3)
            {
                Debug.WriteLine(
                    $"{Id}: Skipping degenerate contour {i}, " +
                    $"with {contour.Size} points.");

                continue;
            }

            double area = Math.Abs(
                CvInvoke.ContourArea(contour));

            double perimeterLength =
                CvInvoke.ArcLength(contour, true);

            // Contour coordinates include the artificial border.
            // Translate the bounding rectangle back to image coordinates.
            Rectangle contourBounds =
                CvInvoke.BoundingRectangle(contour);

            contourBounds.Offset(
                -ArtificialBorderSize,
                -ArtificialBorderSize);

            // Create path geometry in the original image coordinate system.
            using SKPath geometry =
                CreateRegionPath(contour, ArtificialBorderSize);

            SKRect bounds = geometry.Bounds;

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                Debug.WriteLine(
                    $"{Id}: Skipping degenerate contour {i}; " +
                    $"bounds={bounds}.");

                continue;
            }

            // No area filtering, image-border rejection, or confidence
            // scoring at this stage. The candidates will be reviewed later.
            ImportRegion region = new(geometry)
            {
                Index = regions.Count + 1,
                Source = ImportRegionSource.Automatic,
                State = ImportRegionState.Proposed,
                Confidence = 0.0,
                Area = area,
                PerimeterLength = perimeterLength,
                ScaffoldPerimeterSupport = 0.0
            };

            regions.Add(region);

            Debug.WriteLine(
                $"{Id}: Region {region.Index}: " +
                $"contourArea={area:F0}, " +
                $"perimeter={perimeterLength:F1}, " +
                $"pixelBounds={contourBounds}, " +
                $"pathBounds={bounds}.");
        }

        // Publish the candidates using the configured output artifact name.
        data.Set(outputName, regions);

        Debug.WriteLine(
            $"{Id}: Extracted {regions.Count} proposed regions.");

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 1.0,
            Status = $"Extracted {regions.Count} proposed regions."
        });

        return new PerimeterStageResult
        {
            Succeeded = true
        };
    }

    private static SKPath CreateRegionPath(
        VectorOfPoint contour,
        int artificialBorder)
    {
        Point[] points = contour.ToArray();

        using SKPathBuilder builder = new();

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
}