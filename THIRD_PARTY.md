# Third-party components and sources

The product uses official, unmodified x64 7-Zip engines 26.04 and 26.03. Their DLLs and executables are distributed with the corresponding license text. It also includes the unmodified Windows `7z.sfx` modules from those same installers; these official modules are x86 executables, independent of the x64 or ARM64 engine DLL. Source packages are available from the official release links:

- https://github.com/ip7z/7zip/releases/tag/26.04
- https://github.com/ip7z/7zip/releases/tag/26.03
- https://www.7-zip.org/

The worker is built against the interface headers from the 26.04 source package. The original LGPL/BSD and unRAR license information is in the product's licenses folder. Review the exact files and licenses before a public release; this development preview is not a claim that publication packaging has completed its acceptance.

JSON parsing in the native worker uses nlohmann/json 3.12.0 (MIT): https://github.com/nlohmann/json/tree/v3.12.0. Its license is copied into the product package.

The x64 native worker is built with MinGW and static runtime libraries. GCC runtime, mingw-w64 and winpthreads license notices from the compiler distribution are copied into the licenses folder, including the GCC Runtime Library Exception. The ARM64 worker uses the Visual Studio ARM64 C++ compiler with /MT and the statically linked Microsoft C++ runtime; it needs no separate Visual C++ runtime installer.

The C# password store uses Microsoft's System.Security.Cryptography.ProtectedData 10.0.12 package (MIT): https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.12. Runtime and framework dependencies are Microsoft's .NET 10 / WPF.

Current Windows x64 and ARM64 distributions use the system's shared .NET 10 Desktop Runtime and do not bundle .NET or WPF runtime files. Older self-contained payloads are used only as isolated upgrade test fixtures and are excluded from release assets. The per-user installer uses NSIS 3.13, obtained from the official MSYS2 3.13-2 package with the published SHA-256 verified: https://packages.msys2.org/packages/mingw-w64-ucrt-x86_64-nsis. NSIS copyright and applicable license notices are included as `licenses/NSIS-COPYING.txt`.

Integration tests download RAR fixtures from libarchive's test suite, and do not include them in the application package: https://github.com/libarchive/libarchive/tree/master/libarchive/test. The tests use the fixture content and passwords documented in test_read_format_rar_encryption.c. Test fixture provenance is independent from the product license.
