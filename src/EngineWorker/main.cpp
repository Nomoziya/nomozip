#define NOMINMAX
#include <windows.h>
#include <oleauto.h>
#include <propidl.h>
#include <fcntl.h>
#include <io.h>
#include <atomic>
#include <filesystem>
#include <iostream>
#include <mutex>
#include <thread>
#include <vector>
#include <algorithm>
#include "Common/MyInitGuid.h"
#include "7zip/Archive/IArchive.h"
#include "7zip/IPassword.h"
#include "json.hpp"

using json = nlohmann::json;
namespace fs = std::filesystem;
static std::atomic_bool cancelled{false};
static std::mutex outputMutex;
static void emit(json value) {
    value["protocol"] = 1;
    std::lock_guard<std::mutex> lock(outputMutex);
    std::cout << value.dump() << '\n' << std::flush;
}
static std::wstring wide(const std::string& text) {
    if (text.empty()) return {};
    int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), (int)text.size(), nullptr, 0);
    if (!size) throw std::runtime_error("invalid_utf8");
    std::wstring result(size, 0);
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), (int)text.size(), result.data(), size);
    return result;
}
static std::string utf8(const std::wstring& text) {
    if (text.empty()) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), (int)text.size(), nullptr, 0, nullptr, nullptr);
    std::string result(size, 0);
    WideCharToMultiByte(CP_UTF8, 0, text.data(), (int)text.size(), result.data(), size, nullptr, nullptr);
    return result;
}
struct Variant {
    PROPVARIANT value{};
    Variant() = default;
    Variant(const Variant&) = delete;
    Variant& operator=(const Variant&) = delete;
    Variant(Variant&& other) noexcept : value(other.value) { PropVariantInit(&other.value); }
    ~Variant() { PropVariantClear(&value); }
    std::wstring text() const { return value.vt == VT_BSTR && value.bstrVal ? std::wstring(value.bstrVal, SysStringLen(value.bstrVal)) : L""; }
    bool boolean() const { return value.vt == VT_BOOL && value.boolVal != VARIANT_FALSE; }
    UInt64 number() const { return value.vt == VT_UI8 ? value.uhVal.QuadPart : value.vt == VT_UI4 ? value.ulVal : 0; }
};
template<class T> struct ComPtr {
    T* ptr = nullptr;
    ~ComPtr() { if (ptr) ptr->Release(); }
    T* operator->() const { return ptr; }
    void reset() { if (ptr) ptr->Release(); ptr = nullptr; }
};
#define REFCOUNT_IMPL \
    std::atomic<ULONG> refs{1}; \
    ULONG STDMETHODCALLTYPE AddRef() noexcept override { return ++refs; } \
    ULONG STDMETHODCALLTYPE Release() noexcept override { auto count = --refs; if (!count) delete this; return count; }

