#nullable enable

using System;
using HVO.SkyMonitorV5.RPi.Infrastructure.NativeMemory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HVO.SkyMonitorV5.RPi.Tests.Infrastructure.NativeMemory;

[TestClass]
public sealed class HGlobalNativeBufferLeaseFactoryTests
{
    [TestMethod]
    public void Rent_ReturnsAllocatedLease()
    {
        var factory = HGlobalNativeBufferLeaseFactory.Shared;
        using var lease = factory.Rent(64);

        Assert.AreEqual(64, lease.Length);
        Assert.AreNotEqual(IntPtr.Zero, lease.Pointer);
        Assert.IsTrue(lease.IsAllocated);
    }

    [TestMethod]
    public void Lease_AddRefAndRelease_DeallocatesOnFinalRelease()
    {
        var factory = HGlobalNativeBufferLeaseFactory.Shared;
        var lease = factory.Rent(128);

        try
        {
            var originalPointer = lease.Pointer;
            Assert.AreNotEqual(IntPtr.Zero, originalPointer);

            lease.AddRef();
            lease.Release();
            Assert.AreEqual(originalPointer, lease.Pointer);
            Assert.IsTrue(lease.IsAllocated);
        }
        finally
        {
            lease.Dispose();
            Assert.AreEqual(IntPtr.Zero, lease.Pointer);
            Assert.IsFalse(lease.IsAllocated);
        }
    }
}
