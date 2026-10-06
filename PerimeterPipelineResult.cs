using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineResult
    {
        public string PipelineName { get; init; } = "";

        public bool Succeeded { get; init; }

        public IReadOnlyList<PerimeterCandidate> Candidates { get; init; } = [];

        public double Confidence { get; init; }

        public string? FailureReason { get; init; }
    }
}
