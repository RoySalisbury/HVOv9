using Microsoft.VisualStudio.TestTools.UnitTesting;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public class ImageEncodingFormatTests
{
    [TestMethod]
    public void ImageEncodingFormat_AllValuesAreDefined()
    {
        // Arrange & Act
        var formats = Enum.GetValues<ImageEncodingFormat>();

        // Assert
        Assert.AreEqual(5, formats.Length, "Expected 5 format values");
        CollectionAssert.Contains(formats, ImageEncodingFormat.Png);
        CollectionAssert.Contains(formats, ImageEncodingFormat.Jpeg);
        CollectionAssert.Contains(formats, ImageEncodingFormat.Fits);
        CollectionAssert.Contains(formats, ImageEncodingFormat.Tiff);
        CollectionAssert.Contains(formats, ImageEncodingFormat.Xisf);
    }

    [TestMethod]
    public void ImageEncodingFormat_ValuesHaveCorrectNumericValues()
    {
        // Assert enum values maintain expected numbering
        Assert.AreEqual(0, (int)ImageEncodingFormat.Png);
        Assert.AreEqual(1, (int)ImageEncodingFormat.Jpeg);
        Assert.AreEqual(2, (int)ImageEncodingFormat.Fits);
        Assert.AreEqual(3, (int)ImageEncodingFormat.Tiff);
        Assert.AreEqual(4, (int)ImageEncodingFormat.Xisf);
    }
}

[TestClass]
public class FitsBitDepthTests
{
    [TestMethod]
    public void FitsBitDepth_AllValuesAreDefined()
    {
        // Arrange & Act
        var depths = Enum.GetValues<FitsBitDepth>();

        // Assert
        Assert.AreEqual(6, depths.Length, "Expected 6 bit depth values");
        CollectionAssert.Contains(depths, FitsBitDepth.U8);
        CollectionAssert.Contains(depths, FitsBitDepth.U16);
        CollectionAssert.Contains(depths, FitsBitDepth.I16);
        CollectionAssert.Contains(depths, FitsBitDepth.I32);
        CollectionAssert.Contains(depths, FitsBitDepth.F32);
        CollectionAssert.Contains(depths, FitsBitDepth.F64);
    }

    [TestMethod]
    public void FitsBitDepth_ValuesHaveCorrectNumericValues()
    {
        // Assert enum values maintain expected numbering
        Assert.AreEqual(0, (int)FitsBitDepth.U8);
        Assert.AreEqual(1, (int)FitsBitDepth.U16);
        Assert.AreEqual(2, (int)FitsBitDepth.I16);
        Assert.AreEqual(3, (int)FitsBitDepth.I32);
        Assert.AreEqual(4, (int)FitsBitDepth.F32);
        Assert.AreEqual(5, (int)FitsBitDepth.F64);
    }
}

[TestClass]
public class FitsImageFormatTests
{
    [TestMethod]
    public void FitsImageFormat_AllValuesAreDefined()
    {
        // Arrange & Act
        var formats = Enum.GetValues<FitsImageFormat>();

        // Assert
        Assert.AreEqual(4, formats.Length, "Expected 4 image format values");
        CollectionAssert.Contains(formats, FitsImageFormat.Mono);
        CollectionAssert.Contains(formats, FitsImageFormat.RGB);
        CollectionAssert.Contains(formats, FitsImageFormat.RGBA);
        CollectionAssert.Contains(formats, FitsImageFormat.BayerMosaic);
    }

    [TestMethod]
    public void FitsImageFormat_ValuesHaveCorrectNumericValues()
    {
        // Assert enum values maintain expected numbering
        Assert.AreEqual(0, (int)FitsImageFormat.Mono);
        Assert.AreEqual(1, (int)FitsImageFormat.RGB);
        Assert.AreEqual(2, (int)FitsImageFormat.RGBA);
        Assert.AreEqual(3, (int)FitsImageFormat.BayerMosaic);
    }
}

[TestClass]
public class FitsCompressionTests
{
    [TestMethod]
    public void FitsCompression_AllValuesAreDefined()
    {
        // Arrange & Act
        var compressions = Enum.GetValues<FitsCompression>();

        // Assert
        Assert.AreEqual(6, compressions.Length, "Expected 6 compression values");
        CollectionAssert.Contains(compressions, FitsCompression.None);
        CollectionAssert.Contains(compressions, FitsCompression.Rice);
        CollectionAssert.Contains(compressions, FitsCompression.Gzip1);
        CollectionAssert.Contains(compressions, FitsCompression.Gzip2);
        CollectionAssert.Contains(compressions, FitsCompression.HCompress);
        CollectionAssert.Contains(compressions, FitsCompression.PLio);
    }

    [TestMethod]
    public void FitsCompression_ValuesHaveCorrectNumericValues()
    {
        // Assert enum values maintain expected numbering
        Assert.AreEqual(0, (int)FitsCompression.None);
        Assert.AreEqual(1, (int)FitsCompression.Rice);
        Assert.AreEqual(2, (int)FitsCompression.Gzip1);
        Assert.AreEqual(3, (int)FitsCompression.Gzip2);
        Assert.AreEqual(4, (int)FitsCompression.HCompress);
        Assert.AreEqual(5, (int)FitsCompression.PLio);
    }
}

[TestClass]
public class FitsEncodingOptionsTests
{
    [TestMethod]
    public void FitsEncodingOptions_DefaultConstructor_HasExpectedDefaults()
    {
        // Arrange & Act
        var options = new FitsEncodingOptions();

        // Assert
        Assert.AreEqual(FitsBitDepth.U16, options.BitDepth);
        Assert.AreEqual(FitsImageFormat.Mono, options.ImageFormat);
        Assert.AreEqual(FitsCompression.None, options.Compression);
        Assert.IsTrue(options.UnsignedU16);
        Assert.IsTrue(options.WriteChecksum);
    }

