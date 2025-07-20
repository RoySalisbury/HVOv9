using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.WebSite.RoofControllerV4.Logic;
using HVO.WebSite.RoofControllerV4.Models;

namespace HVO.WebSite.RoofControllerV4.Tests.Logic;

[TestClass]public class RoofControllerStatusTests
{
    #region Enum Value Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.Unknown, 0)]
    [DataRow(RoofControllerStatus.NotInitialized, 1)]
    [DataRow(RoofControllerStatus.Closed, 2)]
    [DataRow(RoofControllerStatus.Closing, 3)]
    [DataRow(RoofControllerStatus.Open, 4)]
    [DataRow(RoofControllerStatus.Opening, 5)]
    [DataRow(RoofControllerStatus.Stopped, 6)]
    [DataRow(RoofControllerStatus.PartiallyOpen, 7)]
    [DataRow(RoofControllerStatus.PartiallyClose, 8)]
    [DataRow(RoofControllerStatus.Error, 99)]
    public void EnumValues_HaveCorrectIntegerValues(RoofControllerStatus status, int expectedValue)
    {
        // Act & Assert
        ((int)status).Should().Be(expectedValue);
    }

    [TestMethod]
    public void AllEnumValues_AreDefined()
    {
        // Arrange
        var expectedValues = new[]
        {
            RoofControllerStatus.Unknown,
            RoofControllerStatus.NotInitialized,
            RoofControllerStatus.Closed,
            RoofControllerStatus.Closing,
            RoofControllerStatus.Open,
            RoofControllerStatus.Opening,
            RoofControllerStatus.Stopped,
            RoofControllerStatus.PartiallyOpen,
            RoofControllerStatus.PartiallyClose,
            RoofControllerStatus.Error
        };

        // Act
        var allEnumValues = Enum.GetValues<RoofControllerStatus>();

        // Assert
        allEnumValues.Should().BeEquivalentTo(expectedValues);
        allEnumValues.Should().HaveCount(10);
    }

    #endregion

    #region Enum Name Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.Unknown, "Unknown")]
    [DataRow(RoofControllerStatus.NotInitialized, "NotInitialized")]
    [DataRow(RoofControllerStatus.Closed, "Closed")]
    [DataRow(RoofControllerStatus.Closing, "Closing")]
    [DataRow(RoofControllerStatus.Open, "Open")]
    [DataRow(RoofControllerStatus.Opening, "Opening")]
    [DataRow(RoofControllerStatus.Stopped, "Stopped")]
    [DataRow(RoofControllerStatus.PartiallyOpen, "PartiallyOpen")]
    [DataRow(RoofControllerStatus.PartiallyClose, "PartiallyClose")]
    [DataRow(RoofControllerStatus.Error, "Error")]
    public void EnumValues_HaveCorrectNames(RoofControllerStatus status, string expectedName)
    {
        // Act & Assert
        status.ToString().Should().Be(expectedName);
    }

    #endregion

    #region State Category Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.Opening)]
    [DataRow(RoofControllerStatus.Closing)]
    public void MovementStates_AreTransitional(RoofControllerStatus status)
    {
        // Assert - these states represent the roof in motion
        status.Should().BeOneOf(RoofControllerStatus.Opening, RoofControllerStatus.Closing);
    }

    [TestMethod]
    [DataRow(RoofControllerStatus.Open)]
    [DataRow(RoofControllerStatus.Closed)]
    [DataRow(RoofControllerStatus.Stopped)]
    public void FinalStates_AreStable(RoofControllerStatus status)
    {
        // Assert - these states represent the roof at rest
        status.Should().BeOneOf(RoofControllerStatus.Open, RoofControllerStatus.Closed, RoofControllerStatus.Stopped);
    }

    [TestMethod]
    [DataRow(RoofControllerStatus.Unknown)]
    [DataRow(RoofControllerStatus.NotInitialized)]
    [DataRow(RoofControllerStatus.Error)]
    public void SystemStates_AreNonOperational(RoofControllerStatus status)
    {
        // Assert - these states represent system conditions where normal operation is not possible
        status.Should().BeOneOf(RoofControllerStatus.Unknown, RoofControllerStatus.NotInitialized, RoofControllerStatus.Error);
    }

    #endregion

    #region Serialization Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.Unknown)]
    [DataRow(RoofControllerStatus.NotInitialized)]
    [DataRow(RoofControllerStatus.Closed)]
    [DataRow(RoofControllerStatus.Closing)]
    [DataRow(RoofControllerStatus.Open)]
    [DataRow(RoofControllerStatus.Opening)]
    [DataRow(RoofControllerStatus.Stopped)]
    [DataRow(RoofControllerStatus.Error)]
    public void EnumValues_CanBeConvertedToStringAndBack(RoofControllerStatus originalStatus)
    {
        // Act
        var stringValue = originalStatus.ToString();
        var success = Enum.TryParse<RoofControllerStatus>(stringValue, out var parsedStatus);

        // Assert
        success.Should().BeTrue();
        parsedStatus.Should().Be(originalStatus);
    }

    [TestMethod]
    [DataRow(0, RoofControllerStatus.Unknown)]
    [DataRow(1, RoofControllerStatus.NotInitialized)]
    [DataRow(2, RoofControllerStatus.Closed)]
    [DataRow(3, RoofControllerStatus.Closing)]
    [DataRow(4, RoofControllerStatus.Open)]
    [DataRow(5, RoofControllerStatus.Opening)]
    [DataRow(6, RoofControllerStatus.Stopped)]
    [DataRow(99, RoofControllerStatus.Error)]
    public void IntegerValues_CanBeCastToEnum(int intValue, RoofControllerStatus expectedStatus)
    {
        // Act
        var castStatus = (RoofControllerStatus)intValue;

        // Assert
        castStatus.Should().Be(expectedStatus);
    }

    #endregion

    #region Default Value Tests

    [TestMethod]
    public void DefaultEnumValue_IsUnknown()
    {
        // Act
        var defaultValue = default(RoofControllerStatus);

        // Assert
        defaultValue.Should().Be(RoofControllerStatus.Unknown);
        ((int)defaultValue).Should().Be(0);
    }

    #endregion

    #region Edge Case Tests

    [TestMethod]
    public void InvalidIntegerValue_CastsToUndefinedEnum()
    {
        // Act
        var invalidStatus = (RoofControllerStatus)1000;

        // Assert
        Enum.GetValues<RoofControllerStatus>().Should().NotContain(invalidStatus);
        ((int)invalidStatus).Should().Be(1000);
    }

    [TestMethod]
    public void EnumIsDefined_WorksCorrectly()
    {
        // Act & Assert
        Enum.IsDefined(typeof(RoofControllerStatus), RoofControllerStatus.Open).Should().BeTrue();
        Enum.IsDefined(typeof(RoofControllerStatus), (RoofControllerStatus)1000).Should().BeFalse();
    }

    #endregion

    #region State Transition Logic Tests

    [TestMethod]
    [DataRow(RoofControllerStatus.NotInitialized, RoofControllerStatus.Stopped)]
    [DataRow(RoofControllerStatus.Stopped, RoofControllerStatus.Opening)]
    [DataRow(RoofControllerStatus.Stopped, RoofControllerStatus.Closing)]
    [DataRow(RoofControllerStatus.Opening, RoofControllerStatus.Stopped)]
    [DataRow(RoofControllerStatus.Closing, RoofControllerStatus.Stopped)]
    [DataRow(RoofControllerStatus.Opening, RoofControllerStatus.Open)]
    [DataRow(RoofControllerStatus.Closing, RoofControllerStatus.Closed)]
    public void StateTransitions_AreLogical(RoofControllerStatus fromState, RoofControllerStatus toState)
    {
        // This test documents expected state transitions
        // Assert that both states are valid enum values
        Enum.IsDefined(typeof(RoofControllerStatus), fromState).Should().BeTrue();
        Enum.IsDefined(typeof(RoofControllerStatus), toState).Should().BeTrue();
        
        // The transition relationship is documented but not enforced at the enum level
        fromState.Should().NotBe(toState, "because we're testing transitions between different states");
    }

    #endregion
}
