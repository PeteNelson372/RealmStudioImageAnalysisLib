using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using Microsoft.ML.OnnxRuntime.Tensors;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;

using SKCanvas = SkiaSharp.SKCanvas;
using SKPaint = SkiaSharp.SKPaint;

namespace RealmStudioImageAnalysisLib
{
    public class ImportRegionAnalyzer
    {
        private readonly static string rootRealmStudioXDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments),
                "RealmStudioX");

        private readonly static string assetsDirectory =
            Path.Combine(
                rootRealmStudioXDirectory,
                "Assets");

        private readonly static string imageAnalysisDirectory =
            Path.Combine(
                assetsDirectory,
                "ImageAnalysis");

        private readonly static string encoderPath =
            Path.Combine(
                imageAnalysisDirectory,
                "mobile_sam_image_encoder.onnx");

        private readonly static string decoderPath =
            Path.Combine(
                imageAnalysisDirectory,
                "sam_mask_decoder_single.onnx");


        // ================================================================
        // MobileSAM landform extraction
        // ================================================================

        public static List<SKPath> ExtractLandforms(
            List<ImportRegion> importRegions,
            SKBitmap sourceBitmap)
        {
            List<ImportRegion> regions =
            [
                .. importRegions
                    .Where(r =>
                        r.State == ImportRegionState.Accepted)
            ];

            if (regions.Count == 0)
                return [];

            using var encoder =
                new MobileSamEncoder(
                    encoderPath);

            DenseTensor<float> embedding =
                encoder.Encode(sourceBitmap);

            using var decoder =
                new MobileSamDecoder(
                    decoderPath);

            List<SKPath> perimeters = [];

            foreach (ImportRegion region in regions)
            {
                SKPath? perimeter =
                    ExtractLandform(
                        region,
                        embedding,
                        encoder,
                        decoder,
                        sourceBitmap);

                if (perimeter != null)
                {
                    perimeters.Add(
                        perimeter);
                }
            }

            return perimeters;
        }


        private static SKPath? ExtractLandform(
            ImportRegion region,
            DenseTensor<float> embedding,
            MobileSamEncoder encoder,
            MobileSamDecoder decoder,
            SKBitmap sourceBitmap)
        {
            SKRect bounds = region.Bounds;

            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            //
            // Convert the accepted region's bounds from original-image
            // coordinates into MobileSAM's 1024-long-side coordinates.
            //
            float scale =
                1024.0f /
                Math.Max(sourceBitmap.Width, sourceBitmap.Height);

            float x0 = bounds.Left * scale;
            float y0 = bounds.Top * scale;
            float x1 = bounds.Right * scale;
            float y1 = bounds.Bottom * scale;

            //
            // Clamp to the actual encoder image dimensions.
            //
            x0 = Math.Clamp(x0, 0.0f, encoder.InputWidth);
            y0 = Math.Clamp(y0, 0.0f, encoder.InputHeight);

            x1 = Math.Clamp(x1, 0.0f, encoder.InputWidth);
            y1 = Math.Clamp(y1, 0.0f, encoder.InputHeight);

            //
            // Make sure the box remains valid.
            //
            if (x1 <= x0 || y1 <= y0)
                return null;

            Console.WriteLine(
                $"MobileSAM box prompt: " +
                $"original=({bounds.Left:0.##},{bounds.Top:0.##})-" +
                $"({bounds.Right:0.##},{bounds.Bottom:0.##})");

            Console.WriteLine(
                $"MobileSAM box prompt: " +
                $"encoder=({x0:0.##},{y0:0.##})-" +
                $"({x1:0.##},{y1:0.##})");

            SegmentationResult result =
                decoder.Predict(
                    embedding,
                    x0,
                    y0,
                    x1,
                    y1,
                    sourceBitmap.Width,
                    sourceBitmap.Height);

            Console.WriteLine(
                $"MobileSAM mask dimensions: " +
                $"[{string.Join(", ", result.MaskDimensions)}]");

            Console.WriteLine(
                $"MobileSAM mask values: {result.Masks.Length}");

            if (result.IoU.Length > 0)
            {
                Console.WriteLine(
                    $"MobileSAM IoU prediction: " +
                    $"{result.IoU[0]:0.######}");
            }

            return CreateLandformPerimeter(result);
        }


        private static void SaveMaskInput(
            float[] maskInput,
            string filename)
        {
            const int size = 256;

            using SKBitmap bitmap =
                new(
                    size,
                    size,
                    SKColorType.Gray8,
                    SKAlphaType.Opaque);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float value =
                        maskInput[
                            y * size + x];

                    byte intensity =
                        value > 0
                            ? (byte)255
                            : (byte)0;

                    bitmap.SetPixel(
                        x,
                        y,
                        new SKColor(
                            intensity,
                            intensity,
                            intensity));
                }
            }

            using SKImage image =
                SKImage.FromBitmap(bitmap);

            using SKData data =
                image.Encode(
                    SKEncodedImageFormat.Png,
                    100);

            using FileStream stream =
                File.OpenWrite(filename);

            data.SaveTo(stream);
        }


        private static SKPath? CreateLandformPerimeter(
            SegmentationResult result)
        {
            //
            // The single-mask decoder returns:
            //
            //     [1, 1, height, width]
            //
            if (result.MaskDimensions.Length != 4)
            {
                throw new InvalidOperationException(
                    "Unexpected MobileSAM mask dimensions: " +
                    $"[{string.Join(
                        ", ",
                        result.MaskDimensions)}]");
            }

            if (result.MaskDimensions[0] != 1 ||
                result.MaskDimensions[1] != 1)
            {
                throw new InvalidOperationException(
                    "The single-mask MobileSAM decoder " +
                    "returned unexpected dimensions: " +
                    $"[{string.Join(
                        ", ",
                        result.MaskDimensions)}]");
            }

            int height =
                result.MaskDimensions[2];

            int width =
                result.MaskDimensions[3];

            int maskSize =
                width * height;

            if (result.Masks.Length < maskSize)
            {
                throw new InvalidOperationException(
                    "MobileSAM returned a mask buffer " +
                    "smaller than expected.");
            }

            //
            // The decoder has already resized the mask to
            // orig_im_size. Therefore these coordinates are
            // already in source-image/map coordinates.
            //
            using var mask =
                new Image<Gray, byte>(
                    width,
                    height);

            const float threshold = 0.0f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value =
                        result.Masks[
                            y * width + x];

                    mask.Data[y, x, 0] =
                        value > threshold
                            ? (byte)255
                            : (byte)0;
                }
            }

            //
            // Find external contours.
            //
            using var contours =
                new VectorOfVectorOfPoint();

            using var hierarchy =
                new Mat();

            CvInvoke.FindContours(
                mask,
                contours,
                hierarchy,
                RetrType.External,
                ChainApproxMethod.ChainApproxSimple);

            if (contours.Size == 0)
                return null;

            //
            // Select the largest external contour.
            //
            int largestIndex = 0;
            double largestArea = 0.0;

            for (int i = 0;
                 i < contours.Size;
                 i++)
            {
                double area =
                    CvInvoke.ContourArea(
                        contours[i]);

                if (area > largestArea)
                {
                    largestArea = area;
                    largestIndex = i;
                }
            }

            if (largestArea <= 0)
                return null;

            VectorOfPoint contour =
                contours[largestIndex];

            Rectangle bounds =
                CvInvoke.BoundingRectangle(
                    contour);

            Debug.WriteLine(
                "Selected contour bounds: " +
                $"{bounds.X}, " +
                $"{bounds.Y}, " +
                $"{bounds.Width} x " +
                $"{bounds.Height}");

            Debug.WriteLine(
                "Selected contour area: " +
                $"{largestArea}");

            return CreateSKPathFromContour(
                contour);
        }


        private static SKPath CreateSKPathFromContour(
            VectorOfPoint contour)
        {
            System.Drawing.Point[] points =
                contour.ToArray();

            using SKPathBuilder builder =
                new();

            if (points.Length == 0)
                return builder.Detach();

            builder.MoveTo(
                points[0].X,
                points[0].Y);

            for (int i = 1;
                 i < points.Length;
                 i++)
            {
                builder.LineTo(
                    points[i].X,
                    points[i].Y);
            }

            builder.Close();

            return builder.Detach();
        }


        // ================================================================
        // MobileSAM mask prompt
        // ================================================================

        public static DenseTensor<float> CreateMaskPrompt(
            SKPath sourcePath,
            int originalWidth,
            int originalHeight,
            int encoderInputWidth,
            int encoderInputHeight)
        {
            ArgumentNullException.ThrowIfNull(
                sourcePath);

            if (originalWidth <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(originalWidth));

            if (originalHeight <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(originalHeight));

            if (encoderInputWidth <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(encoderInputWidth));

            if (encoderInputHeight <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(encoderInputHeight));

            const int maskSize = 256;

            //
            // Determine the scale used by the encoder.
            //
            double scale =
                Math.Min(
                    (double)encoderInputWidth /
                    originalWidth,

                    (double)encoderInputHeight /
                    originalHeight);

            int resizedWidth =
                (int)Math.Round(
                    originalWidth * scale);

            int resizedHeight =
                (int)Math.Round(
                    originalHeight * scale);

            //
            // Convert the resized image dimensions into
            // the decoder's 256x256 mask coordinate system.
            //
            int activeMaskWidth =
                (int)Math.Round(
                    resizedWidth *
                    (double)maskSize /
                    encoderInputWidth);

            int activeMaskHeight =
                (int)Math.Round(
                    resizedHeight *
                    (double)maskSize /
                    encoderInputHeight);

            activeMaskWidth =
                Math.Clamp(
                    activeMaskWidth,
                    1,
                    maskSize);

            activeMaskHeight =
                Math.Clamp(
                    activeMaskHeight,
                    1,
                    maskSize);

            //
            // Render the accepted/traced region at its
            // original image resolution.
            //
            using SKBitmap originalMask =
                new(
                    originalWidth,
                    originalHeight,
                    SKColorType.Gray8,
                    SKAlphaType.Opaque);

            originalMask.Erase(
                SKColors.Black);

            using (SKCanvas canvas =
                new(originalMask))
            using (SKPaint paint =
                new())
            {
                paint.Style =
                    SKPaintStyle.Fill;

                paint.Color =
                    SKColors.White;

                paint.IsAntialias =
                    true;

                //
                // Never modify the ImportRegion's actual
                // geometry.
                //
                using SKPath sourceMaskPath =
                    new(sourcePath);

                //
                // Close a copy using the current
                // SKPathBuilder API.
                //
                using SKPathBuilder builder =
                    new(sourceMaskPath);

                builder.Close();

                using SKPath closedMaskPath =
                    builder.Detach();

                canvas.DrawPath(
                    closedMaskPath,
                    paint);
            }

            //
            // Resize the source-resolution mask into
            // the active portion of the 256x256
            // MobileSAM mask coordinate system.
            //
            using SKBitmap resizedMask =
                new(
                    activeMaskWidth,
                    activeMaskHeight,
                    SKColorType.Gray8,
                    SKAlphaType.Opaque);

            resizedMask.Erase(
                SKColors.Black);

            using (SKCanvas canvas =
                new(resizedMask))
            {
                SKRect destination =
                    new(
                        0,
                        0,
                        activeMaskWidth,
                        activeMaskHeight);

                canvas.DrawBitmap(
                    originalMask,
                    destination,
                    new SKSamplingOptions(
                        SKFilterMode.Linear,
                        SKMipmapMode.None));
            }

            //
            // Construct the [1,1,256,256] tensor.
            //
            DenseTensor<float> maskInput =
                new(
                    new int[]
                    {
                        1,
                        1,
                        maskSize,
                        maskSize
                    });

            //
            // Copy the active mask into the upper-left
            // portion of the 256x256 tensor.
            //
            for (int y = 0;
                 y < activeMaskHeight;
                 y++)
            {
                for (int x = 0;
                     x < activeMaskWidth;
                     x++)
                {
                    SKColor pixel =
                        resizedMask.GetPixel(
                            x,
                            y);

                    maskInput[0, 0, y, x] =
                        pixel.Red > 127
                            ? 1.0f
                            : 0.0f;
                }
            }

            return maskInput;
        }
    }
}