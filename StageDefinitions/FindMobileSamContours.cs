using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class FindMobileSamContours : IPerimeterStage
    {
        public string Id => "FindMobileSamContours";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            MobileSamMaskSet maskSet =
                data.Get<MobileSamMaskSet>(
                    "MobileSamMasks");

            List<VectorOfVectorOfPoint> contourSets = [];

            for (int i = 0; i < maskSet.Masks.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                Image<Gray, byte> mask =
                    maskSet.Masks[i];

                VectorOfVectorOfPoint contours =
                    new();

                using Mat hierarchy =
                    new();

                CvInvoke.FindContours(
                    mask,
                    contours,
                    hierarchy,
                    RetrType.External,
                    ChainApproxMethod.ChainApproxSimple);

                contourSets.Add(contours);

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress =
                            (double)(i + 1) /
                            Math.Max(1, maskSet.Masks.Count),
                        Status =
                            $"Found external contours for region " +
                            $"{i + 1} of {maskSet.Masks.Count}."
                    });
            }

            data.Set(
                "MobileSamContours",
                new MobileSamContourSet(contourSets));

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
