#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <vector>
#include <string>
#include <fstream>
#include "json.hpp"

int wmain(int argc, wchar_t** argv) {
    if (argc < 4) return 2;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 3;
    CLSID clsid{}; if (FAILED(CLSIDFromString(argv[1], &clsid))) return 4;
    bool modern = argc >= 6 && std::wstring(argv[3]) == L"--modern-dll";
    int firstPath = modern ? 5 : 3;
    std::vector<std::wstring> paths;
    if (argc == 5 && std::wstring(argv[3]) == L"--paths-file") {
        std::ifstream file(argv[4], std::ios::binary); auto document = nlohmann::json::parse(file);
        for (const auto& entry : document) {
            auto utf8 = entry.get<std::string>();
            int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), static_cast<int>(utf8.size()), nullptr, 0);
            if (count <= 0) return 5;
            std::wstring path(count, L'\0'); MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, utf8.data(), static_cast<int>(utf8.size()), path.data(), count); paths.push_back(path);
        }
    } else for (int index = firstPath; index < argc; ++index) paths.push_back(argv[index]);
    std::vector<PIDLIST_ABSOLUTE> ids;
    for (const auto& path : paths) {
        auto id = ILCreateFromPathW(path.c_str()); if (!id) return 5; ids.push_back(id);
    }
    IShellItemArray* selection = nullptr;
    std::vector<PCIDLIST_ABSOLUTE> constantIds(ids.begin(), ids.end());
    HRESULT hr = SHCreateShellItemArrayFromIDLists(static_cast<UINT>(ids.size()), constantIds.data(), &selection);
    for (auto id : ids) ILFree(id);
    if (FAILED(hr)) return 6;
    if (modern) {
        auto library = LoadLibraryW(argv[4]); if (!library) return 9;
        HRESULT(__stdcall* getFactory)(REFCLSID,REFIID,void**) = nullptr;
        auto address = GetProcAddress(library,"DllGetClassObject");
        static_assert(sizeof(getFactory) == sizeof(address)); memcpy(&getFactory,&address,sizeof(address));
        GUID rootId{}; CLSIDFromString(L"{c708b15d-0d3e-47ab-8fc8-c22d21ff1170}",&rootId);
        IClassFactory* factory = nullptr; if (!getFactory || FAILED(getFactory(rootId,IID_PPV_ARGS(&factory)))) return 10;
        IExplorerCommand* rootCommand = nullptr; hr = factory->CreateInstance(nullptr,IID_PPV_ARGS(&rootCommand)); factory->Release(); if (FAILED(hr)) return 11;
        EXPCMDFLAGS flags{}; rootCommand->GetFlags(&flags); if (!(flags & ECF_HASSUBCOMMANDS)) return 12;
        IEnumExplorerCommand* children = nullptr; hr = rootCommand->EnumSubCommands(&children); rootCommand->Release(); if (FAILED(hr)) return 13;
        IExplorerCommand* child = nullptr; bool found = false;
        while (children->Next(1,&child,nullptr) == S_OK) {
            GUID name{}; child->GetCanonicalName(&name);
            if (name == clsid) { found = true; break; } child->Release(); child = nullptr;
        }
        children->Release(); if (!found) return 14;
        EXPCMDSTATE state{}; hr = child->GetState(selection,TRUE,&state);
        if (FAILED(hr) || state != ECS_ENABLED) return 15;
        for (int attempt = 0; attempt < 100; ++attempt) {
            hr = child->Invoke(selection,nullptr);
            if (SUCCEEDED(hr) || hr != REGDB_E_CLASSNOTREG) break;
            Sleep(100);
        }
        child->Release(); selection->Release(); FreeLibrary(library); CoUninitialize();
        if (FAILED(hr)) { fwprintf(stderr,L"Modern invocation HRESULT: 0x%08lx\n", static_cast<unsigned long>(hr)); return 16; }
        std::ofstream out(argv[2]); out << "{\"passed\":true,\"modernInterface\":true,\"selected\":" << paths.size() << "}"; return 0;
    }
    IExecuteCommand* command = nullptr;
    for (int attempt = 0; attempt < 100; ++attempt) {
        hr = CoCreateInstance(clsid, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&command));
        if (SUCCEEDED(hr)) break;
        Sleep(100);
    }
    if (FAILED(hr)) { fwprintf(stderr, L"COM activation HRESULT: 0x%08lx\n", static_cast<unsigned long>(hr)); return 7; }
    IObjectWithSelection* receiver = nullptr;
    hr = command->QueryInterface(IID_PPV_ARGS(&receiver));
    if (SUCCEEDED(hr)) hr = receiver->SetSelection(selection);
    if (SUCCEEDED(hr)) {
        command->SetKeyState(0); command->SetShowWindow(SW_SHOWNORMAL);
        command->SetParameters(L"ignored injection-like string '$ &'");
        command->SetDirectory(L"C:\\"); hr = command->Execute();
    }
    if (receiver) receiver->Release();
    command->Release(); selection->Release(); CoUninitialize();
    if (FAILED(hr)) { fwprintf(stderr, L"Execute HRESULT: 0x%08lx\n", static_cast<unsigned long>(hr)); return 8; }
    std::ofstream out(argv[2]); out << "{\"passed\":true,\"selected\":" << paths.size() << "}";
    return 0;
}
