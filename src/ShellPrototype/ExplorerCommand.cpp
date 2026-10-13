// Modern-menu technical prototype; not included in the lightweight payload.
#ifndef UNICODE
#define UNICODE
#endif
#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>

static LONG objects = 0;
static const GUID Root = {0xc708b15d,0x0d3e,0x47ab,{0x8f,0xc8,0xc2,0x2d,0x21,0xff,0x11,0x70}};
static const GUID Actions[] = {
    {0xc708b15d,0x0d3e,0x47ab,{0x8f,0xc8,0xc2,0x2d,0x21,0xff,0x11,0x71}},
    {0xc708b15d,0x0d3e,0x47ab,{0x8f,0xc8,0xc2,0x2d,0x21,0xff,0x11,0x72}},
    {0xc708b15d,0x0d3e,0x47ab,{0x8f,0xc8,0xc2,0x2d,0x21,0xff,0x11,0x73}}};

class Node final : public IExplorerCommand {
    LONG refs = 1; int action;
public:
    explicit Node(int value) : action(value) { InterlockedIncrement(&objects); }
    ~Node() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        if (!result) return E_POINTER;
        *result = nullptr;
        if (id != IID_IUnknown && id != IID_IExplorerCommand) return E_NOINTERFACE;
        *result = static_cast<IExplorerCommand*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { auto left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, PWSTR* title) override {
        static const wchar_t* names[] = {L"解压到当前目录",L"解压到同名文件夹",L"压缩…"};
        return SHStrDupW(action < 0 ? L"nomozip" : names[action], title);
    }
    HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, PWSTR* icon) override { *icon = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, PWSTR* text) override { *text = nullptr; return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* id) override { *id = action < 0 ? Root : Actions[action]; return S_OK; }
    HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        *state = ECS_HIDDEN; DWORD count = 0, attrs = 0;
        if (!items || FAILED(items->GetCount(&count)) || count == 0 || count > 10000) return S_OK;
        if (FAILED(items->GetAttributes(SIATTRIBFLAGS_AND, SFGAO_FILESYSTEM, &attrs)) || !(attrs & SFGAO_FILESYSTEM)) return S_OK;
        if (action >= 0 && action < 2) {
            for (DWORD index = 0; index < count; ++index) {
                IShellItem* item = nullptr; PWSTR path = nullptr;
                if (FAILED(items->GetItemAt(index, &item))) return S_OK;
                auto hr = item->GetDisplayName(SIGDN_FILESYSPATH, &path); item->Release();
                if (FAILED(hr)) return S_OK;
                auto ext = PathFindExtensionW(path);
                // ZIP/7z files can also be Shell folders; use physical directory attributes.
                auto fileAttrs = GetFileAttributesW(path);
                bool supported = fileAttrs != INVALID_FILE_ATTRIBUTES && !(fileAttrs & FILE_ATTRIBUTE_DIRECTORY) && (!lstrcmpiW(ext,L".7z") || !lstrcmpiW(ext,L".zip") || !lstrcmpiW(ext,L".rar"));
                CoTaskMemFree(path); if (!supported) return S_OK;
            }
        }
        *state = ECS_ENABLED; return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override {
        if (action < 0) return E_NOTIMPL;
        IExecuteCommand* command = nullptr;
        auto hr = CoCreateInstance(Actions[action], nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&command));
        if (FAILED(hr)) return hr;
        IObjectWithSelection* receiver = nullptr; hr = command->QueryInterface(IID_PPV_ARGS(&receiver));
        if (SUCCEEDED(hr)) hr = receiver->SetSelection(items);
        if (SUCCEEDED(hr)) hr = command->Execute();
        if (receiver) receiver->Release();
        command->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override { *flags = action < 0 ? ECF_HASSUBCOMMANDS : ECF_DEFAULT; return S_OK; }
    HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** result) override;
};

class Commands final : public IEnumExplorerCommand {
    LONG refs = 1; ULONG position = 0;
public:
    explicit Commands(ULONG value = 0) : position(value) { InterlockedIncrement(&objects); }
    ~Commands() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        *result = nullptr; if (id != IID_IUnknown && id != IID_IEnumExplorerCommand) return E_NOINTERFACE;
        *result = static_cast<IEnumExplorerCommand*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { auto left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE Next(ULONG count, IExplorerCommand** result, ULONG* fetched) override {
        if (!result || (count > 1 && !fetched)) return E_POINTER;
        ULONG taken = 0; while (taken < count && position < 3) result[taken++] = new Node(position++);
        if (fetched) *fetched = taken;
        return taken == count ? S_OK : S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override { ULONG remaining = 3 - position; position += count < remaining ? count : remaining; return count <= remaining ? S_OK : S_FALSE; }
    HRESULT STDMETHODCALLTYPE Reset() override { position = 0; return S_OK; }
    HRESULT STDMETHODCALLTYPE Clone(IEnumExplorerCommand** result) override { *result = new Commands(position); return S_OK; }
};
HRESULT Node::EnumSubCommands(IEnumExplorerCommand** result) { *result = nullptr; if (action >= 0) return E_NOTIMPL; *result = new Commands(); return S_OK; }

class Factory final : public IClassFactory {
    LONG refs = 1;
public:
    Factory() { InterlockedIncrement(&objects); }
    ~Factory() { InterlockedDecrement(&objects); }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** result) override {
        *result = nullptr; if (id != IID_IUnknown && id != IID_IClassFactory) return E_NOINTERFACE;
        *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&refs); }
    ULONG STDMETHODCALLTYPE Release() override { auto left = InterlockedDecrement(&refs); if (!left) delete this; return left; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID id, void** result) override {
        *result = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        auto node = new Node(-1); auto hr = node->QueryInterface(id,result); node->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&objects); else InterlockedDecrement(&objects); return S_OK; }
};
extern "C" __declspec(dllexport) HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) {
    *result = nullptr; if (clsid != Root) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new Factory(); auto hr = factory->QueryInterface(iid,result); factory->Release(); return hr;
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllCanUnloadNow() { return objects == 0 ? S_OK : S_FALSE; }
