using Emgu.CV;
using Emgu.CV.CvEnum;
using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineRunner
    {
        public static PerimeterPipelineResult Run(PerimeterPipeline pipeline, SKBitmap bitmap, PerimeterAlgorithmContext context)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(context);

            return RunInternal(pipeline, bitmap, null, context);
        }

        public static PerimeterPipelineResult Run(PerimeterPipeline pipeline, SKBitmap bitmap,
            IReadOnlyList<ImportRegion> importRegions, PerimeterAlgorithmContext context)
        {
            ArgumentNullException.ThrowIfNull(importRegions);

            return RunInternal(pipeline, bitmap, importRegions, context);
        }

        private static PerimeterPipelineResult RunInternal(PerimeterPipeline pipeline, SKBitmap bitmap,
            IReadOnlyList<ImportRegion>? importRegions, PerimeterAlgorithmContext context)
        {
            ArgumentNullException.ThrowIfNull(pipeline);
            ArgumentNullException.ThrowIfNull(bitmap);
            ArgumentNullException.ThrowIfNull(context);

            var data = importRegions == null ? new PerimeterPipelineData(bitmap) : new PerimeterPipelineData(bitmap, importRegions);

            foreach (PerimeterPipelineStage pipelineStage in pipeline.Stages)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                PerimeterStageDefinition definition = pipelineStage.Definition;

                PerimeterStageContext stageContext =
                    new(
                        definition.Id,
                        context.Progress,
                        definition.InputArtifacts,
                        definition.OutputArtifact,
                        definition.Parameters,
                        definition.SaveOutput,
                        definition.OutputName,
                        context.CancellationToken);

                PerimeterStageResult result = pipelineStage.Stage.Process(data, stageContext);

                if (!result.Succeeded)
                {
                    return new PerimeterPipelineResult
                    {
                        PipelineName = pipeline.Name,

                        Succeeded = false,

                        ImportRegions = [],

                        RefinedPerimeters = [],

                        FailureReason = result.FailureReason
                    };
                }

                if (pipeline.Debug &&
                    stageContext.SaveOutput &&
                    !string.IsNullOrWhiteSpace(
                        stageContext.OutputName) &&
                    !string.IsNullOrWhiteSpace(
                        stageContext.OutputArtifact))
                {
                    object? artifact = data.Get<object>(stageContext.OutputArtifact);

                    if (artifact is SKBitmap bmp)
                    {
                        SaveDebugBitmap(bmp, stageContext.OutputName, context.OutputDirectory);
                    }
                    else if (artifact is Mat mat)
                    {
                        SaveMatBitmap(mat, stageContext.OutputName, context.OutputDirectory);
                    }
                }
            }

            if (pipeline.Type == PerimeterPipelineType.Extraction)
            {
                IReadOnlyList<ImportRegion> importRegionsResult = data.Get<IReadOnlyList<ImportRegion>>("ImportRegions");

                return new PerimeterPipelineResult
                {
                    PipelineName = pipeline.Name,

                    Succeeded = true,

                    ImportRegions = [.. importRegionsResult],

                    RefinedPerimeters = []
                };
            }

            if (pipeline.Type == PerimeterPipelineType.Refinement)
            {
                IReadOnlyList<SKPath> refinedPerimeters =
                    data.Get<IReadOnlyList<SKPath>>("RefinedPerimeters");

                return new PerimeterPipelineResult
                {
                    PipelineName = pipeline.Name,

                    Succeeded = true,

                    ImportRegions = [],

                    RefinedPerimeters = [.. refinedPerimeters]
                };
            }

            throw new InvalidOperationException(
                $"Unsupported perimeter pipeline type: " +
                $"{pipeline.Type}.");
        }

        private static void SaveDebugBitmap(SKBitmap bitmap, string outputName, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException(
                    "Pipeline debugging is enabled and a stage requested " +
                    "debug output, but no output directory was specified.");
            }

            Directory.CreateDirectory(outputDirectory);

            string outputFile = Path.Combine(outputDirectory, outputName);

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);

            using FileStream stream = File.Create(outputFile);
            encoded.SaveTo(stream);
        }

        private static void SaveMatBitmap(Mat mat, string outputName, string? outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new InvalidOperationException(
                    "Pipeline debugging is enabled and a stage requested " +
                    "debug output, but no output directory was specified.");
            }

            if (mat.IsEmpty)
                return;

            if (mat.Depth != DepthType.Cv8U ||
                (mat.NumberOfChannels != 1 &&
                 mat.NumberOfChannels != 4))
            {
                throw new InvalidOperationException(
                    "The Mat debug output must be an 8-bit single-channel " +
                    "or four-channel image.");
            }

            Directory.CreateDirectory(outputDirectory);

            string outputFile =
                Path.Combine(
                    outputDirectory,
                    outputName);

            SKColorType colorType =
                mat.NumberOfChannels == 1
                    ? SKColorType.Gray8
                    : SKColorType.Bgra8888;

            using SKBitmap bitmap =
                new(
                    mat.Width,
                    mat.Height,
                    colorType,
                    SKAlphaType.Opaque);

            int rowBytes =
                mat.Width * mat.NumberOfChannels;

            byte[] row = new byte[rowBytes];

            for (int y = 0; y < mat.Height; y++)
            {
                IntPtr source =
                    mat.DataPointer +
                    (y * mat.Step);

                Marshal.Copy(
                    source,
                    row,
                    0,
                    rowBytes);

                IntPtr destination =
                    bitmap.GetPixels() +
                    (y * bitmap.RowBytes);

                Marshal.Copy(
                    row,
                    0,
                    destination,
                    rowBytes);
            }

            using SKImage image =
                SKImage.FromBitmap(bitmap);

            using SKData encoded =
                image.Encode(
                    SKEncodedImageFormat.Png,
                    100);

            using FileStream stream =
                File.Create(outputFile);

            encoded.SaveTo(stream);
        }
    }
}

