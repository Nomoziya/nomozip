struct CreateEntry { fs::path source; std::wstring name; bool directory; UInt64 size; WIN32_FILE_ATTRIBUTE_DATA attributes; };
class ArchiveOutputStream final : public IOutStream {
    HANDLE file;
public:
    explicit ArchiveOutputStream(HANDLE value) : file(value) {}
    ~ArchiveOutputStream() { CloseHandle(file); }
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_IOutStream || id == IID_ISequentialOutStream) *object = static_cast<IOutStream*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Write(const void* data, UInt32 size, UInt32* processed) noexcept override {
        if (processed) *processed = 0;
        if (checkpoint() != S_OK) return E_ABORT;
        DWORD count = 0;
        if (!WriteFile(file, data, size, &count, nullptr)) return HRESULT_FROM_WIN32(GetLastError());
        if (processed) *processed = count;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Seek(Int64 offset, UInt32 origin, UInt64* position) noexcept override {
        LARGE_INTEGER move{}, result{}; move.QuadPart = offset;
        if (!SetFilePointerEx(file, move, &result, origin)) return HRESULT_FROM_WIN32(GetLastError());
        if (position) *position = result.QuadPart;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetSize(UInt64 size) noexcept override {
        LARGE_INTEGER move{}, previous{}; move.QuadPart = size;
        if (!SetFilePointerEx(file, {}, &previous, FILE_CURRENT) || !SetFilePointerEx(file, move, nullptr, FILE_BEGIN) || !SetEndOfFile(file)) return HRESULT_FROM_WIN32(GetLastError());
        return SetFilePointerEx(file, previous, nullptr, FILE_BEGIN) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
    }
};
class CreateCallback final : public IArchiveUpdateCallback, public ICryptoGetTextPassword2, public ICryptoGetTextPassword {
    Context& context;
    const std::vector<CreateEntry>& entries;
    UInt64 total = 0;
    const std::vector<UInt32>* previous;
public:
    std::string error;
    CreateCallback(Context& value, const std::vector<CreateEntry>& items, const std::vector<UInt32>* old = nullptr) : context(value), entries(items), previous(old) {}
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** object) noexcept override {
        *object = nullptr;
        if (id == IID_IUnknown || id == IID_IArchiveUpdateCallback || id == IID_IProgress) *object = static_cast<IArchiveUpdateCallback*>(this);
        else if (id == IID_ICryptoGetTextPassword2) *object = static_cast<ICryptoGetTextPassword2*>(this);
        else if (id == IID_ICryptoGetTextPassword) *object = static_cast<ICryptoGetTextPassword*>(this);
        if (!*object) return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetTotal(UInt64 value) noexcept override { total = value; return checkpoint(); }
    HRESULT STDMETHODCALLTYPE SetCompleted(const UInt64* value) noexcept override {
        if (checkpoint() != S_OK) return E_ABORT;
        try { emit({{"kind", "progress"}, {"completed", value ? *value : 0}, {"total", total}}); } catch (...) { return E_FAIL; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetUpdateItemInfo(UInt32 item, Int32* data, Int32* properties, UInt32* index) noexcept override {
        if(item>=entries.size()) return E_INVALIDARG;
        const auto old=previous ? (*previous)[item] : 0xFFFFFFFF;
        if (data) *data = old==0xFFFFFFFF;
        if (properties) *properties = old==0xFFFFFFFF;
        if (index) *index = old;
        return checkpoint();
    }
    HRESULT STDMETHODCALLTYPE GetProperty(UInt32 index, PROPID propertyId, PROPVARIANT* value) noexcept override {
        PropVariantInit(value);
        if (checkpoint() != S_OK) return E_ABORT;
        if (index >= entries.size()) return E_INVALIDARG;
        const auto& entry = entries[index];
        switch (propertyId) {
            case kpidPath: value->vt = VT_BSTR; value->bstrVal = SysAllocStringLen(entry.name.data(), (UINT)entry.name.size()); return value->bstrVal ? S_OK : E_OUTOFMEMORY;
            case kpidIsDir: value->vt = VT_BOOL; value->boolVal = entry.directory ? VARIANT_TRUE : VARIANT_FALSE; break;
            case kpidSize: value->vt = VT_UI8; value->uhVal.QuadPart = entry.size; break;
            case kpidMTime: value->vt = VT_FILETIME; value->filetime = entry.attributes.ftLastWriteTime; break;
            case kpidAttrib: value->vt = VT_UI4; value->ulVal = entry.attributes.dwFileAttributes; break;
            case kpidIsAnti: value->vt = VT_BOOL; value->boolVal = VARIANT_FALSE; break;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetStream(UInt32 index, ISequentialInStream** stream) noexcept override {
        *stream = nullptr;
        if (checkpoint() != S_OK) return E_ABORT;
        if (index >= entries.size()) return E_INVALIDARG;
        const auto& entry = entries[index];
        if (entry.directory) return S_OK;
        HANDLE handle = CreateFileW(entry.source.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (handle == INVALID_HANDLE_VALUE) { error = "source_read_failed"; return HRESULT_FROM_WIN32(GetLastError()); }
        LARGE_INTEGER size{};
        if (!GetFileSizeEx(handle, &size) || (UInt64)size.QuadPart != entry.size) { CloseHandle(handle); error = "source_changed"; return E_FAIL; }
        *stream = new InputStream(handle); return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetOperationResult(Int32 result) noexcept override {
        if (result != 0) error = "compression_failed";
        return checkpoint();
    }
    HRESULT STDMETHODCALLTYPE CryptoGetTextPassword2(Int32* defined, BSTR* password) noexcept override {
        *password = nullptr; *defined = context.passwordDefined ? 1 : 0;
        return context.passwordDefined ? context.getPassword(password) : cancelled ? E_ABORT : S_OK;
    }
    HRESULT STDMETHODCALLTYPE CryptoGetTextPassword(BSTR* password) noexcept override {return context.getPassword(password);}
};
using CreateArchiveFactory = HRESULT (WINAPI*)(const GUID*, const GUID*, void**);
static json createArchive(json& request, const std::vector<Format>& formats, CreateArchiveFactory factory) {
    auto formatName = request.at("format").get<std::string>();
    if (formatName != "7z" && formatName != "zip") throw std::runtime_error("unsupported_format");
    auto format = std::find_if(formats.begin(), formats.end(), [&](const Format& value) { return utf8(value.name) == (formatName == "zip" ? "zip" : "7z"); });
    if (format == formats.end()) throw std::runtime_error("unsupported_format");
    Context context;
    if (request.contains("password") && !request["password"].is_null()) {
        auto& secret = request["password"].get_ref<std::string&>(); context.password = wide(secret); context.passwordDefined = !secret.empty();
        if (!secret.empty()) SecureZeroMemory(secret.data(), secret.size());
        request.erase("password");
    }
    if (formatName == "zip" && std::any_of(context.password.begin(), context.password.end(), [](wchar_t value) { return value > 127; })) return {{"kind", "result"}, {"status", "error"}, {"code", "zip_password_ascii_only"}};
    std::vector<CreateEntry> entries;
    for (const auto& item : request.at("sourceEntries")) {
        auto source = fs::absolute(wide(item.at("sourcePath").get<std::string>()));
        auto name = safeRelative(wide(item.at("path").get<std::string>())).wstring();
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        if (!GetFileAttributesExW(source.c_str(), GetFileExInfoStandard, &attributes) || (attributes.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)) throw std::runtime_error("source_read_failed");
        bool directory = (attributes.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
        entries.push_back({source, name, directory, directory ? 0 : ((UInt64)attributes.nFileSizeHigh << 32) | attributes.nFileSizeLow, attributes});
    }
    if (entries.empty()) throw std::runtime_error("empty_sources");
    ComPtr<IOutArchive> archive;
    if (factory(&format->id, &IID_IOutArchive, reinterpret_cast<void**>(&archive.ptr)) != S_OK) throw std::runtime_error("compression_not_supported");
    ComPtr<ISetProperties> properties;
    if (archive->QueryInterface(IID_ISetProperties, reinterpret_cast<void**>(&properties.ptr)) != S_OK) throw std::runtime_error("compression_not_supported");
    const wchar_t* names[3] = {L"x", L"he", L"em"};
    Variant level, encryption;
    level.value.vt = VT_UI4; level.value.ulVal = request.value("compressionLevel", 5);
    PROPVARIANT values[2] = {level.value, {}};
    UInt32 propertyCount = 1;
    if (formatName == "7z") { values[1].vt = VT_BOOL; values[1].boolVal = context.passwordDefined && request.value("encryptHeaders", true) ? VARIANT_TRUE : VARIANT_FALSE; propertyCount = 2; }
    else if (context.passwordDefined) { names[1] = names[2]; encryption.value.vt = VT_BSTR; encryption.value.bstrVal = SysAllocString(L"AES256"); values[1] = encryption.value; propertyCount = 2; }
    if (properties->SetProperties(names, values, propertyCount) != S_OK) throw std::runtime_error("invalid_compression_options");
    auto outputPath = fs::absolute(wide(request.at("outputPath").get<std::string>()));
    const auto volumeSize=request.value("volumeSize",static_cast<UInt64>(0));VolumeOutputStream* split=nullptr;
    ComPtr<IOutStream> stream;
    if(volumeSize) {split=new VolumeOutputStream(outputPath,volumeSize);stream.ptr=split;}
    else {
        HANDLE handle=CreateFileW(outputPath.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(handle==INVALID_HANDLE_VALUE) return {{"kind","result"},{"status","error"},{"code","output_write_failed"}};
        stream.ptr=new ArchiveOutputStream(handle);
    }
    auto* callback = new CreateCallback(context, entries);
    ComPtr<IArchiveUpdateCallback> callbacks; callbacks.ptr = callback;
    auto result = archive->UpdateItems(stream.ptr, (UInt32)entries.size(), callbacks.ptr);
    if(result==S_OK && split) result=split->Finish();
    auto code = cancelled ? "cancelled" : !callback->error.empty() ? callback->error : result != S_OK ? "compression_failed" : "ok";
    return {{"kind", "result"}, {"status", cancelled ? "cancelled" : std::string(code) == "ok" ? "success" : "error"}, {"code", code}, {"format", formatName}, {"itemCount", entries.size()}, {"volumeCount",split ? split->Count() : 0}, {"hresult", (uint32_t)result}};
}
