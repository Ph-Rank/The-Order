#pragma once
#include "GuideProtocol.hpp"
#include <dwmapi.h>
#pragma comment(lib, "dwmapi.lib")

namespace guide::presentation {
// Magpie publishes these HWND properties for other programs. Use the source
// identity, never a process-name or full-screen-size guess. No Magpie code is
// loaded into the game (or copied into this renderer).
constexpr wchar_t magpieClass[] = L"Window_Magpie_967EB565-6F73-4E94-AE53-00CC42592A22";
struct Surface { HWND window=nullptr, source=nullptr; RECT bounds{}; UINT dpi=96; bool screenCapture=false; };
inline bool area(const RECT& r) { return r.right>r.left && r.bottom>r.top; }
inline bool available(HWND window) {
    if(!IsWindow(window)||!IsWindowVisible(window)||IsIconic(window))return false;
    DWORD cloaked=0;
    return FAILED(DwmGetWindowAttribute(window,DWMWA_CLOAKED,&cloaked,sizeof(cloaked)))||!cloaked;
}
inline bool gameRoot(HWND window) {
    if(!window||GetAncestor(window,GA_ROOT)!=window||GetWindow(window,GW_OWNER))return false;
    wchar_t windowClass[32]{};
    return GetClassNameW(window,windowClass,32)&&wcscmp(windowClass,L"#32770")!=0;
}
inline bool onSurface(HWND window,HWND foreground) {
    return window&&foreground&&(foreground==window||IsChild(window,foreground));
}
inline bool clientBounds(HWND window,RECT& r) {
    if(!GetClientRect(window,&r)||!area(r))return false;
    POINT corners[]={{r.left,r.top},{r.right,r.bottom}};
    if(!ClientToScreen(window,&corners[0])||!ClientToScreen(window,&corners[1]))return false;
    r={corners[0].x,corners[0].y,corners[1].x,corners[1].y};
    return area(r);
}
inline RECT readingBounds(const RECT& output,const RECT& screen) {
    RECT visible{};
    if(!IntersectRect(&visible,&output,&screen)||visible.right-visible.left<320||visible.bottom-visible.top<240)return {};
    return visible;
}
inline RECT readingBounds(const Surface& surface) {
    MONITORINFO monitor{sizeof(monitor)};
    if(!GetMonitorInfoW(MonitorFromRect(&surface.bounds,MONITOR_DEFAULTTONEAREST),&monitor))return {};
    // A windowed scaled image can extend beyond the desktop. Keep the reader
    // on its visible monitor rather than centering controls off screen.
    return readingBounds(surface.bounds,monitor.rcMonitor);
}
inline RECT windowBounds(HWND window) {
    Surface surface{};
    if(!available(window)||!clientBounds(window,surface.bounds))return {};
    return readingBounds(surface);
}
inline bool readMagpie(HWND window,DWORD gamePid,Surface& result) {
    wchar_t name[96]{};
    if(!GetClassNameW(window,name,96)||wcscmp(name,magpieClass)||!available(window))return false;
    const HWND source=static_cast<HWND>(GetPropW(window,L"Magpie.SrcHWND"));
    DWORD pid=0;GetWindowThreadProcessId(source,&pid);
    if(!gamePid||pid!=gamePid||!available(source))return false;
    RECT output{};if(!clientBounds(window,output))return false;
    RECT dest{LONG(INT_PTR(GetPropW(window,L"Magpie.DestLeft"))),LONG(INT_PTR(GetPropW(window,L"Magpie.DestTop"))),
        LONG(INT_PTR(GetPropW(window,L"Magpie.DestRight"))),LONG(INT_PTR(GetPropW(window,L"Magpie.DestBottom")))};
    // DestRect is in screen pixels, including negative monitor coordinates.
    // Crop oversized effects to the actual output window; do not span black
    // bars or fall back to an unrelated primary monitor for incomplete props.
    RECT visible{};
    if(!area(dest)||!IntersectRect(&visible,&dest,&output)||visible.right-visible.left<320||visible.bottom-visible.top<260)return false;
    if(GetPropW(window,L"Magpie.SrcHWND")!=source)return false;
    // Desktop Duplication excludes Magpie's own output from screen capture.
    // Mirror that policy only for this capture path; ordinary window capture
    // should still allow the user to take screenshots of the reader.
    DWORD affinity=0;
    const bool screenCapture=GetWindowDisplayAffinity(window,&affinity)&&(affinity&WDA_EXCLUDEFROMCAPTURE)==WDA_EXCLUDEFROMCAPTURE;
    result={window,source,visible,GetDpiForWindow(window),screenCapture};
    if(!result.dpi)result.dpi=96;
    return true;
}
inline Surface resolve(DWORD gamePid,HWND preferredSource,HWND previous=nullptr) {
    Surface current{};if(previous&&readMagpie(previous,gamePid,current))return current;
    struct Search {DWORD pid;HWND preferred;Surface match;bool ambiguous=false;};
    Search search{gamePid,preferredSource,{}};
    EnumWindows([](HWND window,LPARAM value)->BOOL {
        auto& s=*reinterpret_cast<Search*>(value);Surface candidate{};
        if(!readMagpie(window,s.pid,candidate))return TRUE;
        if(candidate.source==s.preferred){s.match=candidate;s.ambiguous=false;return FALSE;}
        if(s.match.window)s.ambiguous=true;else s.match=candidate;
        return TRUE;
    },reinterpret_cast<LPARAM>(&search));
    return search.ambiguous?Surface{}:search.match;
}
inline bool foreground(const Surface& surface,HWND foregroundWindow) {
    return onSurface(surface.window,foregroundWindow);
}
}
