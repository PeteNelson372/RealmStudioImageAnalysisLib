using Microsoft.ML.OnnxRuntime.Tensors;
using RealmStudioShapeRenderingLib;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class RunMobileSamSegmentation : IPerimeterStage
    {
        public string Id => "RunMobileSamSegmentation";

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

        private readonly static string decoderPath =
            Path.Combine(
                imageAnalysisDirectory,
                "sam_mask_decoder_single.onnx");

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ImportRegion> regions =
                data.Get<IReadOnlyList<ImportRegion>>("AcceptedImportRegions");

            DenseTensor<float> embedding =
                data.Get<DenseTensor<float>>("MobileSamEmbedding");

            int encoderInputWidth =
                data.Get<int>("MobileSamInputWidth");

            int encoderInputHeight =
                data.Get<int>("MobileSamInputHeight");

            List<SegmentationResult> results = [];

            if (regions.Count == 0)
            {
                data.Set("MobileSamSegmentationResults", results);

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress = 1.0,
                        Status =
                            "No accepted import regions to segment."
                    });

                return new PerimeterStageResult
                {
                    Succeeded = true
                };
            }

            using var decoder = new MobileSamDecoder(decoderPath);

            for (int i = 0; i < regions.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                ImportRegion region =
                    regions[i];

                SKRect bounds = region.Bounds;

                if (bounds.Width <= 0 ||
                    bounds.Height <= 0)
                {
                    throw new InvalidOperationException(
                        "An accepted import region has invalid bounds.");
                }

                //
                // Convert the accepted region's bounds from
                // original-image coordinates into MobileSAM's
                // 1024-long-side coordinates.
                //
                float scale =
                    1024.0f /
                    Math.Max(
                        data.OriginalBitmap.Width,
                        data.OriginalBitmap.Height);

                float x0 =
                    bounds.Left * scale;

                float y0 =
                    bounds.Top * scale;

                float x1 =
                    bounds.Right * scale;

                float y1 =
                    bounds.Bottom * scale;

                //
                // Clamp to the actual encoder image dimensions.
                //
                x0 =
                    Math.Clamp(
                        x0,
                        0.0f,
                        encoderInputWidth);

                y0 =
                    Math.Clamp(
                        y0,
                        0.0f,
                        encoderInputHeight);

                x1 =
                    Math.Clamp(
                        x1,
                        0.0f,
                        encoderInputWidth);

                y1 =
                    Math.Clamp(
                        y1,
                        0.0f,
                        encoderInputHeight);

                //
                // Make sure the box remains valid.
                //
                if (x1 <= x0 ||
                    y1 <= y0)
                {
                    throw new InvalidOperationException(
                        "An accepted import region produced an invalid " +
                        "MobileSAM box prompt.");
                }

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
                        data.OriginalBitmap.Width,
                        data.OriginalBitmap.Height);

                Console.WriteLine(
                    $"MobileSAM mask dimensions: " +
                    $"[{string.Join(
                        ", ",
                        result.MaskDimensions)}]");

                Console.WriteLine(
                    $"MobileSAM mask values: " +
                    $"{result.Masks.Length}");

                if (result.IoU.Length > 0)
                {
                    Console.WriteLine(
                        $"MobileSAM IoU prediction: " +
                        $"{result.IoU[0]:0.######}");
                }

                results.Add(result);

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress =
                            (double)(i + 1) /
                            regions.Count,
                        Status =
                            $"Segmented accepted region " +
                            $"{i + 1} of {regions.Count}."
                    });
            }

            data.Set(
                "MobileSamSegmentationResults",
                results);

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
