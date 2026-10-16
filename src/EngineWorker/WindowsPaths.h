#pragma once
#include <windows.h>
#include <filesystem>
#include <stdexcept>
#include <string>

// Some MinGW filesystem implementations collapse the UNC server prefix in
// absolute(). Keep Windows source/output paths intact using the platform API.
inline std::filesystem::path absoluteWindowsPath(const std::filesystem::path& input) {
    DWORD capacity=GetFullPathNameW(input.c_str(),0,nullptr,nullptr);
    if(!capacity) throw std::runtime_error("invalid_windows_path");
    std::wstring buffer(capacity,L'\0');
    DWORD length=GetFullPathNameW(input.c_str(),capacity,buffer.data(),nullptr);
    if(!length || length>=capacity) throw std::runtime_error("invalid_windows_path");
    buffer.resize(length);
    return std::filesystem::path(buffer);
}
