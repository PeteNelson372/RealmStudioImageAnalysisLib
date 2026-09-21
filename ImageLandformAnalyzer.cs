using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib;

public sealed class ImageLandformAnalyzer
{
    private const int MinimumRegionArea = 100;

    private const int MaterialColorQuantization = 16;
    private const int MaterialRaySpacing = 8;
    private const float MaterialRayDominance = 0.25f;
    private const float MaterialMinimumRaySupport = 0.15f;
    private const int MaterialMinimumRayPixels = 16;
    private const int MaterialClosingSize = 15;

    public ImageAnalysisResult Analyze(string filename)
    {
        using SKBitmap source =
            SKBitmap.Decode(filename)
            ?? throw new InvalidOperationException(
                "Unable to decode image.");

        using SKBitmap bitmap =
            source.Copy(SKColorType.Bgra8888)
            ?? throw new InvalidOperationException(
                "Unable to convert image to BGRA8888.");

        Debug.WriteLine(
            $"Image analysis: {bitmap.Width} x {bitmap.Height}");

        // ------------------------------------------------------------
        // PASS 1
        //
        // Detect the background from the image corners, then consider
        // everything sufficiently different from that background to be
        // foreground.
        // ------------------------------------------------------------

        SKColor backgroundColor =
            DetectBackgroundColor(bitmap);

        Debug.WriteLine(
            $"Detected background color: {backgroundColor}");

        using Mat backgroundMask =
            CreateBackgroundMask(
                bitmap,
                backgroundColor,
                20);

        SaveMaskDiagnostic(
            backgroundMask,
            "TracingBackgroundMask.png");

        List<DetectedRegion> firstPassRegions =
            ExtractRegions(
                backgroundMask,
                bitmap.Width,
                bitmap.Height,
                "First pass");

        SaveCandidateDiagnostic(
            bitmap,
            firstPassRegions,
            "TracingCandidates.png");

        Debug.WriteLine(
            $"First-pass candidates: {firstPassRegions.Count}");

        // ------------------------------------------------------------
        // PASS 2
        //
        // Look at the colors within the first-pass foreground and
        // determine which color families have broad support across
        // horizontal and vertical rays.
        // ------------------------------------------------------------

        using Mat materialMask =
            CreateSecondPassMaterialMask(
                bitmap,
                backgroundMask);

        SaveMaskDiagnostic(
            materialMask,
            "TracingMaterialMask.png");

        List<DetectedRegion> secondPassRegions =
            ExtractRegions(
                materialMask,
                bitmap.Width,
                bitmap.Height,
                "Second pass");

        SaveCandidateDiagnostic(
            bitmap,
            secondPassRegions,
            "TracingCandidatesSecondPass.png");

        Debug.WriteLine(
            $"Second-pass candidates: {secondPassRegions.Count}");

        // For now, use the second-pass candidates as the result.
        ImageAnalysisResult result = new()
        {
            ImageWidth = bitmap.Width,
            ImageHeight = bitmap.Height
        };

        result.Regions.AddRange(secondPassRegions);

        return result;
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

        using SKBitmap bitmap = new(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);

        byte[] bitmapPixels =
            new byte[width * height * 4];

        for (int i = 0; i < maskPixels.Length; i++)
        {
            byte value = maskPixels[i];

            int offset = i * 4;

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

        using SKImage image =
            SKImage.FromBitmap(bitmap);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        using FileStream stream =
            File.Create(filename);

        data.SaveTo(stream);
    }

    private static SKColor DetectBackgroundColor(
    SKBitmap bitmap)
    {
        const int sampleSize = 20;
        const int quantization = 8;

        var samples = new List<SKColor>();

        AddCornerSamples(
            bitmap,
            0,
            0,
            sampleSize,
            samples);

        AddCornerSamples(
            bitmap,
            bitmap.Width - sampleSize,
            0,
            sampleSize,
            samples);

        AddCornerSamples(
            bitmap,
            0,
            bitmap.Height - sampleSize,
            sampleSize,
            samples);

        AddCornerSamples(
            bitmap,
            bitmap.Width - sampleSize,
            bitmap.Height - sampleSize,
            sampleSize,
            samples);

        var buckets =
            new Dictionary<(int R, int G, int B, int A), List<SKColor>>();

        foreach (SKColor color in samples)
        {
            var key =
                (
                    Quantize(color.Red, quantization),
                    Quantize(color.Green, quantization),
                    Quantize(color.Blue, quantization),
                    Quantize(color.Alpha, quantization)
                );

            if (!buckets.TryGetValue(
                    key,
                    out List<SKColor>? bucket))
            {
                bucket = [];
                buckets.Add(key, bucket);
            }

            bucket.Add(color);
        }

        List<SKColor> largestBucket =
            buckets
                .Values
                .OrderByDescending(b => b.Count)
                .First();

        int red =
            (int)Math.Round(
                largestBucket.Average(c => c.Red));

        int green =
            (int)Math.Round(
                largestBucket.Average(c => c.Green));

        int blue =
            (int)Math.Round(
                largestBucket.Average(c => c.Blue));

        int alpha =
            (int)Math.Round(
                largestBucket.Average(c => c.Alpha));

        Debug.WriteLine(
            $"Background samples: {samples.Count}");

        Debug.WriteLine(
            $"Background bucket size: " +
            $"{largestBucket.Count}");

        return new SKColor(
            (byte)red,
            (byte)green,
            (byte)blue,
            (byte)alpha);
    }

    private static Mat CreateSecondPassMaterialMask(
    SKBitmap bitmap,
    Mat backgroundMask)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        Debug.WriteLine(
            "Creating second-pass material mask...");

        byte[] sourcePixels =
            new byte[bitmap.RowBytes * height];

        Marshal.Copy(
            bitmap.GetPixels(),
            sourcePixels,
            0,
            sourcePixels.Length);

        byte[] foregroundPixels =
            new byte[width * height];

        Marshal.Copy(
            backgroundMask.DataPointer,
            foregroundPixels,
            0,
            foregroundPixels.Length);

        // ------------------------------------------------------------
        // Each quantized RGB color becomes a "material family".
        //
        // With 16 levels per channel:
        //
        //   R: 0..15
        //   G: 0..15
        //   B: 0..15
        //
        // This gives us 4096 possible color families.
        // ------------------------------------------------------------

        const int familyCount =
            MaterialColorQuantization *
            MaterialColorQuantization *
            MaterialColorQuantization;

        int[] horizontalSupport =
            new int[familyCount];

        int[] verticalSupport =
            new int[familyCount];

        int horizontalRayCount = 0;
        int verticalRayCount = 0;

        // ------------------------------------------------------------
        // HORIZONTAL RAYS
        // ------------------------------------------------------------

        for (int y = 0;
             y < height;
             y += MaterialRaySpacing)
        {
            int[] counts =
                new int[familyCount];

            int foregroundCount = 0;

            int rowOffset =
                y * bitmap.RowBytes;

            int maskOffset =
                y * width;

            for (int x = 0; x < width; x++)
            {
                if (foregroundPixels[maskOffset + x] == 0)
                    continue;

                int pixelOffset =
                    rowOffset + x * 4;

                byte blue =
                    sourcePixels[pixelOffset + 0];

                byte green =
                    sourcePixels[pixelOffset + 1];

                byte red =
                    sourcePixels[pixelOffset + 2];

                int family =
                    GetMaterialColorFamily(
                        red,
                        green,
                        blue);

                counts[family]++;
                foregroundCount++;
            }

            if (foregroundCount < MaterialMinimumRayPixels)
                continue;

            horizontalRayCount++;

            int dominantCount = 0;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > dominantCount)
                    dominantCount = counts[i];
            }

