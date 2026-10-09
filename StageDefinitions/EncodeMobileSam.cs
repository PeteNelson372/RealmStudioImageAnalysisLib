using Microsoft.ML.OnnxRuntime.Tensors;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class EncodeMobileSam : IPerimeterStage
    {
        public string Id => "EncodeMobileSam";

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

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            using var encoder = new MobileSamEncoder(encoderPath);

            DenseTensor<float> embedding = encoder.Encode(data.OriginalBitmap);

            data.Set("MobileSamEmbedding", embedding);

            data.Set("MobileSamInputWidth", encoder.InputWidth);

            data.Set("MobileSamInputHeight", encoder.InputHeight);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "MobileSAM image encoding completed."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
