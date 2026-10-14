#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <cstddef>
struct ProbeResult { unsigned int size, infoOffset, accepted, error, validWindow, validIcon; };
extern "C" __declspec(dllexport) void __cdecl ProbeNotification(HWND window, ProbeResult* result) {
    NOTIFYICONDATAW data{};
    data.cbSize=sizeof(data);data.hWnd=window;data.uID=19073;
    data.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;data.uCallbackMessage=WM_APP+73;
    data.hIcon=LoadIconW(nullptr,IDI_APPLICATION);lstrcpyW(data.szTip,L"nomozip native SDK test");
    SetLastError(0);auto accepted=Shell_NotifyIconW(NIM_ADD,&data);auto error=GetLastError();
    *result={sizeof(data),offsetof(NOTIFYICONDATAW,szInfo),static_cast<unsigned int>(accepted),error,static_cast<unsigned int>(IsWindow(window)),data.hIcon?1u:0u};
    if(accepted) Shell_NotifyIconW(NIM_DELETE,&data);
}