            if (dominantCount == 0)
                continue;

            float minimumCount =
                dominantCount *
                MaterialRayDominance;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] >= minimumCount)
                    horizontalSupport[i]++;
            }
        }

        // ------------------------------------------------------------
        // VERTICAL RAYS
        // ------------------------------------------------------------

        for (int x = 0;
             x < width;
             x += MaterialRaySpacing)
        {
            int[] counts =
                new int[familyCount];

            int foregroundCount = 0;

            for (int y = 0; y < height; y++)
            {
                int maskOffset =
                    y * width + x;

                if (foregroundPixels[maskOffset] == 0)
                    continue;

                int pixelOffset =
                    y * bitmap.RowBytes + x * 4;

                byte blue =
                    sourcePixels[pixelOffset + 0];

                byte green =
                    sourcePixels[pixelOffset + 1];

                byte red =
                    sourcePixels[pixelOffset + 2];

                int family =
                    GetMaterialColorFamily(
                        red,
                        green,
                        blue);

                counts[family]++;
                foregroundCount++;
            }

            if (foregroundCount < MaterialMinimumRayPixels)
                continue;

            verticalRayCount++;

            int dominantCount = 0;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] > dominantCount)
                    dominantCount = counts[i];
            }

            if (dominantCount == 0)
                continue;

            float minimumCount =
                dominantCount *
                MaterialRayDominance;

            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] >= minimumCount)
                    verticalSupport[i]++;
            }
        }

        Debug.WriteLine(
            $"Horizontal rays: {horizontalRayCount}");

        Debug.WriteLine(
            $"Vertical rays: {verticalRayCount}");

        // ------------------------------------------------------------
        // Determine which color families have broad support.
        //
        // Requiring support in BOTH directions is intentional.
        // A map label, lake, river, or isolated feature may occupy
        // quite a few pixels, but generally won't have the same
        // broad horizontal AND vertical support as the main land
        // material.
        // ------------------------------------------------------------

        bool[] acceptedFamilies =
            new bool[familyCount];

        int acceptedCount = 0;

        for (int i = 0; i < familyCount; i++)
        {
            bool horizontalAccepted =
                horizontalRayCount > 0 &&
                horizontalSupport[i] >=
                horizontalRayCount *
                MaterialMinimumRaySupport;

            bool verticalAccepted =
                verticalRayCount > 0 &&
                verticalSupport[i] >=
                verticalRayCount *
                MaterialMinimumRaySupport;

            if (horizontalAccepted &&
                verticalAccepted)
            {
                acceptedFamilies[i] = true;
                acceptedCount++;
            }
        }

        Debug.WriteLine(
            $"Accepted material families: {acceptedCount}");

        // ------------------------------------------------------------
        // Build the material mask.
        //
        // A pixel must satisfy BOTH:
        //
        //   1. It was foreground according to pass 1.
        //   2. Its color belongs to an accepted material family.
        // ------------------------------------------------------------

        byte[] materialPixels =
            new byte[width * height];

        int foregroundPixelCount = 0;
        int materialPixelCount = 0;

        for (int y = 0; y < height; y++)
        {
            int rowOffset =
                y * bitmap.RowBytes;

            int maskOffset =
                y * width;

            for (int x = 0; x < width; x++)
            {
                if (foregroundPixels[maskOffset + x] == 0)
                    continue;

                foregroundPixelCount++;

                int pixelOffset =
                    rowOffset + x * 4;

                byte blue =
                    sourcePixels[pixelOffset + 0];

                byte green =
                    sourcePixels[pixelOffset + 1];

                byte red =
                    sourcePixels[pixelOffset + 2];

                int family =
                    GetMaterialColorFamily(
                        red,
                        green,
                        blue);

                if (!acceptedFamilies[family])
                    continue;

                materialPixels[maskOffset + x] = 255;
                materialPixelCount++;
            }
        }

        Debug.WriteLine(
            $"First-pass foreground pixels: " +
            $"{foregroundPixelCount}");

        Debug.WriteLine(
            $"Second-pass material pixels: " +
            $"{materialPixelCount}");

        Mat materialMask =
            new(
                height,
                width,
                DepthType.Cv8U,
                1);

        Marshal.Copy(
            materialPixels,
            0,
            materialMask.DataPointer,
            materialPixels.Length);

        // ------------------------------------------------------------
        // Close small gaps between regions of the same material.
        //
        // This is deliberately modest. We don't want morphology
        // turning rivers, lakes, or separate islands into one huge
        // blob.
        // ------------------------------------------------------------

        if (MaterialClosingSize > 1)
        {
            int kernelSize =
                MaterialClosingSize;

            if ((kernelSize & 1) == 0)
                kernelSize++;

            using Mat kernel =
                CvInvoke.GetStructuringElement(
                    MorphShapes.Rectangle,
                    new System.Drawing.Size(
                        kernelSize,
                        kernelSize),
                    new System.Drawing.Point(-1, -1));

            Mat closedMask =
                new(
                    height,
                    width,
                    DepthType.Cv8U,
                    1);

            CvInvoke.MorphologyEx(
                materialMask,
                closedMask,
                MorphOp.Close,
                kernel,
                new System.Drawing.Point(-1, -1),
                1,
                BorderType.Constant,
                new MCvScalar(0));

            materialMask.Dispose();

            materialMask = closedMask;
        }

        return materialMask;
    }

    private static int GetMaterialColorFamily(
        byte red,
        byte green,
        byte blue)
    {
        int r =
            red >> 4;

        int g =
            green >> 4;

        int b =
            blue >> 4;

        return
            r |
            (g << 4) |
            (b << 8);
    }

    private static List<DetectedRegion> ExtractRegions(
        Mat mask,
        int imageWidth,
        int imageHeight,
        string passName)
    {
        List<DetectedRegion> regions = [];

        using VectorOfVectorOfPoint contours =
            new();

        CvInvoke.FindContours(
            mask,
            contours,
            null,
            RetrType.External,
            ChainApproxMethod.ChainApproxSimple);

        Debug.WriteLine(
            $"{passName}: external contours found = " +
            $"{contours.Size}");

        for (int i = 0; i < contours.Size; i++)
        {
            using VectorOfPoint contour =
                contours[i];

            double area =
                CvInvoke.ContourArea(contour);

            if (area < 100.0)
                continue;

            System.Drawing.Rectangle bounds =
                CvInvoke.BoundingRectangle(contour);

            Point[] points =
                contour.ToArray();

            if (points.Length < 3)
                continue;

            using VectorOfPoint approximated =
                new();

            CvInvoke.ApproxPolyDP(
                contour,
                approximated,
                2.0,
                true);

            Point[] approximatePoints =
                approximated.ToArray();

            if (approximatePoints.Length < 3)
                continue;

            SKPath boundary =
                CreateBoundaryPath(
                    approximatePoints);

            SKPoint centroid =
                CalculateCentroid(
                    approximatePoints);

            DetectedRegion region = new()
            {
                Id = regions.Count + 1,
                Area = (int)Math.Round(area),
                Bounds = new SKRect(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right,
                    bounds.Bottom),
                Centroid = centroid,
                Boundary = boundary
            };

            regions.Add(region);

            Debug.WriteLine(
                $"{passName}: " +
                $"Candidate {region.Id}: " +
                $"Area={region.Area}, " +
                $"Bounds={region.Bounds}, " +
                $"Points={approximatePoints.Length}, " +
                $"Centroid={region.Centroid}");
        }

        // Largest regions first.
        regions.Sort(
            (a, b) =>
                b.Area.CompareTo(a.Area));

        // Reassign IDs after sorting.
        for (int i = 0; i < regions.Count; i++)
            regions[i].Id = i + 1;

        return regions;
    }

    private static SKPoint CalculateCentroid(
    Point[] points)
    {
        float x = 0;
        float y = 0;

        foreach (Point point in points)
        {
            x += point.X;
            y += point.Y;
        }

        float count = points.Length;

        return new SKPoint(
            x / count,
            y / count);
    }

    private static SKPath CreateBoundaryPath(
    Point[] points)
    {
        SKPathBuilder builder =
            new();

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

    private static void AddCornerSamples(
        SKBitmap bitmap,
        int left,
        int top,
        int size,
        List<SKColor> samples)
    {
        int right =
            Math.Min(
                left + size,
                bitmap.Width);

        int bottom =
            Math.Min(
                top + size,
                bitmap.Height);

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                samples.Add(
                    bitmap.GetPixel(x, y));
            }
        }
    }

    private static int Quantize(
        byte value,
        int step)
    {
        return value / step;
    }

    private static Mat CreateBackgroundMask(
    SKBitmap bitmap,
    SKColor backgroundColor,
    int tolerance)
    {
        Mat mask = new(
            bitmap.Height,
            bitmap.Width,
            DepthType.Cv8U,
            1);

        int pixelCount =
            bitmap.Width * bitmap.Height;

        byte[] pixels =
            new byte[pixelCount];

        for (int y = 0; y < bitmap.Height; y++)
        {
            int rowOffset =
                y * bitmap.Width;

            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color =
                    bitmap.GetPixel(x, y);

                int dr =
                    color.Red -
                    backgroundColor.Red;

                int dg =
                    color.Green -
                    backgroundColor.Green;

                int db =
                    color.Blue -
                    backgroundColor.Blue;

                double distance =
                    Math.Sqrt(
                        dr * dr +
                        dg * dg +
                        db * db);

                pixels[rowOffset + x] =
                    distance > tolerance
                        ? (byte)255
                        : (byte)0;
            }
        }

        Marshal.Copy(
            pixels,
            0,
            mask.DataPointer,
            pixels.Length);

        return mask;
    }

    private sealed class ContourCandidate
    {
        public int OriginalIndex { get; init; }

        public double Area { get; init; }

        public Rectangle Bounds { get; init; }
    }

    private static void SaveCandidateDiagnostic(
        SKBitmap source,
        IReadOnlyList<DetectedRegion> regions,
        string filename)
    {
        using SKBitmap diagnostic = new(
            source.Width,
            source.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);

        using SKCanvas canvas =
            new(diagnostic);

        canvas.DrawBitmap(
            source,
            0,
            0,
            SKSamplingOptions.Default);

        SKColor[] outlineColors =
        [
            SKColors.Red,
        SKColors.Blue,
        SKColors.Lime,
        SKColors.Magenta,
        SKColors.Cyan,
        SKColors.Orange,
        SKColors.Yellow
        ];

        using SKPaint outlinePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.0f,
            IsAntialias = true
        };

        using SKFont font = new()
        {
            Size = 14
        };

        using SKPaint textPaint = new()
        {
            Style = SKPaintStyle.Fill,
            Color = SKColors.Black,
            IsAntialias = true
        };

        foreach (DetectedRegion region in regions)
        {
            outlinePaint.Color =
                outlineColors[
                    (region.Id - 1) %
                    outlineColors.Length];

            canvas.DrawPath(
                region.Boundary,
                outlinePaint);

            canvas.DrawText(
                region.Id.ToString(),
                region.Centroid,
                SKTextAlign.Center,
                font,
                textPaint);
        }

        using SKImage image =
            SKImage.FromBitmap(diagnostic);

        using SKData data =
            image.Encode(
                SKEncodedImageFormat.Png,
                100);

        using FileStream stream =
            File.Create(filename);

        data.SaveTo(stream);
    }

    private static void TestApproximation(
        VectorOfPoint contour,
        double epsilon)
    {
        using VectorOfPoint approximated = new();

        CvInvoke.ApproxPolyDP(
            contour,
            approximated,
            epsilon,
            true);

        System.Diagnostics.Debug.WriteLine(
            $"ApproxPolyDP epsilon={epsilon}: " +
            $"points={approximated.Size}");
    }

    private static string ConvertContourToSvg(VectorOfPoint contour)
    {
        Point[] points = contour.ToArray();

        if (points.Length == 0)
            return string.Empty;

        using SKPathBuilder pathBuilder = new();

        pathBuilder.MoveTo(points[0].X, points[0].Y);

        for (int i = 1; i < points.Length; i++)
        {
            pathBuilder.LineTo(points[i].X, points[i].Y);
        }

        pathBuilder.Close();

        SKPath path = pathBuilder.Snapshot();
        pathBuilder.Detach();
        pathBuilder.Dispose();

        return path.ToSvgPathData();
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

    private static void TestConnectedComponents()
    {
        using Mat image = new(
            200,
            300,
            DepthType.Cv8U,
            1);

        image.SetTo(new Emgu.CV.Structure.MCvScalar(0));

        CvInvoke.Rectangle(
            image,
            new System.Drawing.Rectangle(20, 20, 60, 40),
            new Emgu.CV.Structure.MCvScalar(255),
            -1);

        CvInvoke.Rectangle(
            image,
            new System.Drawing.Rectangle(150, 30, 80, 50),
            new Emgu.CV.Structure.MCvScalar(255),
            -1);

        CvInvoke.Rectangle(
            image,
            new System.Drawing.Rectangle(80, 120, 100, 40),
            new Emgu.CV.Structure.MCvScalar(255),
            -1);

        using Mat labels = new();
        using Mat stats = new();
        using Mat centroids = new();

        int count = CvInvoke.ConnectedComponentsWithStats(
            image,
            labels,
            stats,
            centroids,
            LineType.EightConnected,
            DepthType.Cv32S);

        System.Diagnostics.Debug.WriteLine(
            $"Connected components: {count}");

        for (int i = 0; i < count; i++)
        {
            int left = ReadInt32(stats, i, 0);
            int top = ReadInt32(stats, i, 1);
            int width = ReadInt32(stats, i, 2);
            int height = ReadInt32(stats, i, 3);
            int area = ReadInt32(stats, i, 4);

            double centroidX = ReadDouble(centroids, i, 0);
            double centroidY = ReadDouble(centroids, i, 1);

            System.Diagnostics.Debug.WriteLine(
                $"Component {i}: " +
                $"Left={left}, Top={top}, " +
                $"Width={width}, Height={height}, " +
                $"Area={area}, " +
                $"Centroid=({centroidX:F1}, {centroidY:F1})");
        }
    }

    private static int ReadInt32(Mat mat, int row, int column)
    {
        int offset =
            (row * mat.Cols + column) * sizeof(int);

        return Marshal.ReadInt32(
            IntPtr.Add(mat.DataPointer, offset));
    }

    private static double ReadDouble(Mat mat, int row, int column)
    {
        int offset =
            (row * mat.Cols + column) * sizeof(double);

        long bits = Marshal.ReadInt64(
            IntPtr.Add(mat.DataPointer, offset));

        return BitConverter.Int64BitsToDouble(bits);
    }


}
