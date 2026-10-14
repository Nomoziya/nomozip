#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>
#include <cstddef>
#include <cstdio>
struct ProbeResult { unsigned int size, infoOffset, accepted, error, validWindow, validIcon, freshAccepted, freshError; };
extern "C" __declspec(dllexport) void __cdecl ProbeNotification(HWND window, ProbeResult* result) {
    NOTIFYICONDATAW data{};
    data.cbSize=sizeof(data);data.hWnd=window;data.uID=19073;
    data.uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP;data.uCallbackMessage=WM_APP+73;
    data.hIcon=LoadIconW(nullptr,IDI_APPLICATION);lstrcpyW(data.szTip,L"nomozip native SDK test");
    SetLastError(0);auto accepted=Shell_NotifyIconW(NIM_ADD,&data);auto error=GetLastError();
    *result={sizeof(data),offsetof(NOTIFYICONDATAW,szInfo),static_cast<unsigned int>(accepted),error,static_cast<unsigned int>(IsWindow(window)),data.hIcon?1u:0u,0,0};
    if(accepted) Shell_NotifyIconW(NIM_DELETE,&data);
    auto fresh=CreateWindowExW(0,L"STATIC",L"nomozip SDK diagnostic",WS_OVERLAPPED,0,0,100,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(fresh){data.hWnd=fresh;SetLastError(0);result->freshAccepted=Shell_NotifyIconW(NIM_ADD,&data);result->freshError=GetLastError();if(result->freshAccepted) Shell_NotifyIconW(NIM_DELETE,&data);DestroyWindow(fresh);}
}
int wmain() {
    auto window=CreateWindowExW(0,L"STATIC",L"nomozip SDK standalone diagnostic",WS_OVERLAPPED,0,0,100,100,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
    ProbeResult result{};ProbeNotification(window,&result);
    DWORD session=0;ProcessIdToSessionId(GetCurrentProcessId(),&session);
    std::printf("{\"size\":%u,\"infoOffset\":%u,\"accepted\":%u,\"error\":%u,\"validWindow\":%u,\"validIcon\":%u,\"freshAccepted\":%u,\"freshError\":%u,\"session\":%lu,\"taskbarAvailable\":%s}\n",result.size,result.infoOffset,result.accepted,result.error,result.validWindow,result.validIcon,result.freshAccepted,result.freshError,session,FindWindowW(L"Shell_TrayWnd",nullptr)?"true":"false");
    if(window) DestroyWindow(window);
    return result.validWindow && result.validIcon ? 0 : 1;
}
