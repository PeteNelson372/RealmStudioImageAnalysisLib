using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageResult
    {
        public bool Succeeded { get; init; }

        public string? FailureReason { get; init; }
    }
}
