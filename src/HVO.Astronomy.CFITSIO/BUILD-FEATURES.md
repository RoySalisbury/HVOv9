# CFITSIO Native Builds (feature manifest)

_Generated: 2025-10-24 (updated)_

This package includes native CFITSIO libraries for ARM64 platforms. The libraries are statically linked where possible and dynamically linked to system libraries.

## Platform Support

Currently included platforms:
- **linux-arm64**: Linux on ARM64 (aarch64)
- **osx-arm64**: macOS on Apple Silicon (M1/M2/M3)

**Note**: x64 platforms (Windows, Linux, macOS) and other architectures are not yet included.

---

## osx-arm64

- **Library**: `libcfitsio.dylib`
- **Library path**: `runtimes/osx-arm64/native/libcfitsio.dylib`
- **SONAME**: libcfitsio.10.dylib
- **CFITSIO version**: 4.x (shared library version 10)
- **Architecture**: Mach-O 64-bit ARM64 dynamically linked shared library
- **System dependencies**: 
  - `/usr/lib/libSystem.B.dylib` (macOS system library)
  - `/usr/lib/libz.1.dylib` (zlib for GZIP compression)
  - `/usr/lib/libcurl.4.dylib` (libcurl for URL I/O)
- **Compression support**:
  - GZIP (.gz): **yes** (via libz)
  - BZIP2 (.bz2): **unknown**
- **Network I/O support**:
  - HTTP/HTTPS: **yes** (via libcurl)
  - FTP: **yes** (via libcurl)

---

## linux-arm64

- **Library**: `libcfitsio.so`
- **Library path**: `runtimes/linux-arm64/native/libcfitsio.so`
- **SONAME**: libcfitsio.so.10
- **CFITSIO version**: 4.x (shared library version 10)
- **Architecture**: ELF 64-bit LSB shared object, ARM aarch64
- **System dependencies**: 
  - `libm.so.6` (math library)
  - `libcurl-gnutls.so.4` (libcurl with GnuTLS for URL I/O)
  - `libz.so.1` (zlib for GZIP compression)
  - `libbz2.so.1.0` (bzip2 for BZIP2 compression)
  - `libc.so.6` (GNU C library)
  - Plus additional transitive dependencies for TLS/crypto (gnutls, crypto, etc.)
- **Compression support**:
  - GZIP (.gz): **yes** (via libz)
  - BZIP2 (.bz2): **yes** (via libbz2)
- **Network I/O support**:
  - HTTP/HTTPS: **yes** (via libcurl with GnuTLS, supports modern TLS)
  - FTP: **yes** (via libcurl)
  - GSIFTP: **yes** (GridFTP support via libcurl)

---

## Feature Summary

### Compression Formats Supported
- **Rice**: yes (built-in CFITSIO algorithm)
- **GZIP**: yes (all platforms via zlib)
- **BZIP2**: yes on Linux ARM64, unknown on macOS ARM64
- **HCOMPRESS**: yes (built-in CFITSIO wavelet compression)
- **PLIO**: yes (built-in CFITSIO algorithm)

### I/O Drivers Registered
Based on library inspection, the following CFITSIO I/O drivers are registered:
- `file://` - Local file access
- `mem://` - In-memory FITS files
- `memkeep://` - Persistent in-memory files
- `stdin://`, `stdout://` - Standard I/O streams
- `compress://`, `compressmem://` - Compressed file handling
- `http://`, `ftp://` - Network protocols (when libcurl linked)
- `root://` - ROOT file format support (experimental)

### Security & Verification
- HTTPS certificate verification can be controlled via `CFITSIO_VERIFY_HTTPS` environment variable
- Built with modern TLS support on Linux (via GnuTLS)
- Uses system-provided crypto libraries for secure connections

---

## Platform-Specific Notes

### Linux ARM64
- Uses GnuTLS instead of OpenSSL for TLS (via libcurl-gnutls)
- Comprehensive compression support including BZIP2
- Full network I/O capability with modern security protocols
- Suitable for server/cloud deployments on ARM64 infrastructure

### macOS ARM64
- Uses Apple's system libraries for networking and compression
- Native support for Apple Silicon (M1/M2/M3 chips)
- Leverages macOS system security frameworks via libcurl

---

## Verification

To verify library features on your platform:

```bash
# Check library dependencies (Linux)
ldd runtimes/linux-arm64/native/libcfitsio.so

# Check library dependencies (macOS)
otool -L runtimes/osx-arm64/native/libcfitsio.dylib

# Check for specific features (Linux/macOS)
strings runtimes/*/native/libcfitsio.* | grep -i "driver\|protocol"
```

---

## Future Platform Support

Planned additions:
- **win-x64**: Windows on x64 (requires CFITSIO build with MSVC or MinGW)
- **linux-x64**: Linux on x64/AMD64
- **osx-x64**: macOS on Intel (pre-Apple Silicon)
- **win-arm64**: Windows on ARM64 (Surface Pro X, etc.)

Contributions of pre-built libraries for these platforms are welcome!