#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#include <windows.h>
#include <appmodel.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <fstream>
#include <string>
#include <vector>

// Runs only inside the disposable signed sparse-package acceptance fixture.
int wmain(int argc, wchar_t** argv) {
    if (argc < 4) return 2;
    UINT32 length = 0;
    if (GetCurrentPackageFullName(&length, nullptr) != ERROR_INSUFFICIENT_BUFFER) return 3;
    std::vector<wchar_t> package(length);
    if (GetCurrentPackageFullName(&length, package.data()) != ERROR_SUCCESS) return 4;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 5;
    CLSID root{}; CLSIDFromString(L"{c708b15d-0d3e-47ab-8fc8-c22d21ff1170}", &root);
    IExplorerCommand* command = nullptr;
    auto hr = CoCreateInstance(root, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&command));
    if (FAILED(hr)) { fwprintf(stderr,L"Packaged root HRESULT: 0x%08lx\n", static_cast<unsigned long>(hr)); return 6; }
    std::vector<PIDLIST_ABSOLUTE> ids;
    for (int index = 3; index < argc; ++index) { auto id = ILCreateFromPathW(argv[index]); if (!id) return 7; ids.push_back(id); }
    std::vector<PCIDLIST_ABSOLUTE> readonlyIds(ids.begin(), ids.end());
    IShellItemArray* selection = nullptr;
    hr = SHCreateShellItemArrayFromIDLists(static_cast<UINT>(ids.size()), readonlyIds.data(), &selection);
    for (auto id : ids) ILFree(id);
    if (FAILED(hr)) return 8;
    EXPCMDFLAGS flags{}; command->GetFlags(&flags);
    if (!(flags & ECF_HASSUBCOMMANDS)) return 9;
    IEnumExplorerCommand* commands = nullptr; hr = command->EnumSubCommands(&commands); command->Release();
    if (FAILED(hr)) return 10;
    GUID desired{}; CLSIDFromString(argv[2], &desired);
    IExplorerCommand* child = nullptr; bool found = false;
    while (commands->Next(1, &child, nullptr) == S_OK) {
        GUID name{}; child->GetCanonicalName(&name);
        if (name == desired) { found = true; break; }
        child->Release(); child = nullptr;
    }
    commands->Release(); if (!found) return 11;
    EXPCMDSTATE state{}; hr = child->GetState(selection, TRUE, &state);
    if (FAILED(hr) || state != ECS_ENABLED) return 12;
    hr = child->Invoke(selection, nullptr); child->Release(); selection->Release(); CoUninitialize();
    if (FAILED(hr)) { fwprintf(stderr,L"Packaged invoke HRESULT: 0x%08lx\n",static_cast<unsigned long>(hr)); return 13; }
    // Package identity is checked by the OS; no LoadLibrary or registry COM bypass.
    std::wstring widePackage(package.data()); std::string packageName(widePackage.begin(),widePackage.end());
    std::ofstream output(argv[1]); output << "{\"passed\":true,\"osIdentity\":true,\"packagedComActivation\":true,\"packageFullName\":\"" << packageName << "\",\"selected\":" << (argc-3) << "}";
    return 0;
}
