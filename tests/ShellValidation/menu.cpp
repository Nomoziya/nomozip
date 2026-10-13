#ifndef UNICODE
#define UNICODE
#endif
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <fstream>

static bool Find(HMENU menu, const wchar_t* title, UINT& result) {
    for (int index = 0; index < GetMenuItemCount(menu); ++index) {
        wchar_t text[512]{}; MENUITEMINFOW item{}; item.cbSize=sizeof(item); item.fMask=MIIM_STRING|MIIM_ID|MIIM_SUBMENU; item.dwTypeData=text; item.cch=511;
        if (!GetMenuItemInfoW(menu,index,TRUE,&item)) continue;
        if (!lstrcmpW(text,title)) { result=item.wID; return true; }
        if (item.hSubMenu && Find(item.hSubMenu,title,result)) return true;
    }
    return false;
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 3 || FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED))) return 2;
    PIDLIST_ABSOLUTE id = ILCreateFromPathW(argv[1]); if (!id) return 3;
    IShellFolder* folder=nullptr; PCUITEMID_CHILD child=nullptr;
    auto hr=SHBindToParent(id,IID_PPV_ARGS(&folder),&child); if (FAILED(hr)) return 4;
    IContextMenu* context=nullptr; hr=folder->GetUIObjectOf(nullptr,1,&child,IID_IContextMenu,nullptr,reinterpret_cast<void**>(&context));
    folder->Release(); if (FAILED(hr)) return 5;
    auto menu=CreatePopupMenu(); hr=context->QueryContextMenu(menu,0,1,30000,CMF_NORMAL); if (FAILED(hr)) return 6;
    UINT root=0,here=0,own=0,compress=0;
    bool visible=Find(menu,L"nomozip",root) && Find(menu,L"解压到当前目录",here) && Find(menu,L"解压到同名文件夹",own) && Find(menu,L"压缩…",compress);
    if (!visible) { fwprintf(stderr,L"Actual Windows Shell menu did not contain the expected commands.\n"); return 7; }
    CMINVOKECOMMANDINFOEX invoke{}; invoke.cbSize=sizeof(invoke); invoke.fMask=CMIC_MASK_UNICODE; invoke.nShow=SW_SHOWNORMAL;
    invoke.lpVerb=MAKEINTRESOURCEA(compress-1); invoke.lpVerbW=MAKEINTRESOURCEW(compress-1);
    hr=context->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&invoke));
    DestroyMenu(menu); context->Release(); ILFree(id); CoUninitialize(); if (FAILED(hr)) return 8;
    std::ofstream report(argv[2]); report << "{\"passed\":true,\"actualShellMenuEnumerated\":true,\"delegateInvoked\":true}"; return 0;
}
