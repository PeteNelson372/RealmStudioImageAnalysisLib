using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CleanLandMask : IPerimeterStage
    {
        public string Id => "CleanLandMask";

        private const int ClosingKernelSize = 9;

        private const int ClosingIterations = 1;

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

            Mat landMask =
                data.Get<Mat>(inputName);

            if (landMask.IsEmpty ||
                landMask.NumberOfChannels != 1 ||
                landMask.Depth != DepthType.Cv8U)
            {
                throw new InvalidOperationException(
                    $"{Id} requires a non-empty, 8-bit, single-channel mask.");
            }

            string outputName =
                context.OutputArtifact
                ?? throw new InvalidOperationException(
                    $"{Id} requires an output artifact.");

            // ------------------------------------------------------------
            // Count the original foreground pixels for diagnostics.
            // ------------------------------------------------------------

            int originalForegroundPixels =
                CvInvoke.CountNonZero(landMask);

            Debug.WriteLine(
                $"{Id}: input={landMask.Cols}x{landMask.Rows}, " +
                $"foreground pixels={originalForegroundPixels:N0}");

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Create the morphological closing kernel.
            //
            // Closing = dilation followed by erosion.
            //
            // This bridges narrow black interruptions while approximately
            // preserving the dimensions of the white land regions.
            // ------------------------------------------------------------

            using Mat kernel =
                CvInvoke.GetStructuringElement(
                    MorphShapes.Ellipse,
                    new System.Drawing.Size(
                        ClosingKernelSize,
                        ClosingKernelSize),
                    new System.Drawing.Point(-1, -1));

            using Mat cleanedMask = new();

            CvInvoke.MorphologyEx(
                landMask,
                cleanedMask,
                MorphOp.Close,
                kernel,
                new System.Drawing.Point(-1, -1),
                ClosingIterations,
                BorderType.Replicate,
                new MCvScalar(0));

            context.CancellationToken.ThrowIfCancellationRequested();

            // ------------------------------------------------------------
            // Report the results.
            // ------------------------------------------------------------

            int cleanedForegroundPixels =
                CvInvoke.CountNonZero(cleanedMask);

            Debug.WriteLine(
                $"{Id}: kernel={ClosingKernelSize}x{ClosingKernelSize}, " +
                $"iterations={ClosingIterations}, " +
                $"foreground pixels={cleanedForegroundPixels:N0}");

            // Store a separate Mat so the output remains valid after
            // the local cleanedMask is disposed.
            data.Set(
                outputName,
                cleanedMask.Clone());

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        "Cleaned the land mask using morphological closing."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}