using FluentAssertions;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;
using HVO.WebSite.RoofControllerV4.Tests.TestSupport;

namespace HVO.WebSite.RoofControllerV4.Tests.Services;

[TestClass]
public sealed class RoofControllerDisposalTests
{
    [TestMethod]
    public async Task DisposeAsync_ShouldMarkServiceDisposed_AndThrowOnOperations()
    {
        var hat = new FakeRoofHat();
        var service = RoofControllerTestFactory.CreateService(hat, options =>
        {
            options.EnableDigitalInputPolling = false;
            options.EnablePeriodicVerificationWhileMoving = false;
        });

        (await service.Initialize(CancellationToken.None)).IsSuccessful.Should().BeTrue();

    await service.DisposeAsync();

    service.IsServiceDisposed.Should().BeTrue();
    var result = service.Open();
    result.IsFailure.Should().BeTrue();
    result.Error.Should().BeOfType<ObjectDisposedException>();
    }

    [TestMethod]
    public void Dispose_ShouldMatchAsyncDisposeBehavior()
    {
        var hat = new FakeRoofHat();
        var service = RoofControllerTestFactory.CreateService(hat, options =>
        {
            options.EnableDigitalInputPolling = false;
            options.EnablePeriodicVerificationWhileMoving = false;
        });

        service.Dispose();

        service.IsServiceDisposed.Should().BeTrue();
        var result = service.Close();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ObjectDisposedException>();
    }
}