class InputStream final : public IInStream {
    HANDLE file;
public:
    explicit InputStream(HANDLE handle) : file(handle) {}
    ~InputStream() { if (file != INVALID_HANDLE_VALUE) CloseHandle(file); }
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_IInStream || id == IID_ISequentialInStream) *object = static_cast<IInStream*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Read(void* data, UInt32 size, UInt32* processed) noexcept override {
        if (processed) *processed = 0;
        if (cancelled) return E_ABORT;
        DWORD count = 0;
        if (!ReadFile(file, data, size, &count, nullptr)) return HRESULT_FROM_WIN32(GetLastError());
        if (processed) *processed = count;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Seek(Int64 offset, UInt32 origin, UInt64* position) noexcept override {
        LARGE_INTEGER move{}, result{}; move.QuadPart = offset;
        if (!SetFilePointerEx(file, move, &result, origin)) return HRESULT_FROM_WIN32(GetLastError());
        if (position) *position = result.QuadPart;
        return S_OK;
    }
};
class OutputStream final : public ISequentialOutStream {
    HANDLE file;
public:
    explicit OutputStream(HANDLE handle) : file(handle) {}
    ~OutputStream() { CloseHandle(file); }
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_ISequentialOutStream) *object = static_cast<ISequentialOutStream*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Write(const void* data, UInt32 size, UInt32* processed) noexcept override {
        if (processed) *processed = 0;
        if (cancelled) return E_ABORT;
        DWORD count = 0;
        if (!WriteFile(file, data, size, &count, nullptr)) return HRESULT_FROM_WIN32(GetLastError());
        if (processed) *processed = count;
        return S_OK;
    }
};
struct Context {
    bool passwordDefined = false, passwordRequested = false, missingVolume = false;
    std::wstring password;
    fs::path archivePath;
    ~Context() { if (!password.empty()) SecureZeroMemory(password.data(), password.size() * sizeof(wchar_t)); }
    HRESULT getPassword(BSTR* result) noexcept {
        *result = nullptr; passwordRequested = true;
        if (cancelled || !passwordDefined) return E_ABORT;
        *result = SysAllocStringLen(password.data(), (UINT)password.size());
        return *result ? S_OK : E_OUTOFMEMORY;
    }
};
class OpenCallback final : public IArchiveOpenCallback, public ICryptoGetTextPassword, public IArchiveOpenVolumeCallback {
    Context& context;
public:
    explicit OpenCallback(Context& ctx) : context(ctx) {}
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_IArchiveOpenCallback) *object = static_cast<IArchiveOpenCallback*>(this);
        else if (id == IID_ICryptoGetTextPassword) *object = static_cast<ICryptoGetTextPassword*>(this);
        else if (id == IID_IArchiveOpenVolumeCallback) *object = static_cast<IArchiveOpenVolumeCallback*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetTotal(const UInt64*, const UInt64*) noexcept override { return cancelled ? E_ABORT : S_OK; }
    HRESULT STDMETHODCALLTYPE SetCompleted(const UInt64*, const UInt64*) noexcept override { return cancelled ? E_ABORT : S_OK; }
    HRESULT STDMETHODCALLTYPE CryptoGetTextPassword(BSTR* result) noexcept override { return context.getPassword(result); }
    HRESULT STDMETHODCALLTYPE GetProperty(PROPID id, PROPVARIANT* value) noexcept override {
        PropVariantInit(value);
        if (id == kpidName) { value->vt = VT_BSTR; value->bstrVal = SysAllocString(context.archivePath.filename().c_str()); }
        else if (id == kpidSize) {
            std::error_code error; auto size = fs::file_size(context.archivePath, error);
            if (!error) { value->vt = VT_UI8; value->uhVal.QuadPart = size; }
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetStream(const wchar_t* name, IInStream** stream) noexcept override {
        *stream = nullptr;
        try {
            fs::path relative(name);
            if (relative.has_parent_path() || relative.is_absolute()) return E_ACCESSDENIED;
            HANDLE handle = CreateFileW((context.archivePath.parent_path() / relative).c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE) { context.missingVolume = true; return S_FALSE; }
            *stream = new InputStream(handle); return S_OK;
        } catch (...) { return E_FAIL; }
    }
};

static Variant property(IInArchive* archive, UInt32 index, PROPID id) {
    Variant result;
    if (archive->GetProperty(index, id, &result.value) != S_OK) throw std::runtime_error("property_failed");
    return result;
}
static fs::path safeRelative(std::wstring name) {
    std::replace(name.begin(), name.end(), L'/', L'\\');
    fs::path path(name);
    if (name.empty() || path.is_absolute() || path.has_root_path() || name.find(L':') != std::wstring::npos) throw std::runtime_error("unsafe_path");
    for (const auto& part : path) {
        auto component = part.wstring();
        if (component == L".." || component == L"." || component.empty() || component.back() == L'.' || component.back() == L' ') throw std::runtime_error("unsafe_path");
        auto stem = component.substr(0, component.find(L'.'));
        std::transform(stem.begin(), stem.end(), stem.begin(), towupper);
        if (stem == L"CON" || stem == L"PRN" || stem == L"AUX" || stem == L"NUL" || (stem.size() == 4 && (stem.substr(0, 3) == L"COM" || stem.substr(0, 3) == L"LPT") && stem[3] >= L'1' && stem[3] <= L'9')) throw std::runtime_error("unsafe_path");
    }
    return path;
}
class ExtractCallback final : public IArchiveExtractCallback, public ICryptoGetTextPassword {
    Context& context;
    IInArchive* archive;
    fs::path output;
    OutputStream* currentStream = nullptr;
    UInt64 total = 0;
    ULONGLONG lastProgress = 0;
public:
    std::string error;
    json failures = json::array();
    UInt32 currentIndex = 0, successfulItems = 0;
    ExtractCallback(Context& ctx, IInArchive* arc, fs::path destination) : context(ctx), archive(arc), output(std::move(destination)) {}
    ~ExtractCallback() { if (currentStream) currentStream->Release(); }
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_IArchiveExtractCallback || id == IID_IProgress) *object = static_cast<IArchiveExtractCallback*>(this);
        else if (id == IID_ICryptoGetTextPassword) *object = static_cast<ICryptoGetTextPassword*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetTotal(UInt64 value) noexcept override { total = value; return cancelled ? E_ABORT : S_OK; }
    HRESULT STDMETHODCALLTYPE SetCompleted(const UInt64* completed) noexcept override {
        if (cancelled) return E_ABORT;
        try {
            auto now = GetTickCount64();
            if (now - lastProgress >= 100) { lastProgress = now; emit({{"kind", "progress"}, {"completed", completed ? *completed : 0}, {"total", total}}); }
        } catch (...) { return E_FAIL; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE CryptoGetTextPassword(BSTR* result) noexcept override { return context.getPassword(result); }
    HRESULT STDMETHODCALLTYPE GetStream(UInt32 index, ISequentialOutStream** stream, Int32 mode) noexcept override {
        *stream = nullptr; currentIndex = index;
        if (cancelled) return E_ABORT;
        if (currentStream) { currentStream->Release(); currentStream = nullptr; }
        try {
            if (mode != NArchive::NExtract::NAskMode::kExtract) return S_OK;
            auto pathName = property(archive, index, kpidPath).text();
            if (pathName.empty()) pathName = context.archivePath.stem().wstring();
            if (!property(archive, index, kpidSymLink).text().empty() || !property(archive, index, kpidHardLink).text().empty() || property(archive, index, kpidIsAltStream).boolean()) { error = "unsafe_path"; return E_ACCESSDENIED; }
            auto relative = safeRelative(pathName);
            auto target = output / relative;
            auto cursor = output;
            for (const auto& part : relative) {
                cursor /= part;
                DWORD attributes = GetFileAttributesW(cursor.c_str());
                if (attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT)) { error = "unsafe_path"; return E_ACCESSDENIED; }
            }
            if (property(archive, index, kpidIsDir).boolean()) { fs::create_directories(target); return S_OK; }
            fs::create_directories(target.parent_path());
            HANDLE handle = CreateFileW(target.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (handle == INVALID_HANDLE_VALUE) { error = GetLastError() == ERROR_FILE_EXISTS ? "file_exists" : "write_failed"; return HRESULT_FROM_WIN32(GetLastError()); }
            currentStream = new OutputStream(handle);
            *stream = currentStream; currentStream->AddRef(); return S_OK;
        } catch (const std::exception& e) { error = std::string(e.what()) == "unsafe_path" ? "unsafe_path" : "write_failed"; return E_FAIL; }
    }
    HRESULT STDMETHODCALLTYPE PrepareOperation(Int32) noexcept override { return cancelled ? E_ABORT : S_OK; }
    HRESULT STDMETHODCALLTYPE SetOperationResult(Int32 result) noexcept override {
        if (currentStream) { currentStream->Release(); currentStream = nullptr; }
        try {
            if (result == NArchive::NExtract::NOperationResult::kOK) ++successfulItems;
            else {
                std::string code = result == NArchive::NExtract::NOperationResult::kWrongPassword ? "wrong_password" :
                    result == NArchive::NExtract::NOperationResult::kUnsupportedMethod ? "unsupported_method" :
                    context.passwordRequested && (result == NArchive::NExtract::NOperationResult::kCRCError || result == NArchive::NExtract::NOperationResult::kDataError) ? "password_or_corrupt" : "corrupt_archive";
                if (error.empty()) error = code;
                failures.push_back({{"index", currentIndex}, {"code", code}});
            }
        } catch (...) { return E_FAIL; }
        return cancelled ? E_ABORT : S_OK;
    }
};
struct Format { GUID id; std::wstring name, extensions; };
#include "Compression.h"
template<class T> static T exported(HMODULE library, const char* name) {
    auto address = GetProcAddress(library, name);
    T result = nullptr;
    static_assert(sizeof(result) == sizeof(address));
    memcpy(&result, &address, sizeof(result));
    return result;
}
static json run(json& request, const wchar_t* dllPath) {
    if (request.value("protocol", 0) != 1) return {{"kind", "result"}, {"status", "error"}, {"code", "protocol_mismatch"}};
    HMODULE library = LoadLibraryExW(dllPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!library) return {{"kind", "result"}, {"status", "error"}, {"code", "engine_load_failed"}};
    // Module remains loaded until process exit; all COM objects must be released first.
    using CountFunc = HRESULT (WINAPI*)(UInt32*);
    using PropertyFunc = HRESULT (WINAPI*)(UInt32, PROPID, PROPVARIANT*);
    using CreateFunc = HRESULT (WINAPI*)(const GUID*, const GUID*, void**);
    auto countFunc = exported<CountFunc>(library, "GetNumberOfFormats");
    auto propFunc = exported<PropertyFunc>(library, "GetHandlerProperty2");
    auto createFunc = exported<CreateFunc>(library, "CreateObject");
    if (!countFunc || !propFunc || !createFunc) return {{"kind", "result"}, {"status", "error"}, {"code", "engine_interface_missing"}};
    UInt32 count = 0; if (countFunc(&count) != S_OK) throw std::runtime_error("engine_interface_missing");
    std::vector<Format> formats;
    for (UInt32 index = 0; index < count; ++index) {
        Variant id, name, extensions;
        propFunc(index, NArchive::NHandlerPropID::kClassID, &id.value);
        propFunc(index, NArchive::NHandlerPropID::kName, &name.value);
        propFunc(index, NArchive::NHandlerPropID::kExtension, &extensions.value);
        if (id.value.vt != VT_BSTR || SysStringByteLen(id.value.bstrVal) != sizeof(GUID)) continue;
        Format format{}; memcpy(&format.id, id.value.bstrVal, sizeof(GUID)); format.name = name.text(); format.extensions = extensions.text();
        formats.push_back(format);
    }
    auto operation = request.at("operation").get<std::string>();
    if (operation == "capabilities") {
        json result = json::array(); for (auto& format : formats) result.push_back({{"name", utf8(format.name)}, {"extensions", utf8(format.extensions)}});
        return {{"kind", "result"}, {"status", "success"}, {"formats", result}};
    }
    if (operation == "create") return createArchive(request, formats, createFunc);
    if (operation != "list" && operation != "test" && operation != "extract") throw std::runtime_error("unsupported_operation");
    Context context;
    context.archivePath = fs::absolute(wide(request.at("archivePath").get<std::string>()));
    if (request.contains("password") && !request["password"].is_null()) {
        context.passwordDefined = true;
        auto& secret = request["password"].get_ref<std::string&>();
        context.password = wide(secret);
        if (!secret.empty()) SecureZeroMemory(secret.data(), secret.size());
        request.erase("password");
    }
    HANDLE handle = CreateFileW(context.archivePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return {{"kind", "result"}, {"status", "error"}, {"code", "archive_read_failed"}};
    ComPtr<IInStream> input; input.ptr = new InputStream(handle);
    std::wstring extension = context.archivePath.extension().wstring(); if (!extension.empty()) extension.erase(0, 1);
    std::transform(extension.begin(), extension.end(), extension.begin(), towlower);
    auto matches = [&](const Format& value) { return !extension.empty() && (L" " + value.extensions + L" ").find(L" " + extension + L" ") != std::wstring::npos; };
    std::stable_sort(formats.begin(), formats.end(), [&](const Format& a, const Format& b) { return matches(a) > matches(b); });
    ComPtr<IArchiveOpenCallback> open; open.ptr = new OpenCallback(context);
    ComPtr<IInArchive> archive;
    std::wstring usedFormat;
    for (const auto& format : formats) {
        if (cancelled) break;
        context.passwordRequested = false; context.missingVolume = false;
        input->Seek(0, STREAM_SEEK_SET, nullptr);
        if (createFunc(&format.id, &IID_IInArchive, reinterpret_cast<void**>(&archive.ptr)) != S_OK) continue;
        UInt64 maxCheck = 1 << 23;
        HRESULT opened = archive->Open(input.ptr, &maxCheck, open.ptr);
        if (opened == S_OK) { usedFormat = format.name; break; }
        archive->Close(); archive.reset();
        if (context.passwordRequested || context.missingVolume || (matches(format) && opened != S_FALSE)) break;
    }
    if (!archive.ptr) return {{"kind", "result"}, {"status", cancelled ? "cancelled" : "error"}, {"code", cancelled ? "cancelled" : context.missingVolume ? "missing_volume" : context.passwordRequested ? context.passwordDefined ? "password_or_corrupt" : "password_required" : "unsupported_or_corrupt"}};
    UInt32 itemCount = 0; archive->GetNumberOfItems(&itemCount);
    if (operation == "list") {
        json items = json::array();
        for (UInt32 index = 0; index < itemCount && !cancelled; ++index) items.push_back({{"index", index}, {"path", utf8(property(archive.ptr, index, kpidPath).text())}, {"size", property(archive.ptr, index, kpidSize).number()}, {"directory", property(archive.ptr, index, kpidIsDir).boolean()}, {"encrypted", property(archive.ptr, index, kpidEncrypted).boolean()}});
        archive->Close();
        return {{"kind", "result"}, {"status", cancelled ? "cancelled" : "success"}, {"format", utf8(usedFormat)}, {"items", items}};
    }
    fs::path output = operation == "extract" ? fs::absolute(wide(request.at("outputPath").get<std::string>())) : fs::path();
    if (operation == "extract") fs::create_directories(output);
    ComPtr<IArchiveExtractCallback> callback;
    auto* extract = new ExtractCallback(context, archive.ptr, output); callback.ptr = extract;
    HRESULT result = archive->Extract(nullptr, 0xFFFFFFFF, operation == "test" ? 1 : 0, callback.ptr);
    archive->Close();
    std::string code = cancelled ? "cancelled" : context.missingVolume ? "missing_volume" : context.passwordRequested && !context.passwordDefined ? "password_required" : !extract->error.empty() ? extract->error : result != S_OK ? "engine_error" : "ok";
    return {{"kind", "result"}, {"status", cancelled ? "cancelled" : code == "ok" ? "success" : "error"}, {"code", code}, {"hresult", (uint32_t)result}, {"format", utf8(usedFormat)}, {"itemCount", itemCount}, {"successfulItems", extract->successfulItems}, {"failures", extract->failures}};
}
int wmain(int argc, wchar_t** argv) {
    _setmode(_fileno(stdin), _O_BINARY); _setmode(_fileno(stdout), _O_BINARY);
    if (argc != 2) { emit({{"kind", "result"}, {"status", "error"}, {"code", "engine_path_required"}}); return 1; }
    try {
        std::string line; if (!std::getline(std::cin, line)) return 1;
        json request = json::parse(line);
        if (!line.empty()) SecureZeroMemory(line.data(), line.size());
        std::thread([] {
            std::string control;
            while (std::getline(std::cin, control)) {
                auto value = json::parse(control, nullptr, false);
                if (!value.is_discarded() && value.value("operation", "") == "cancel") cancelled = true;
            }
        }).detach();
        auto result = run(request, argv[1]);
        bool success = result.value("status", "") == "success";
        emit(std::move(result));
        // The process is single-request. Exit closes the waiting pipe reader.
        ExitProcess(success ? 0 : 1);
    } catch (...) {
        emit({{"kind", "result"}, {"status", "error"}, {"code", "invalid_request_or_worker_error"}});
        ExitProcess(1);
    }
}