    [TestMethod]
    public void FitsEncodingOptions_WithExpression_UpdatesSingleProperty()
    {
        // Arrange
        var original = new FitsEncodingOptions();

        // Act
        var updated = original with { BitDepth = FitsBitDepth.U8 };

        // Assert
        Assert.AreEqual(FitsBitDepth.U8, updated.BitDepth);
        Assert.AreEqual(FitsImageFormat.Mono, updated.ImageFormat);
        Assert.AreEqual(FitsCompression.None, updated.Compression);
        Assert.IsTrue(updated.UnsignedU16);
        Assert.IsTrue(updated.WriteChecksum);
    }

    [TestMethod]
    public void FitsEncodingOptions_WithExpression_UpdatesMultipleProperties()
    {
        // Arrange
        var original = new FitsEncodingOptions();

        // Act
        var updated = original with 
        { 
            BitDepth = FitsBitDepth.F32,
            ImageFormat = FitsImageFormat.RGB,
            Compression = FitsCompression.Rice,
            UnsignedU16 = false,
            WriteChecksum = false
        };

        // Assert
        Assert.AreEqual(FitsBitDepth.F32, updated.BitDepth);
        Assert.AreEqual(FitsImageFormat.RGB, updated.ImageFormat);
        Assert.AreEqual(FitsCompression.Rice, updated.Compression);
        Assert.IsFalse(updated.UnsignedU16);
        Assert.IsFalse(updated.WriteChecksum);
    }

    [TestMethod]
    public void FitsEncodingOptions_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var options1 = new FitsEncodingOptions 
        { 
            BitDepth = FitsBitDepth.U16,
            Compression = FitsCompression.Rice
        };
        var options2 = new FitsEncodingOptions 
        { 
            BitDepth = FitsBitDepth.U16,
            Compression = FitsCompression.Rice
        };
        var options3 = new FitsEncodingOptions 
        { 
            BitDepth = FitsBitDepth.U8,
            Compression = FitsCompression.Rice
        };

        // Assert
        Assert.AreEqual(options1, options2);
        Assert.AreNotEqual(options1, options3);
    }
}

[TestClass]
public class ImageEncodingSettingsTests
{
    [TestMethod]
    public void ImageEncodingSettings_DefaultConstructor_HasExpectedDefaults()
    {
        // Arrange & Act
        var settings = new ImageEncodingSettings();

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Jpeg, settings.Format);
        Assert.AreEqual(90, settings.Quality);
        Assert.IsNull(settings.FitsOptions);
    }

    [TestMethod]
    public void ImageEncodingSettings_ParameterizedConstructor_SetsAllProperties()
    {
        // Arrange
        var fitsOptions = new FitsEncodingOptions { BitDepth = FitsBitDepth.U8 };

        // Act
        var settings = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, fitsOptions);

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Fits, settings.Format);
        Assert.AreEqual(100, settings.Quality);
        Assert.IsNotNull(settings.FitsOptions);
        Assert.AreEqual(FitsBitDepth.U8, settings.FitsOptions.BitDepth);
    }

    [TestMethod]
    public void ImageEncodingSettings_ParameterizedConstructor_AllowsNullFitsOptions()
    {
        // Arrange & Act
        var settings = new ImageEncodingSettings(ImageEncodingFormat.Png, 85, null);

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Png, settings.Format);
        Assert.AreEqual(85, settings.Quality);
        Assert.IsNull(settings.FitsOptions);
    }

    [TestMethod]
    public void ImageEncodingSettings_WithExpression_UpdatesFormat()
    {
        // Arrange
        var original = new ImageEncodingSettings();

        // Act
        var updated = original with { Format = ImageEncodingFormat.Png };

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Png, updated.Format);
        Assert.AreEqual(90, updated.Quality);
        Assert.IsNull(updated.FitsOptions);
    }

    [TestMethod]
    public void ImageEncodingSettings_WithExpression_UpdatesFitsOptions()
    {
        // Arrange
        var original = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, new FitsEncodingOptions());
        var newFitsOptions = new FitsEncodingOptions { BitDepth = FitsBitDepth.F32 };

        // Act
        var updated = original with { FitsOptions = newFitsOptions };

        // Assert
        Assert.AreEqual(ImageEncodingFormat.Fits, updated.Format);
        Assert.IsNotNull(updated.FitsOptions);
        Assert.AreEqual(FitsBitDepth.F32, updated.FitsOptions.BitDepth);
    }

    [TestMethod]
    public void ImageEncodingSettings_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var settings1 = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 90);
        var settings2 = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 90);
        var settings3 = new ImageEncodingSettings(ImageEncodingFormat.Png, 90);

        // Assert
        Assert.AreEqual(settings1, settings2);
        Assert.AreNotEqual(settings1, settings3);
    }

    [TestMethod]
    public void ImageEncodingSettings_RecordEquality_ConsidersFitsOptions()
    {
        // Arrange
        var fitsOptions1 = new FitsEncodingOptions { BitDepth = FitsBitDepth.U16 };
        var fitsOptions2 = new FitsEncodingOptions { BitDepth = FitsBitDepth.U16 };
        var fitsOptions3 = new FitsEncodingOptions { BitDepth = FitsBitDepth.U8 };

        var settings1 = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, fitsOptions1);
        var settings2 = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, fitsOptions2);
        var settings3 = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, fitsOptions3);

        // Assert
        Assert.AreEqual(settings1, settings2);
        Assert.AreNotEqual(settings1, settings3);
    }
}
