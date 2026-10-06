using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamDecoder : IDisposable
    {
        private readonly InferenceSession _session;

        public MobileSamDecoder(string modelPath)
        {
            _session = new InferenceSession(modelPath);
        }

        /// <summary>
        /// Runs MobileSAM using a coarse mask prompt and no user point prompt.
        /// </summary>
        public SegmentationResult Predict(
            DenseTensor<float> embedding,
            float[] maskInput,
            int originalWidth,
            int originalHeight)
        {
            ArgumentNullException.ThrowIfNull(embedding);
            ArgumentNullException.ThrowIfNull(maskInput);

            const int maskSize = 256;

            if (maskInput.Length !=
                maskSize * maskSize)
            {
                throw new ArgumentException(
                    $"Mask input must contain exactly " +
                    $"{maskSize * maskSize} values.",
                    nameof(maskInput));
            }

            // ------------------------------------------------------------
            // No point prompt.
            //
            // SAM/MobileSAM represents the absence of a point prompt
            // with a single padding point whose label is -1.
            // ------------------------------------------------------------

            DenseTensor<float> pointCoords =
                new(
                    new float[]
                    {
                        0.0f,
                        0.0f
                    },
                    new[] { 1, 1, 2 });

            DenseTensor<float> pointLabels =
                new(
                    new float[]
                    {
                        -1.0f
                    },
                    new[] { 1, 1 });

            // ------------------------------------------------------------
            // Coarse mask prompt.
            // ------------------------------------------------------------

            DenseTensor<float> maskTensor =
                new(
                    maskInput,
                    new[] { 1, 1, maskSize, maskSize });

            DenseTensor<float> hasMaskInput =
                new(
                    new float[]
                    {
                        1.0f
                    },
                    new[] { 1 });

            // ------------------------------------------------------------
            // Original image dimensions.
            //
            // MobileSAM expects:
            //
            //     [height, width]
            // ------------------------------------------------------------

            DenseTensor<float> originalImageSize =
                new(
                    new float[]
                    {
                        originalHeight,
                        originalWidth
                    },
                    new[] { 2 });

            // ------------------------------------------------------------
            // Run decoder.
            // ------------------------------------------------------------

            var inputs =
                new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(
                        "image_embeddings",
                        embedding),

                    NamedOnnxValue.CreateFromTensor(
                        "point_coords",
                        pointCoords),

                    NamedOnnxValue.CreateFromTensor(
                        "point_labels",
                        pointLabels),

                    NamedOnnxValue.CreateFromTensor(
                        "mask_input",
                        maskTensor),

                    NamedOnnxValue.CreateFromTensor(
                        "has_mask_input",
                        hasMaskInput),

                    NamedOnnxValue.CreateFromTensor(
                        "orig_im_size",
                        originalImageSize)
                };

            using IDisposableReadOnlyCollection<
                DisposableNamedOnnxValue> outputs =
                _session.Run(inputs);

            // ------------------------------------------------------------
            // Read outputs.
            // ------------------------------------------------------------

            DenseTensor<float> masks =
                outputs
                    .First(x =>
                        x.Name == "masks")
                    .AsTensor<float>()
                    .ToDenseTensor();

            DenseTensor<float> iouPredictions =
                outputs
                    .First(x =>
                        x.Name == "iou_predictions")
                    .AsTensor<float>()
                    .ToDenseTensor();

            DenseTensor<float> lowResMasks =
                outputs
                    .First(x =>
                        x.Name == "low_res_masks")
                    .AsTensor<float>()
                    .ToDenseTensor();

            return new SegmentationResult
            {
                Masks =
                    masks.ToArray(),

                MaskDimensions =
                    masks.Dimensions.ToArray(),

                IoU =
                    iouPredictions.ToArray(),

                LowResMasks =
                    lowResMasks.ToArray(),

                LowResDimensions =
                    lowResMasks.Dimensions.ToArray()
            };
        }

        public SegmentationResult Predict(
            DenseTensor<float> imageEmbedding,
            float x0,
            float y0,
            float x1,
            float y1,
            int originalWidth,
            int originalHeight)
        {
            ArgumentNullException.ThrowIfNull(imageEmbedding);

            const int maskSize = 256;

            // Box prompt:
            //
            // point 0 = top-left     -> label 2
            // point 1 = bottom-right -> label 3
            //
            // Coordinates must already be in the MobileSAM
            // 1024-long-side coordinate system.
            DenseTensor<float> pointCoords =
                new(
                    new float[]
                    {
                x0, y0,
                x1, y1
                    },
                    new[] { 1, 2, 2 });

            DenseTensor<float> pointLabels =
                new(
                    new float[]
                    {
                2.0f,
                3.0f
                    },
                    new[] { 1, 2 });

            // No previous mask is being supplied.
            //
            // The ONNX decoder requires mask_input even when
            // has_mask_input is zero.
            DenseTensor<float> maskInput =
                new(
                    new float[maskSize * maskSize],
                    new[] { 1, 1, maskSize, maskSize });

            DenseTensor<float> hasMaskInput =
                new(
                    new float[] { 0.0f },
                    new[] { 1 });

            // ONNX expects [height, width].
            DenseTensor<float> originalImageSize =
                new(
                    new float[]
                    {
                originalHeight,
                originalWidth
                    },
                    new[] { 2 });

            var inputs = new[]
            {
        NamedOnnxValue.CreateFromTensor(
            "image_embeddings",
            imageEmbedding),

        NamedOnnxValue.CreateFromTensor(
            "point_coords",
            pointCoords),

        NamedOnnxValue.CreateFromTensor(
            "point_labels",
            pointLabels),

        NamedOnnxValue.CreateFromTensor(
            "mask_input",
            maskInput),

        NamedOnnxValue.CreateFromTensor(
            "has_mask_input",
            hasMaskInput),

        NamedOnnxValue.CreateFromTensor(
            "orig_im_size",
            originalImageSize)
    };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                _session.Run(inputs);

            DenseTensor<float> masks =
                outputs
                    .First(x => x.Name == "masks")
                    .AsTensor<float>()
                    .ToDenseTensor();

            DenseTensor<float> iouPredictions =
                outputs
                    .First(x => x.Name == "iou_predictions")
                    .AsTensor<float>()
                    .ToDenseTensor();

            DenseTensor<float> lowResMasks =
                outputs
                    .First(x => x.Name == "low_res_masks")
                    .AsTensor<float>()
                    .ToDenseTensor();

            Console.WriteLine(
                $"MobileSAM mask dimensions: " +
                $"[{string.Join(", ", masks.Dimensions.ToArray())}]");

            Console.WriteLine(
                $"MobileSAM mask values: {masks.Length}");

            return new SegmentationResult
            {
                Masks = masks.ToArray(),
                MaskDimensions = masks.Dimensions.ToArray(),

                IoU = iouPredictions.ToArray(),

                LowResMasks = lowResMasks.ToArray(),
                LowResDimensions = lowResMasks.Dimensions.ToArray()
            };
        }

        /// <summary>
        /// Runs MobileSAM using explicit positive/negative point prompts.
        /// This is the point-prompt version used by the earlier experiments.
        /// </summary>
        public SegmentationResult Predict(
            DenseTensor<float> imageEmbedding,
            IReadOnlyList<SamPoint> points,
            int originalWidth,
            int originalHeight)
        {
            ArgumentNullException.ThrowIfNull(imageEmbedding);
            ArgumentNullException.ThrowIfNull(points);

            int pointCount =
                points.Count;

            var pointCoords =
                new DenseTensor<float>(
                    new[] { 1, pointCount, 2 });

            var pointLabels =
                new DenseTensor<float>(
                    new[] { 1, pointCount });

            for (int i = 0; i < pointCount; i++)
            {
                pointCoords[0, i, 0] =
                    points[i].X;

                pointCoords[0, i, 1] =
                    points[i].Y;

                pointLabels[0, i] =
                    points[i].IsPositive
                        ? 1.0f
                        : 0.0f;
            }

            // ------------------------------------------------------------
            // No mask prompt for the point-prompt overload.
            // ------------------------------------------------------------

            var maskInput =
                new DenseTensor<float>(
                    new[] { 1, 1, 256, 256 });

            var hasMaskInput =
                new DenseTensor<float>(
                    new[] { 1 });

            hasMaskInput[0] = 0.0f;

            // ------------------------------------------------------------
            // Original image dimensions.
            // ------------------------------------------------------------

            var origImageSize =
                new DenseTensor<float>(
                    new[] { 2 });

            origImageSize[0] =
                originalHeight;

            origImageSize[1] =
                originalWidth;

            // ------------------------------------------------------------
            // Run decoder.
            // ------------------------------------------------------------

            using var results =
                _session.Run(
                [
                    NamedOnnxValue.CreateFromTensor(
                        "image_embeddings",
                        imageEmbedding),

                    NamedOnnxValue.CreateFromTensor(
                        "point_coords",
                        pointCoords),

                    NamedOnnxValue.CreateFromTensor(
                        "point_labels",
                        pointLabels),

                    NamedOnnxValue.CreateFromTensor(
                        "mask_input",
                        maskInput),

                    NamedOnnxValue.CreateFromTensor(
                        "has_mask_input",
                        hasMaskInput),

                    NamedOnnxValue.CreateFromTensor(
                        "orig_im_size",
                        origImageSize)
                ]);

            var masks =
                results
                    .First(x =>
                        x.Name == "masks")
                    .AsTensor<float>();

            var iou =
                results
                    .First(x =>
                        x.Name == "iou_predictions")
                    .AsTensor<float>();

            var lowResMasks =
                results
                    .First(x =>
                        x.Name == "low_res_masks")
                    .AsTensor<float>();

            // ------------------------------------------------------------
            // Diagnostics.
            // ------------------------------------------------------------

            Console.WriteLine();

            Console.WriteLine(
                "Decoder masks dimensions: " +
                $"[{string.Join(
                    ", ",
                    masks.Dimensions.ToArray())}]");

            Console.WriteLine(
                "Decoder IoU dimensions: " +
                $"[{string.Join(
                    ", ",
                    iou.Dimensions.ToArray())}]");

            Console.WriteLine(
                "Decoder low-res dimensions: " +
                $"[{string.Join(
                    ", ",
                    lowResMasks.Dimensions.ToArray())}]");

            return new SegmentationResult
            {
                Masks =
                    [.. masks],

                MaskDimensions =
                    masks.Dimensions.ToArray(),

                IoU =
                    [.. iou],

                LowResMasks =
                    lowResMasks.ToArray(),

                LowResDimensions =
                    lowResMasks.Dimensions.ToArray()
            };
        }


        public void Dispose()
        {
            _session.Dispose();
        }
    }


    public readonly record struct SamPoint(
        float X,
        float Y,
        bool IsPositive);
}