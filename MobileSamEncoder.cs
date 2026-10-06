namespace RealmStudioImageAnalysisLib
{
    using Microsoft.ML.OnnxRuntime;
    using Microsoft.ML.OnnxRuntime.Tensors;
    using SkiaSharp;

    public sealed class MobileSamEncoder : IDisposable
    {
        private const int EncoderSize = 1024;

        private readonly InferenceSession _session;

        public int InputWidth { get; private set; }
        public int InputHeight { get; private set; }

        public MobileSamEncoder(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        public DenseTensor<float> Encode(SKBitmap original)
        {
            ArgumentNullException.ThrowIfNull(original);

            const int encoderSize = 1024;

            float scale = MathF.Min(
                (float)encoderSize / original.Width,
                (float)encoderSize / original.Height);

            int width = Math.Max(
                1,
                (int)MathF.Round(original.Width * scale));

            int height = Math.Max(
                1,
                (int)MathF.Round(original.Height * scale));

            InputWidth = width;
            InputHeight = height;

            using var resized = original.Resize(
                new SKImageInfo(width, height),
                SKSamplingOptions.Default);

            if (resized == null)
            {
                throw new InvalidOperationException(
                    "Unable to resize image.");
            }

            var input = new DenseTensor<float>(
                new[] { height, width, 3 });

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    SKColor pixel = resized.GetPixel(x, y);

                    input[y, x, 0] = pixel.Red;
                    input[y, x, 1] = pixel.Green;
                    input[y, x, 2] = pixel.Blue;
                }
            }

            using var results = _session.Run(
            [
                NamedOnnxValue.CreateFromTensor(
            "input_image",
            input)
            ]);

            var embedding = results
                .First(x => x.Name == "image_embeddings")
                .AsTensor<float>();

            return new DenseTensor<float>(
                embedding.ToArray(),
                [1, 256, 64, 64]);
        }

        public DenseTensor<float> Encode(string imagePath)
        {
            using var original = SKBitmap.Decode(imagePath);

            if (original == null)
            {
                throw new InvalidOperationException(
                    $"Unable to load image: {imagePath}");
            }

            return Encode(original);
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
