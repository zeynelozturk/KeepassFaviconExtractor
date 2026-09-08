### Native libwebp dependency

This project uses native **libwebp** libraries for WebP image support.

The required native binaries are obtained from the [Imazen libwebp-net releases](https://github.com/imazen/libwebp-net/releases).
The project provides prebuilt native libwebp binaries for Windows and other platforms.

The binaries are built from the official [WebP/libwebp](https://github.com/webmproject/libwebp) project.

If `native\\x86\\libwebp.dll` and `native\\x64\\libwebp.dll` are missing, run:

```powershell
.\\scripts\\bootstrap-native-webp.ps1
```

The version used by a particular release of this project may change over time.
The corresponding version and license information should be checked in the release package.

The distributed plugin package includes the required native libwebp binaries. See `THIRD-PARTY-NOTICES.txt` for the applicable third-party license information.