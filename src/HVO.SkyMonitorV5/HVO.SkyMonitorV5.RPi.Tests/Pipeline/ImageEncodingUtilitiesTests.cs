using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using HVO.SkyMonitorV5.RPi.Pipeline;

namespace HVO.SkyMonitorV5.RPi.Tests.Pipeline;

[TestClass]
public class ImageEncodingUtilitiesTests
{
  #region ToSkiaFormat Tests

  [TestMethod]
  public void ToSkiaFormat_Jpeg_ReturnsSkiaJpeg()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToSkiaFormat(ImageEncodingFormat.Jpeg);

    // Assert
    Assert.AreEqual(SKEncodedImageFormat.Jpeg, result);
  }

  [TestMethod]
  public void ToSkiaFormat_Png_ReturnsSkiaPng()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToSkiaFormat(ImageEncodingFormat.Png);

    // Assert
    Assert.AreEqual(SKEncodedImageFormat.Png, result);
  }

  [TestMethod]
  public void ToSkiaFormat_Fits_ThrowsNotSupportedException()
  {
    // Arrange & Act
    try
    {
      ImageEncodingUtilities.ToSkiaFormat(ImageEncodingFormat.Fits);
      Assert.Fail("Expected NotSupportedException was not thrown");
    }
    catch (NotSupportedException ex)
    {
      // Assert
      Assert.Contains("FITS", ex.Message);
    }
  }

  [TestMethod]
  public void ToSkiaFormat_Tiff_ThrowsNotSupportedException()
  {
    // Arrange & Act
    try
    {
      ImageEncodingUtilities.ToSkiaFormat(ImageEncodingFormat.Tiff);
      Assert.Fail("Expected NotSupportedException was not thrown");
    }
    catch (NotSupportedException ex)
    {
      // Assert
      Assert.Contains("TIFF", ex.Message);
    }
  }

  [TestMethod]
  public void ToSkiaFormat_Xisf_ThrowsNotSupportedException()
  {
    // Arrange & Act
    try
    {
      ImageEncodingUtilities.ToSkiaFormat(ImageEncodingFormat.Xisf);
      Assert.Fail("Expected NotSupportedException was not thrown");
    }
    catch (NotSupportedException ex)
    {
      // Assert
      Assert.Contains("XISF", ex.Message);
    }
  }

  #endregion

  #region ToContentType Tests

  [TestMethod]
  public void ToContentType_Jpeg_ReturnsImageJpeg()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToContentType(ImageEncodingFormat.Jpeg);

    // Assert
    Assert.AreEqual("image/jpeg", result);
  }

  [TestMethod]
  public void ToContentType_Png_ReturnsImagePng()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToContentType(ImageEncodingFormat.Png);

    // Assert
    Assert.AreEqual("image/png", result);
  }

  [TestMethod]
  public void ToContentType_Fits_ReturnsImageFits()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToContentType(ImageEncodingFormat.Fits);

    // Assert
    Assert.AreEqual("image/fits", result);
  }

  [TestMethod]
  public void ToContentType_Tiff_ReturnsImageTiff()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToContentType(ImageEncodingFormat.Tiff);

    // Assert
    Assert.AreEqual("image/tiff", result);
  }

  [TestMethod]
  public void ToContentType_Xisf_ReturnsOctetStream()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToContentType(ImageEncodingFormat.Xisf);

    // Assert
    Assert.AreEqual("application/octet-stream", result);
  }

  #endregion

  #region ToFileExtension Tests

  [TestMethod]
  public void ToFileExtension_Jpeg_ReturnsJpg()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToFileExtension(ImageEncodingFormat.Jpeg);

    // Assert
    Assert.AreEqual("jpg", result);
  }

  [TestMethod]
  public void ToFileExtension_Png_ReturnsPng()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToFileExtension(ImageEncodingFormat.Png);

    // Assert
    Assert.AreEqual("png", result);
  }

  [TestMethod]
  public void ToFileExtension_Fits_ReturnsFits()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToFileExtension(ImageEncodingFormat.Fits);

    // Assert
    Assert.AreEqual("fits", result);
  }

  [TestMethod]
  public void ToFileExtension_Tiff_ReturnsTiff()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToFileExtension(ImageEncodingFormat.Tiff);

    // Assert
    Assert.AreEqual("tiff", result);
  }

  [TestMethod]
  public void ToFileExtension_Xisf_ReturnsXisf()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.ToFileExtension(ImageEncodingFormat.Xisf);

    // Assert
    Assert.AreEqual("xisf", result);
  }

  #endregion

  #region Normalize Tests

  [TestMethod]
  public void Normalize_NullSettings_ReturnsDefaultSettings()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.Normalize(null);

    // Assert
    Assert.IsNotNull(result);
    Assert.AreEqual(ImageEncodingFormat.Jpeg, result.Format);
    Assert.AreEqual(90, result.Quality);
  }

  [TestMethod]
  public void Normalize_QualityWithinRange_ReturnsUnchanged()
  {
    // Arrange
    var settings = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 75);

    // Act
    var result = ImageEncodingUtilities.Normalize(settings);

    // Assert
    Assert.AreEqual(75, result.Quality);
  }

  [TestMethod]
  public void Normalize_QualityTooLow_ClampsToOne()
  {
    // Arrange
    var settings = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, -10);

    // Act
    var result = ImageEncodingUtilities.Normalize(settings);

    // Assert
    Assert.AreEqual(1, result.Quality);
  }

  [TestMethod]
  public void Normalize_QualityTooHigh_ClampsToHundred()
  {
    // Arrange
    var settings = new ImageEncodingSettings(ImageEncodingFormat.Jpeg, 150);

    // Act
    var result = ImageEncodingUtilities.Normalize(settings);

    // Assert
    Assert.AreEqual(100, result.Quality);
  }

  [TestMethod]
  public void Normalize_PreservesFitsOptions()
  {
    // Arrange
    var fitsOptions = new FitsEncodingOptions { BitDepth = FitsBitDepth.F32 };
    var settings = new ImageEncodingSettings(ImageEncodingFormat.Fits, 100, fitsOptions);

    // Act
    var result = ImageEncodingUtilities.Normalize(settings);

    // Assert
    Assert.IsNotNull(result.FitsOptions);
    Assert.AreEqual(FitsBitDepth.F32, result.FitsOptions.BitDepth);
  }

  #endregion

  #region RequiresSpecializedEncoder Tests

  [TestMethod]
  public void RequiresSpecializedEncoder_Jpeg_ReturnsFalse()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.RequiresSpecializedEncoder(ImageEncodingFormat.Jpeg);

    // Assert
    Assert.IsFalse(result);
  }

  [TestMethod]
  public void RequiresSpecializedEncoder_Png_ReturnsFalse()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.RequiresSpecializedEncoder(ImageEncodingFormat.Png);

    // Assert
    Assert.IsFalse(result);
  }

  [TestMethod]
  public void RequiresSpecializedEncoder_Fits_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.RequiresSpecializedEncoder(ImageEncodingFormat.Fits);

    // Assert
    Assert.IsTrue(result);
  }

  [TestMethod]
  public void RequiresSpecializedEncoder_Tiff_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.RequiresSpecializedEncoder(ImageEncodingFormat.Tiff);

    // Assert
    Assert.IsTrue(result);
  }

  [TestMethod]
  public void RequiresSpecializedEncoder_Xisf_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.RequiresSpecializedEncoder(ImageEncodingFormat.Xisf);

    // Assert
    Assert.IsTrue(result);
  }

  #endregion

  #region IsRasterFormat Tests

  [TestMethod]
  public void IsRasterFormat_Jpeg_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.IsRasterFormat(ImageEncodingFormat.Jpeg);

    // Assert
    Assert.IsTrue(result);
  }

  [TestMethod]
  public void IsRasterFormat_Png_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.IsRasterFormat(ImageEncodingFormat.Png);

    // Assert
    Assert.IsTrue(result);
  }

  [TestMethod]
  public void IsRasterFormat_Tiff_ReturnsTrue()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.IsRasterFormat(ImageEncodingFormat.Tiff);

    // Assert
    Assert.IsTrue(result);
  }

  [TestMethod]
  public void IsRasterFormat_Fits_ReturnsFalse()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.IsRasterFormat(ImageEncodingFormat.Fits);

    // Assert
    Assert.IsFalse(result);
  }

  [TestMethod]
  public void IsRasterFormat_Xisf_ReturnsFalse()
  {
    // Arrange & Act
    var result = ImageEncodingUtilities.IsRasterFormat(ImageEncodingFormat.Xisf);

    // Assert
    Assert.IsFalse(result);
  }

  #endregion
}
