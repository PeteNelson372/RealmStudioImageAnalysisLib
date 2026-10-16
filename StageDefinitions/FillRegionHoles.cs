using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class FillRegionHoles : IPerimeterStage
    {
        public string Id => "FillRegionHoles";

        private const int Padding = 2;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Retrieve the input regions.
            // ------------------------------------------------------------

            List<string> inputNames =
                [.. context.InputArtifacts];

            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, " +
                    $"but {inputNames.Count} were provided.");
            }

            string inputArtifact =
                context.GetInputArtifact(inputNames[0])
                ?? throw new InvalidOperationException(
                    $"{Id} requires an input artifact.");

            List<ImportRegion> regions = data.Get<List<ImportRegion>>(inputArtifact);

            if (string.IsNullOrWhiteSpace(context.OutputArtifact))
            {
                throw new InvalidOperationException(
                    $"{Id} requires an output artifact.");
            }

            List<ImportRegion> filledRegions = new(regions.Count);

            int modifiedCount = 0;

            // ------------------------------------------------------------
            // Process each region independently.
            // ------------------------------------------------------------

            for (int i = 0; i < regions.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                ImportRegion region = regions[i];

                ImportRegion result =
                    FillHoles(region, context.CancellationToken,
                        out bool modified);

                filledRegions.Add(result);

                if (modified)
                    modifiedCount++;

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress = (double)(i + 1) / regions.Count,
                        Status = $"Processed region {i + 1} of {regions.Count}."
                    });
            }

            // ------------------------------------------------------------
            // Publish the resulting regions.
            // ------------------------------------------------------------

            // since this is a refinement pipeline, the output has to be a list of SKPath, so we convert the ImportRegion to SKPath
            List<SKPath> filledPerimeters = [.. filledRegions.Select(region => region.CloneGeometry())];

            data.Set(
                context.OutputArtifact,
                filledPerimeters);

            Debug.WriteLine(
                $"{Id}: processed {regions.Count} regions; " +
                $"rebuilt {modifiedCount} region perimeters.");

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static ImportRegion FillHoles(
            ImportRegion region,
            CancellationToken cancellationToken,
            out bool modified)
        {
            modified = false;

            using SKPath sourcePath =
                region.CloneGeometry();

            SKRect bounds = sourcePath.Bounds;

            // Invalid or empty geometry: leave the region unchanged.
            if (sourcePath.IsEmpty ||
                bounds.Width <= 0 ||
                bounds.Height <= 0)
            {
                return region;
            }

            // ------------------------------------------------------------
            // Create a local raster mask.
            //
            // Work in the region's own bounds rather than allocating a
            // mask covering the entire source image.
            // ------------------------------------------------------------

            int left = (int)Math.Floor(bounds.Left);
            int top = (int)Math.Floor(bounds.Top);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);

            int width =
                checked(right - left + Padding * 2);

            int height =
                checked(bottom - top + Padding * 2);

            using SKBitmap maskBitmap =
                new(
                    new SKImageInfo(
                        width,
                        height,
                        SKColorType.Gray8,
                        SKAlphaType.Opaque));

            using (SKCanvas canvas = new(maskBitmap))
            using (SKPaint paint = new())
            {
                canvas.Clear(SKColors.Black);

                canvas.Translate(
                    Padding - left,
                    Padding - top);

                paint.Color = SKColors.White;
                paint.Style = SKPaintStyle.Fill;
                paint.IsAntialias = false;

                canvas.DrawPath(sourcePath, paint);
                canvas.Flush();
            }

            // ------------------------------------------------------------
            // Wrap the bitmap's pixel storage in a single-channel OpenCV
            // Mat. The bitmap remains alive for the lifetime of the Mat.
            // ------------------------------------------------------------

            IntPtr pixelData = maskBitmap.GetPixels();

            if (pixelData == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Could not allocate the mask for region {region.Index}.");
            }

            using Mat mask =
                new(
                    height,
                    width,
                    DepthType.Cv8U,
                    1,
                    pixelData,
                    maskBitmap.RowBytes);

            // ------------------------------------------------------------
            // Retrieve only the external contours.
            //
            // Internal contours representing holes are deliberately
            // ignored. Separate external components are all retained.
            // ------------------------------------------------------------

            using VectorOfVectorOfPoint contours = new();

            CvInvoke.FindContours(
                mask,
                contours,
                null,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            cancellationToken.ThrowIfCancellationRequested();

            if (contours.Size == 0)
            {
                Debug.WriteLine(
                    $"{nameof(FillRegionHoles)}: no external contours " +
                    $"found for region {region.Index}; preserving original.");

                return region;
            }

            // ------------------------------------------------------------
            // Reconstruct the geometry in the original coordinate space.
            //
            // Every retrieved contour becomes a closed exterior outline.
            // Since internal contours were discarded, the resulting
            // geometry contains no holes.
            // ------------------------------------------------------------

            using SKPathBuilder builder = new();

            double area = 0.0;
            double perimeterLength = 0.0;

            float originX = left - Padding;
            float originY = top - Padding;

            for (int i = 0; i < contours.Size; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using VectorOfPoint contour = contours[i];

                if (contour.Size < 3)
                    continue;

                System.Drawing.Point[] points =
                    contour.ToArray();

                builder.MoveTo(
                    originX + points[0].X,
                    originY + points[0].Y);

                for (int j = 1; j < points.Length; j++)
                {
                    builder.LineTo(
                        originX + points[j].X,
                        originY + points[j].Y);
                }

                builder.Close();

                area += Math.Abs(
                    CvInvoke.ContourArea(contour));

                perimeterLength +=
                    CvInvoke.ArcLength(contour, true);
            }

            using SKPath filledGeometry =
                builder.Detach();

            // Preserve the original object if reconstruction failed.
            if (filledGeometry.IsEmpty)
            {
                return region;
            }

            // The reconstructed subpaths represent exterior components.
            // EvenOdd avoids depending on their winding directions.
            filledGeometry.FillType =
                SKPathFillType.EvenOdd;

            // ------------------------------------------------------------
            // Construct the replacement region.
            //
            // Preserve the source and user decision state. Do not promote
            // Proposed, Accepted, or Committed regions to another state.
            // ------------------------------------------------------------

            ImportRegion filledRegion =
                new(filledGeometry)
                {
                    Source = region.Source,
                    State = region.State,
                    Area = area,
                    PerimeterLength = perimeterLength,
                    ScaffoldPerimeterSupport =
                        region.ScaffoldPerimeterSupport
                };

            modified = true;

            Debug.WriteLine(
                $"{nameof(FillRegionHoles)}: region {region.Index} " +
                $"rebuilt with {contours.Size} exterior contour(s); " +
                $"area={area:F0}, perimeter={perimeterLength:F1}.");

            return filledRegion;
        }
    }
}
