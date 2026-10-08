using RealmStudioShapeRenderingLib;
using SkiaSharp;

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

            using var data =
                importRegions == null
                    ? new PerimeterPipelineData(bitmap)
                    : new PerimeterPipelineData(
                        bitmap,
                        importRegions);

            foreach (PerimeterPipelineStage pipelineStage in pipeline.Stages)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                PerimeterStageDefinition definition =
                    pipelineStage.Definition;

                var stageContext =
                    new PerimeterStageContext
                    {
                        CancellationToken = context.CancellationToken,

                        Progress = context.Progress,

                        Parameters = definition.Parameters,

                        SaveOutput = definition.SaveOutput,

                        OutputArtifact = definition.OutputArtifact,

                        OutputName = definition.OutputName
                    };

                PerimeterStageResult stageResult =
                    pipelineStage.Stage.Process(
                        data,
                        stageContext);

                if (!stageResult.Succeeded)
                {
                    return new PerimeterPipelineResult
                    {
                        PipelineName = pipeline.Name,

                        Succeeded = false,

                        ImportRegions = [],

                        RefinedPerimeters = [],

                        FailureReason = stageResult.FailureReason
                    };
                }

                if (pipeline.Debug &&
                    stageContext.SaveOutput &&
                    !string.IsNullOrWhiteSpace(
                        stageContext.OutputName) &&
                    !string.IsNullOrWhiteSpace(
                        stageContext.OutputArtifact))
                {
                    if (data.TryGet<SKBitmap>(
                        stageContext.OutputArtifact,
                        out SKBitmap? debugBitmap))
                    {
                        if (debugBitmap != null)
                        {
                            SaveDebugBitmap(
                                debugBitmap,
                                stageContext.OutputName,
                                context.OutputDirectory);
                        }
                    }
                }
            }

            if (pipeline.Type == PerimeterPipelineType.Extraction)
            {
                IReadOnlyList<ImportRegion> importRegionsResult =
                    data.Get<IReadOnlyList<ImportRegion>>("ImportRegions");

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

        private static void SaveDebugBitmap(
            SKBitmap bitmap,
            string outputName,
            string? outputDirectory)
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
    }
}

