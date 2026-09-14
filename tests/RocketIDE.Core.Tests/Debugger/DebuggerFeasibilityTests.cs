using RocketIDE.Core.Debugger;

namespace RocketIDE.Core.Tests.Debugger;

[TestClass]
public sealed class DebuggerFeasibilityTests
{
    [TestMethod]
    public void DeferredAssessmentCannotBeMarkedReady()
    {
        var assessment = DebuggerFeasibility.Deferred("No redistributable backend was found.");

        Assert.IsFalse(assessment.IsReadyToImplement);
        StringAssert.Contains(assessment.Recommendation, "DEFERRED");
        CollectionAssert.Contains(assessment.Capabilities.ToArray(), "CodeView/PDB source breakpoints");
    }
}
