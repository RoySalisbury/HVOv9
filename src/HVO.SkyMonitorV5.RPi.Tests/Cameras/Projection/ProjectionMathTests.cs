#nullable enable

using System;
using HVO.SkyMonitorV5.RPi.Cameras.Projection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Cameras.Projection;

[TestClass]
public sealed class ProjectionMathTests
{
    [TestMethod]
    public void DirFromAltAz_ReturnsUnitVector()
    {
        var vector = ProjectionMath.DirFromAltAz(45.0, 135.0);
        var length = ProjectionMath.Norm(vector);

        Assert.AreEqual(1.0, length, 1e-9, "Direction vector should be unit length.");
    }

    [TestMethod]
    public void BuildBasis_ProducesOrthonormalSet()
    {
        var basis = ProjectionMath.BuildBasis(60.0, 45.0);

        var dotBWithE1 = ProjectionMath.Dot(basis.B, basis.E1);
        var dotBWithE2 = ProjectionMath.Dot(basis.B, basis.E2);
        var dotE1WithE2 = ProjectionMath.Dot(basis.E1, basis.E2);

        Assert.AreEqual(0.0, dotBWithE1, 1e-9, "B and E1 must be orthogonal.");
        Assert.AreEqual(0.0, dotBWithE2, 1e-9, "B and E2 must be orthogonal.");
        Assert.AreEqual(0.0, dotE1WithE2, 1e-9, "E1 and E2 must be orthogonal.");

        Assert.AreEqual(1.0, ProjectionMath.Norm(basis.B), 1e-9, "B must be unit length.");
        Assert.AreEqual(1.0, ProjectionMath.Norm(basis.E1), 1e-9, "E1 must be unit length.");
        Assert.AreEqual(1.0, ProjectionMath.Norm(basis.E2), 1e-9, "E2 must be unit length.");
    }

    [TestMethod]
    public void ProjectionVector_Normalize_HandlesZeroVector()
    {
        var zeroVector = new ProjectionMath.ProjectionVector(0.0, 0.0, 0.0);
        var normalized = zeroVector.Normalize();

        Assert.AreEqual(1.0, normalized.X, 1e-9);
        Assert.AreEqual(0.0, normalized.Y, 1e-9);
        Assert.AreEqual(0.0, normalized.Z, 1e-9);
    }

    [TestMethod]
    public void ProjectionVector_CreateUnit_ReturnsExpected()
    {
        var unit = ProjectionMath.ProjectionVector.CreateUnit(3.0, 0.0, 0.0);

        Assert.AreEqual(1.0, unit.X, 1e-9);
        Assert.AreEqual(0.0, unit.Y, 1e-9);
        Assert.AreEqual(0.0, unit.Z, 1e-9);
    }
}
