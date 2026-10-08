using Emgu.CV;
using Emgu.CV.CvEnum;
using SkiaSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class RemoveExternalMaterialStage : IPerimeterStage
    {
        private const int ExternalMaterialMinimumBoundaryPixels = 100;
        private const int ExternalMaterialMinimumComponentArea = 5000;
        private const int ExternalMaterialColorQuantization = 16;

        public string Id => "RemoveExternalMaterial";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            SKBitmap sourceBitmap =
                data.Get<SKBitmap>("BgraBitmap");

            using Mat externalMaterialMask =
                DetectExternalMaterialMask(sourceBitmap);

            SKBitmap resultBitmap =
                ReplaceExternalMaterialWithWhite(
                    sourceBitmap,
                    externalMaterialMask);

            data.Set(
                "ExternalMaterialRemovedBitmap",
                resultBitmap);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "External material removed."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
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

        private static int GetMaterialColorFamily(byte red, byte green, byte blue)
        {
            int r = red >> 4;
            int g = green >> 4;
            int b = blue >> 4;

            return r | (g << 4) | (b << 8);
        }
    }
}
