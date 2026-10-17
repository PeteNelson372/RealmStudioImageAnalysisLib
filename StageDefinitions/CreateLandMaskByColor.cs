using Emgu.CV;
using Emgu.CV.CvEnum;
using SkiaSharp;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateLandMaskByColor : IPerimeterStage
    {
        public string Id => "CreateLandMaskByColor";

        // OpenCV's 8-bit Lab b channel uses 128 as approximately neutral.
        // Yellow/tan colors tend to be above this value; blue colors
        // tend to be below it.
        //
        // Initial value chosen for experimentation with Danhabn.
        private const double MinimumLabBThreshold = 130.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Resolve the input artifact.
            // ------------------------------------------------------------

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

            SKBitmap sourceBitmap =
                data.Get<SKBitmap>(inputName);

            if (sourceBitmap.Width <= 0 ||
                sourceBitmap.Height <= 0)
            {
                throw new InvalidOperationException(
                    $"{Id} received an empty source bitmap.");
            }

            string outputName =
                context.OutputArtifact
                ?? throw new InvalidOperationException(
                    $"{Id} requires an output artifact.");

            Debug.WriteLine(
                $"{Id}: source image = " +
                $"{sourceBitmap.Width}x{sourceBitmap.Height}");

            // ------------------------------------------------------------
            // Normalize the source to BGRA8888.
            //
            // Keep the original bitmap owned by the pipeline. We work
            // with a copy so that this stage doesn't modify its input.
            // ------------------------------------------------------------

            using SKBitmap bgraBitmap =
                sourceBitmap.Copy(SKColorType.Bgra8888)
                ?? throw new InvalidOperationException(
                    $"{Id} could not convert the source bitmap to BGRA8888.");

            context.CancellationToken.ThrowIfCancellationRequested();

            IntPtr pixelData = bgraBitmap.GetPixels();

            if (pixelData == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"{Id} could not access the source bitmap pixels.");
            }

            // The Mat wraps the bitmap memory, so bgraBitmap must remain
            // alive until the Mat has been disposed.
            using Mat bgra =
                new(
                    bgraBitmap.Height,
                    bgraBitmap.Width,
                    DepthType.Cv8U,
                    4,
                    pixelData,
                    bgraBitmap.RowBytes);

            // ------------------------------------------------------------
            // Convert BGRA to CIELAB.
            // ------------------------------------------------------------

            // Convert BGRA to BGR.
            using Mat bgr = new();

            CvInvoke.CvtColor(
                bgra,
                bgr,
                ColorConversion.Bgra2Bgr);

            // Convert BGR to CIELAB.
            using Mat lab = new();

            CvInvoke.CvtColor(
                bgr,
                lab,
                ColorConversion.Bgr2Lab);

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Extract the b channel.
            //
            // In OpenCV's 8-bit Lab representation, channel 2 is b:
            //
            //   Lower values = bluer
            //   Higher values = yellower
            //
            // Danhabn's pale blue ocean should fall below the threshold,
            // while much of its tan land should fall above it.
            // ------------------------------------------------------------

            using Mat labB = new();

            CvInvoke.ExtractChannel(
                lab,
                labB,
                2);

            // ------------------------------------------------------------
            // Create the binary land-color mask.
            //
            // White (255) = land-colored pixel
            // Black (0)   = pixel that doesn't pass the threshold
            //
            // This is color classification only. It does not attempt to
            // remove the frame, select a land component, or repair gaps.
            // Those operations belong in subsequent stages.
            // ------------------------------------------------------------

            using Mat landMask = new();

            CvInvoke.Threshold(
                labB,
                landMask,
                MinimumLabBThreshold,
                255.0,
                ThresholdType.Binary);

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Diagnostics.
            // ------------------------------------------------------------

            int foregroundPixels =
                CvInvoke.CountNonZero(landMask);

            long totalPixels =
                (long)landMask.Rows * landMask.Cols;

            double foregroundFraction =
                totalPixels > 0
                    ? (double)foregroundPixels / totalPixels
                    : 0.0;

            Debug.WriteLine(
                $"{Id}: Lab b threshold = {MinimumLabBThreshold:F1}");

            Debug.WriteLine(
                $"{Id}: land-colored pixels = " +
                $"{foregroundPixels:N0} / {totalPixels:N0} " +
                $"({foregroundFraction:P1})");

            // ------------------------------------------------------------
            // Publish the mask.
            //
            // Store a clone because the local mask is disposed when
            // Process returns. The output is an 8-bit, single-channel Mat.
            // ------------------------------------------------------------

            data.Set(
                outputName,
                landMask.Clone());

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Created land-color mask; " +
                        $"{foregroundFraction:P1} of pixels classified."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}