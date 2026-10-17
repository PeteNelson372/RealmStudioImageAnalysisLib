using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Diagnostics;
using System.Drawing;

namespace RealmStudioImageAnalysisLib.StageDefinitions;

public sealed class RemoveThinLandFeatures : IPerimeterStage
{
    public string Id => "RemoveThinLandFeatures";

    private const int OpeningKernelSize = 7;
    private const int OpeningIterations = 1;

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
                $"{Id} requires exactly one input artifact.");
        }

        string inputName = context.GetInputArtifact(inputNames[0])
            ?? throw new InvalidOperationException(
                $"{Id}: Unable to resolve the input artifact.");

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
                $"{Id} requires an output artifact name.");

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 0.2,
            Status = "Removing narrow land connections and thin features."
        });

        // Create an elliptical structuring element.
        using Mat kernel = CvInvoke.GetStructuringElement(
            MorphShapes.Ellipse,
            new Size(OpeningKernelSize, OpeningKernelSize),
            new Point(-1, -1));

        // Morphological opening = erosion followed by dilation.
        // White is land; black is ocean.
        using Mat openedMask = new();

        CvInvoke.MorphologyEx(
            landMask,
            openedMask,
            MorphOp.Open,
            kernel,
            new Point(-1, -1),
            OpeningIterations,
            BorderType.Replicate,
            new MCvScalar(0));

        context.CancellationToken.ThrowIfCancellationRequested();

        // Store an independent output artifact; leave the input unchanged.
        data.Set(outputName, openedMask.Clone());

        int originalLandPixels = CvInvoke.CountNonZero(landMask);
        int remainingLandPixels = CvInvoke.CountNonZero(openedMask);

        Debug.WriteLine(
            $"{Id}: White pixels before opening: {originalLandPixels:N0}; " +
            $"after opening: {remainingLandPixels:N0}; " +
            $"removed: {originalLandPixels - remainingLandPixels:N0}; " +
            $"kernel: {OpeningKernelSize}x{OpeningKernelSize}.");

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 1.0,
            Status = "Removed thin land features."
        });

        return new PerimeterStageResult
        {
            Succeeded = true
        };
    }
}