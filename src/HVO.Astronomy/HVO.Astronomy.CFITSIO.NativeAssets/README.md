# HVO.Astronomy.CFITSIO.NativeAssets

Native CFITSIO library binaries for multiple platforms (Linux ARM64, macOS ARM64).

## Purpose

This package contains only the native CFITSIO binaries required by the `HVO.Astronomy.CFITSIO` managed library. It enables proper native asset resolution for:

- **NuGet consumers**: The managed `HVO.Astronomy.CFITSIO` package references this transitively
- **Source-based consumers**: Projects using `ProjectReference` to `HVO.Astronomy.CFITSIO` should add a `PackageReference` to this package

## Platforms Included

- `linux-arm64`: `libcfitsio.so`
- `osx-arm64`: `libcfitsio.dylib`

Additional platforms can be added by building CFITSIO for the target RID and placing the library in the appropriate `runtimes/{RID}/native/` directory.

## Usage

### For NuGet Package Consumers

When you install `HVO.Astronomy.CFITSIO` from NuGet, this native assets package is included automatically as a transitive dependency. No additional steps are needed.

### For Source-Based Development (ProjectReference)

If you're referencing `HVO.Astronomy.CFITSIO` via `<ProjectReference>`, add this package reference to your project:

```xml
<ItemGroup>
  <ProjectReference Include="../HVO.Astronomy.CFITSIO/HVO.Astronomy.CFITSIO.csproj" />
  <PackageReference Include="HVO.Astronomy.CFITSIO.NativeAssets" Version="1.0.3" />
</ItemGroup>
```

This ensures the native libraries are copied to your output directory during build.

## License

BSD-3-Clause (matching CFITSIO upstream license)

## Repository

Part of the HVOv9 observatory software suite: https://github.com/RoySalisbury/HVOv9
