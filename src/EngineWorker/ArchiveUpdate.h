// Update the already opened input archive into a separate new file. Retained items
// use the SDK's copy-data-and-properties path; no extracted plaintext staging.
static json updateArchive(json& request,IInArchive* input,const std::wstring& format,Context& context,UInt32 count) {
    if(format!=L"7z" && _wcsicmp(format.c_str(),L"zip")!=0) throw std::runtime_error("archive_edit_read_only");
    const auto& snapshot=request.at("selection");
    if(!snapshot.is_array() || snapshot.size()!=count) throw std::runtime_error("selection_changed");
    for(UInt32 i=0;i<count;++i) {
        const auto& item=snapshot[i];
        if(item.at("index").get<UInt32>()!=i || property(input,i,kpidPath).text()!=wide(item.at("path").get<std::string>())
            || property(input,i,kpidSize).number()!=item.at("size").get<UInt64>()
            || property(input,i,kpidIsDir).boolean()!=item.at("directory").get<bool>()
            || property(input,i,kpidEncrypted).boolean()!=item.at("encrypted").get<bool>()) throw std::runtime_error("selection_changed");
    }
    std::vector<bool> removed(count,false);
    for(const auto& item:request.at("removedIndices")) {auto index=item.get<UInt32>();if(index>=count) throw std::runtime_error("invalid_selection");removed[index]=true;}
    std::vector<CreateEntry> entries;std::vector<UInt32> previous;
    for(UInt32 i=0;i<count;++i) if(!removed[i]) {entries.push_back({{},property(input,i,kpidPath).text(),property(input,i,kpidIsDir).boolean(),property(input,i,kpidSize).number(),{}});previous.push_back(i);}
    for(const auto& item:request.at("sourceEntries")) {
        auto source=fs::absolute(wide(item.at("sourcePath").get<std::string>()));
        auto name=safeRelative(wide(item.at("path").get<std::string>())).wstring();
        if(source==context.archivePath) throw std::runtime_error("invalid_source");
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        if(!GetFileAttributesExW(source.c_str(),GetFileExInfoStandard,&attributes) || (attributes.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT)) throw std::runtime_error("source_read_failed");
        const bool directory=(attributes.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)!=0;
        entries.push_back({source,name,directory,directory ? 0 : ((UInt64)attributes.nFileSizeHigh<<32)|attributes.nFileSizeLow,attributes});previous.push_back(0xFFFFFFFF);
    }
    ComPtr<IOutArchive> output;
    if(input->QueryInterface(IID_IOutArchive,reinterpret_cast<void**>(&output.ptr))!=S_OK) throw std::runtime_error("archive_edit_read_only");
    ComPtr<ISetProperties> properties;
    if(output->QueryInterface(IID_ISetProperties,reinterpret_cast<void**>(&properties.ptr))!=S_OK) throw std::runtime_error("compression_not_supported");
    const wchar_t* names[2]={L"x",format==L"7z" ? L"he" : L"em"};
    Variant level,encryption;level.value.vt=VT_UI4;level.value.ulVal=5;
    PROPVARIANT values[2]={level.value,{}};UInt32 number=1;
    if(format==L"7z") {values[1].vt=VT_BOOL;values[1].boolVal=request.value("encryptHeaders",false) ? VARIANT_TRUE : VARIANT_FALSE;number=2;}
    else if(context.passwordDefined) {encryption.value.vt=VT_BSTR;encryption.value.bstrVal=SysAllocString(L"AES256");values[1]=encryption.value;number=2;}
    if(properties->SetProperties(names,values,number)!=S_OK) throw std::runtime_error("invalid_compression_options");
    auto path=fs::absolute(wide(request.at("outputPath").get<std::string>()));
    if(path==context.archivePath) throw std::runtime_error("invalid_output");
    HANDLE handle=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(handle==INVALID_HANDLE_VALUE) throw std::runtime_error("output_write_failed");
    ComPtr<IOutStream> stream;stream.ptr=new ArchiveOutputStream(handle);
    auto* callback=new CreateCallback(context,entries,&previous);ComPtr<IArchiveUpdateCallback> callbacks;callbacks.ptr=callback;
    const auto result=output->UpdateItems(stream.ptr,static_cast<UInt32>(entries.size()),callbacks.ptr);
    auto code=cancelled ? "cancelled" : context.passwordMissing ? "password_required" : !callback->error.empty() ? callback->error : result!=S_OK ? "archive_update_failed" : "ok";
    return {{"kind","result"},{"status",cancelled ? "cancelled" : std::string(code)=="ok" ? "success" : "error"},{"code",code},{"format",utf8(format)},{"itemCount",entries.size()}};
}
