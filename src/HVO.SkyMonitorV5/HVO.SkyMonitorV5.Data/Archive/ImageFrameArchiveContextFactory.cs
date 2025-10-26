using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HVO.SkyMonitorV5.Data.Archive;

/// <summary>
/// Design-time factory for generating migrations for <see cref="ImageFrameArchiveContext"/>.
/// </summary>
public sealed class ImageFrameArchiveContextFactory : IDesignTimeDbContextFactory<ImageFrameArchiveContext>
{
    public ImageFrameArchiveContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ImageFrameArchiveContext>();

        var basePath = AppContext.BaseDirectory;
        var databasePath = Path.Combine(basePath, "image-frame-archive.design.db");
        var connectionString = FormattableString.Invariant($"Data Source={databasePath}");

        optionsBuilder.UseSqlite(connectionString, sqliteOptions =>
        {
            sqliteOptions.MigrationsAssembly(typeof(ImageFrameArchiveContext).Assembly.FullName);
        });

        return new ImageFrameArchiveContext(optionsBuilder.Options);
    }
}
