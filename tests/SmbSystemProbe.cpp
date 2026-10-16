#define NOMINMAX
#include <windows.h>
#include <filesystem>
#include <iostream>
#include "../src/EngineWorker/WindowsPaths.h"
namespace fs=std::filesystem;
int wmain(int argc,wchar_t** argv) {
    if(argc!=3) return 2;
    WIN32_FILE_ATTRIBUTE_DATA direct{},normalized{};
    bool directOk=GetFileAttributesExW(argv[1],GetFileExInfoStandard,&direct);
    DWORD directError=directOk?0:GetLastError();
    bool absoluteOk=false,normalizedOk=false,outputOk=false;
    int absoluteError=0,outputError=0;
    bool win32Attributes=false,win32Output=false;
    try {
        auto path=fs::absolute(fs::path(argv[1]));absoluteOk=true;
        normalizedOk=GetFileAttributesExW(path.c_str(),GetFileExInfoStandard,&normalized);
    } catch(const fs::filesystem_error& error){absoluteError=error.code().value();}
    catch(...){absoluteError=-1;}
    try {
        auto path=fs::absolute(fs::path(argv[2]));
        HANDLE file=CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        outputOk=file!=INVALID_HANDLE_VALUE;
        if(outputOk){CloseHandle(file);DeleteFileW(path.c_str());}else{outputError=GetLastError();}
    } catch(const fs::filesystem_error& error){outputError=error.code().value();}
    catch(...){outputError=-1;}
    try {
        auto path=absoluteWindowsPath(argv[1]);
        win32Attributes=GetFileAttributesExW(path.c_str(),GetFileExInfoStandard,&normalized);
        auto output=absoluteWindowsPath(argv[2]);
        HANDLE file=CreateFileW(output.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        win32Output=file!=INVALID_HANDLE_VALUE;
        if(win32Output){CloseHandle(file);DeleteFileW(output.c_str());}
    }catch(...){}
    std::cout<<"{\"directAttributes\":"<<(directOk?"true":"false")
      <<",\"directAttributesError\":"<<directError<<",\"attributes\":"<<direct.dwFileAttributes
      <<",\"filesystemAbsolute\":"<<(absoluteOk?"true":"false")<<",\"absoluteError\":"<<absoluteError
      <<",\"normalizedAttributes\":"<<(normalizedOk?"true":"false")
      <<",\"normalizedOutputCreate\":"<<(outputOk?"true":"false")<<",\"outputError\":"<<outputError
      <<",\"win32NormalizedAttributes\":"<<(win32Attributes?"true":"false")
      <<",\"win32NormalizedOutputCreate\":"<<(win32Output?"true":"false")<<"}"<<std::endl;
    return 0;
}
