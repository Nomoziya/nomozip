// Adapted from the official SDK's seekable multi-stream approach. Only one
// volume handle remains open, including while the encoder seeks back to headers.
static std::wstring volumeSuffix(UInt64 index) {auto value=std::to_wstring(index+1);return L"."+std::wstring(value.size()<3 ? 3-value.size() : 0,L'0')+value;}
class VolumeOutputStream final : public IOutStream {
    fs::path base;UInt64 limit,position=0,length=0,currentIndex=0;HANDLE current=INVALID_HANDLE_VALUE;
    std::vector<fs::path> paths;
    void close() {if(current!=INVALID_HANDLE_VALUE) {CloseHandle(current);current=INVALID_HANDLE_VALUE;}}
    HRESULT open(UInt64 index) {
        if(current!=INVALID_HANDLE_VALUE && currentIndex==index) return S_OK;
        close();
        while(paths.size()<=index) {
            auto path=fs::path(base.wstring()+volumeSuffix(paths.size()));
            HANDLE file=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
            if(file==INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
            CloseHandle(file);paths.push_back(path);
        }
        current=CreateFileW(paths[index].c_str(),GENERIC_WRITE,0,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);currentIndex=index;
        return current==INVALID_HANDLE_VALUE ? HRESULT_FROM_WIN32(GetLastError()) : S_OK;
    }
public:
    VolumeOutputStream(fs::path path,UInt64 size):base(std::move(path)),limit(size) {}
    ~VolumeOutputStream() {close();}
    std::atomic<ULONG> refs{1};
    ULONG STDMETHODCALLTYPE AddRef() noexcept override {return ++refs;}
    // Avoid GCC speculative inlining across differently sized COM stream classes.
    [[gnu::noinline]] ULONG STDMETHODCALLTYPE Release() noexcept override {auto count=--refs;if(!count) delete this;return count;}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** object) noexcept override {*object=nullptr;if(id==IID_IUnknown || id==IID_IOutStream || id==IID_ISequentialOutStream) *object=static_cast<IOutStream*>(this);if(!*object) return E_NOINTERFACE;AddRef();return S_OK;}
    HRESULT STDMETHODCALLTYPE Write(const void* data,UInt32 size,UInt32* processed) noexcept override {
        if(processed) *processed=0;
        try {
            while(size) {
                if(checkpoint()!=S_OK) return E_ABORT;
                auto hr=open(position/limit);if(hr!=S_OK) return hr;
                LARGE_INTEGER offset{};offset.QuadPart=position%limit;
                if(!SetFilePointerEx(current,offset,nullptr,FILE_BEGIN)) return HRESULT_FROM_WIN32(GetLastError());
                const auto chunk=static_cast<DWORD>(std::min<UInt64>(size,limit-position%limit));DWORD written=0;
                if(!WriteFile(current,data,chunk,&written,nullptr)) return HRESULT_FROM_WIN32(GetLastError());
                if(!written) return E_FAIL;
                position+=written;length=std::max(length,position);size-=written;data=static_cast<const BYTE*>(data)+written;if(processed) *processed+=written;
            }
            return S_OK;
        } catch(...) {return E_FAIL;}
    }
    HRESULT STDMETHODCALLTYPE Seek(Int64 offset,UInt32 origin,UInt64* result) noexcept override {
        const auto start=origin==STREAM_SEEK_SET ? 0 : origin==STREAM_SEEK_CUR ? position : origin==STREAM_SEEK_END ? length : UINT64_MAX;
        if(start==UINT64_MAX || (offset<0 && static_cast<UInt64>(-(offset+1))+1>start) || (offset>=0 && static_cast<UInt64>(offset)>INT64_MAX-start)) return STG_E_INVALIDFUNCTION;
        position=offset<0 ? start-(static_cast<UInt64>(-(offset+1))+1) : start+offset;if(result) *result=position;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE SetSize(UInt64 size) noexcept override {
        try {
            if(size>INT64_MAX) return E_INVALIDARG;
            const auto count=size ? (size-1)/limit+1 : 0;
            for(UInt64 i=0;i<count;++i) {
                if(checkpoint()!=S_OK) return E_ABORT;
                auto hr=open(i);if(hr!=S_OK) return hr;
                LARGE_INTEGER end{};end.QuadPart=std::min(limit,size-i*limit);
                if(!SetFilePointerEx(current,end,nullptr,FILE_BEGIN) || !SetEndOfFile(current)) return HRESULT_FROM_WIN32(GetLastError());
            }
            close();while(paths.size()>count) {if(!DeleteFileW(paths.back().c_str())) return HRESULT_FROM_WIN32(GetLastError());paths.pop_back();}
            length=size;return S_OK;
        } catch(...) {return E_FAIL;}
    }
    HRESULT Finish() {return SetSize(length);}
    UInt64 Count() const {return paths.size();}
};

class VolumeInputStream final : public IInStream {
    struct Part {fs::path path;UInt64 start,size;};std::vector<Part> parts;
    UInt64 position=0,length=0,currentIndex=UINT64_MAX;HANDLE current=INVALID_HANDLE_VALUE;
    void close() {if(current!=INVALID_HANDLE_VALUE) {CloseHandle(current);current=INVALID_HANDLE_VALUE;}}
public:
    explicit VolumeInputStream(const fs::path& first) {
        auto base=first.wstring();base.resize(base.size()-4);
        for(UInt64 i=0;;++i) {
            auto path=fs::path(base+volumeSuffix(i));WIN32_FILE_ATTRIBUTE_DATA data{};
            if(!GetFileAttributesExW(path.c_str(),GetFileExInfoStandard,&data)) {if(GetLastError()!=ERROR_FILE_NOT_FOUND) throw std::runtime_error("archive_read_failed");break;}
            if(data.dwFileAttributes&(FILE_ATTRIBUTE_DIRECTORY|FILE_ATTRIBUTE_REPARSE_POINT)) throw std::runtime_error("unsafe_path");
            const UInt64 size=(static_cast<UInt64>(data.nFileSizeHigh)<<32)|data.nFileSizeLow;
            if(size==0 || size>INT64_MAX-length) throw std::runtime_error("missing_volume");
            parts.push_back({path,length,size});length+=size;
        }
        if(parts.empty()) throw std::runtime_error("archive_read_failed");
        // A missing middle volume must not turn a prefix into an apparently complete ZIP.
        const auto prefix=first.filename().wstring().substr(0,first.filename().wstring().size()-3);
        for(const auto& file:fs::directory_iterator(first.parent_path())) {
            const auto name=file.path().filename().wstring();
            if(name.size()<=prefix.size() || _wcsnicmp(name.c_str(),prefix.c_str(),prefix.size())!=0) continue;
            const auto number=name.substr(prefix.size());if(number.size()<3 || number.find_first_not_of(L"0123456789")!=std::wstring::npos) continue;
            if(number.size()>18 || std::stoull(number)>parts.size()) throw std::runtime_error("missing_volume");
        }
    }
    ~VolumeInputStream() {close();}
    REFCOUNT_IMPL
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** object) noexcept override {*object=nullptr;if(id==IID_IUnknown || id==IID_IInStream || id==IID_ISequentialInStream) *object=static_cast<IInStream*>(this);if(!*object) return E_NOINTERFACE;AddRef();return S_OK;}
    HRESULT STDMETHODCALLTYPE Read(void* data,UInt32 size,UInt32* processed) noexcept override {
        if(processed) *processed=0;
        while(size && position<length) {
            if(checkpoint()!=S_OK) return E_ABORT;
            const auto found=std::upper_bound(parts.begin(),parts.end(),position,[](UInt64 value,const Part& part){return value<part.start;});const UInt64 index=static_cast<UInt64>(found-parts.begin()-1);const auto& part=parts[index];
            if(currentIndex!=index || current==INVALID_HANDLE_VALUE) {
                close();current=CreateFileW(part.path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);currentIndex=index;
                if(current==INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
                LARGE_INTEGER actual{};if(!GetFileSizeEx(current,&actual) || static_cast<UInt64>(actual.QuadPart)!=part.size) return E_FAIL;
            }
            LARGE_INTEGER offset{};offset.QuadPart=position-part.start;if(!SetFilePointerEx(current,offset,nullptr,FILE_BEGIN)) return HRESULT_FROM_WIN32(GetLastError());
            DWORD count=0;const auto chunk=static_cast<DWORD>(std::min<UInt64>(size,part.size-offset.QuadPart));
            if(!ReadFile(current,data,chunk,&count,nullptr)) return HRESULT_FROM_WIN32(GetLastError());
            if(!count) return E_FAIL;
            position+=count;size-=count;data=static_cast<BYTE*>(data)+count;if(processed) *processed+=count;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Seek(Int64 offset,UInt32 origin,UInt64* result) noexcept override {
        const auto start=origin==STREAM_SEEK_SET ? 0 : origin==STREAM_SEEK_CUR ? position : origin==STREAM_SEEK_END ? length : UINT64_MAX;
        if(start==UINT64_MAX || (offset<0 && static_cast<UInt64>(-(offset+1))+1>start) || (offset>=0 && static_cast<UInt64>(offset)>INT64_MAX-start)) return STG_E_INVALIDFUNCTION;
        position=offset<0 ? start-(static_cast<UInt64>(-(offset+1))+1) : start+offset;if(result) *result=position;return S_OK;
    }
};
