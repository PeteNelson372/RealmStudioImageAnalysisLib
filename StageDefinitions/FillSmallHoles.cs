using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib.StageDefinitions;

public sealed class FillSmallHoles : IPerimeterStage
{
    public string Id => "FillSmallHoles";

    // Maximum number of pixels in an enclosed black region to fill.
    private const int MaximumHoleAreaPixels = 250;

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

        int width = landMask.Width;
        int height = landMask.Height;
        int pixelCount = checked(width * height);

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 0.1,
            Status = "Identifying enclosed regions."
        });

        // Invert the mask:
        // original black ocean pixels become foreground;
        // original white land pixels become background.
        using Mat inverseMask = new();
        CvInvoke.BitwiseNot(landMask, inverseMask);

        // Emgu CV 5 connected-components API.
        using Mat labels = new();

        int componentCount = CvInvoke.ConnectedComponents(
            inverseMask,
            labels,
            LineType.EightConnected,
            DepthType.Cv32S);

        // Emgu CV 5 returns flat arrays when multiDimensional is false.
        int[] labelPixels = (int[])labels.GetData(false);

        if (labelPixels.Length != pixelCount)
        {
            throw new InvalidOperationException(
                $"{Id}: Unexpected connected-component label data size.");
        }

        int[] componentAreas = new int[componentCount];
        bool[] touchesBorder = new bool[componentCount];

        // Count the pixels in each black component.
        for (int i = 0; i < pixelCount; i++)
        {
            if ((i & 0xFFFF) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int label = labelPixels[i];

            // Label zero is the background of the inverted mask,
            // corresponding to the original white land.
            if (label == 0)
                continue;

            componentAreas[label]++;

            int x = i % width;
            int y = i / width;

            if (x == 0 || y == 0 ||
                x == width - 1 || y == height - 1)
            {
                touchesBorder[label] = true;
            }
        }

        // Identify which components are small enough to fill.
        // Components touching the image edge are exterior-connected
        // ocean and must remain black.
        bool[] fillComponent = new bool[componentCount];
        int filledHoleCount = 0;

        for (int label = 1; label < componentCount; label++)
        {
            if (!touchesBorder[label] &&
                componentAreas[label] > 0 &&
                componentAreas[label] <= MaximumHoleAreaPixels)
            {
                fillComponent[label] = true;
                filledHoleCount++;
            }
        }

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 0.6,
            Status = "Filling small enclosed regions."
        });

        // Retrieve the original pixels. Black is 0; white is 255.
        byte[] outputPixels = (byte[])landMask.GetData(false);

        if (outputPixels.Length != pixelCount)
        {
            throw new InvalidOperationException(
                $"{Id}: Unexpected land-mask data size.");
        }

        int filledPixelCount = 0;

        for (int i = 0; i < pixelCount; i++)
        {
            if ((i & 0xFFFF) == 0)
                context.CancellationToken.ThrowIfCancellationRequested();

            int label = labelPixels[i];

            if (label != 0 && fillComponent[label])
            {
                outputPixels[i] = 255;
                filledPixelCount++;
            }
        }

        // Create an independent output Mat without modifying the input.
        using Mat filledMask = landMask.Clone();
        filledMask.SetTo(outputPixels);

        data.Set(outputName, filledMask.Clone());

        Debug.WriteLine(
            $"{Id}: Filled {filledHoleCount} enclosed regions " +
            $"({filledPixelCount} pixels). " +
            $"Maximum area: {MaximumHoleAreaPixels} pixels.");

        context.Progress?.Report(new PerimeterAlgorithmProgress
        {
            Stage = Id,
            Progress = 1.0,
            Status = $"Filled {filledHoleCount} small enclosed regions."
        });

        return new PerimeterStageResult
        {
            Succeeded = true
        };
    }
}