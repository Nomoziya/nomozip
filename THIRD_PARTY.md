# Third-party components and sources

The product uses official, unmodified x64 7-Zip engines 26.04 and 26.03. Their DLLs and executables are distributed with the corresponding license text. Source packages are available from the official release links:

- https://github.com/ip7z/7zip/releases/tag/26.04
- https://github.com/ip7z/7zip/releases/tag/26.03
- https://www.7-zip.org/

The worker is built against the interface headers from the 26.04 source package. The original LGPL/BSD and unRAR license information is in the product's licenses folder. Review the exact files and licenses before a public release; this development preview is not a claim that publication packaging has completed its acceptance.

JSON parsing in the native worker uses nlohmann/json 3.12.0 (MIT): https://github.com/nlohmann/json/tree/v3.12.0. Its license is copied into the product package.

The native worker is built with MinGW and static runtime libraries. GCC runtime, mingw-w64 and winpthreads license notices from the compiler distribution are copied into the licenses folder, including the GCC Runtime Library Exception.

The C# password store uses Microsoft's System.Security.Cryptography.ProtectedData 10.0.12 package (MIT): https://www.nuget.org/packages/System.Security.Cryptography.ProtectedData/10.0.12. Runtime and framework dependencies are Microsoft's .NET 10 / WPF.

Integration tests download RAR fixtures from libarchive's test suite, and do not include them in the application package: https://github.com/libarchive/libarchive/tree/master/libarchive/test. The tests use the fixture content and passwords documented in test_read_format_rar_encryption.c. Test fixture provenance is independent from the product license.
