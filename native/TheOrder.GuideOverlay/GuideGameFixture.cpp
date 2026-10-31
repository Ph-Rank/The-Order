#include "GuideProtocol.hpp"
#include <d3d9.h>
#include <d3d11.h>
#include <dxgi.h>
#include <shellapi.h>
#include <cstdio>
#pragma comment(lib,"d3d9.lib")
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")
#pragma comment(lib,"user32.lib")
#pragma comment(lib,"gdi32.lib")
#pragma comment(lib,"shell32.lib")

static bool stop = false;
static bool busyCursor = false;
static int clicks = 0, keys = 0;
static LRESULT CALLBACK proc(HWND w, UINT m, WPARAM p, LPARAM l) {
    if (m == WM_CLOSE) { stop = true; return 0; }
    if (m == WM_LBUTTONDOWN) ++clicks;
    if (m == WM_KEYDOWN) ++keys;
    if(m==WM_SETCURSOR&&busyCursor&&LOWORD(l)==HTCLIENT){SetCursor(LoadCursorW(nullptr,MAKEINTRESOURCEW(32650)));return TRUE;}
    return DefWindowProcW(w,m,p,l);
}
static bool bmp(const std::wstring& path, const void* data, int width, int height, int pitch, bool swap = false) {
    HANDLE file = CreateFileW(path.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr); if(file==INVALID_HANDLE_VALUE)return false;
    BITMAPFILEHEADER header{}; header.bfType=0x4d42; header.bfOffBits=sizeof(header)+sizeof(BITMAPINFOHEADER); header.bfSize=header.bfOffBits+width*height*4;
    BITMAPINFOHEADER info{};info.biSize=sizeof(info);info.biWidth=width;info.biHeight=-height;info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;
    DWORD written=0; WriteFile(file,&header,sizeof(header),&written,nullptr);WriteFile(file,&info,sizeof(info),&written,nullptr);
    std::vector<BYTE> row(width*4);
    for(int y=0;y<height;++y){const BYTE* pixels=static_cast<const BYTE*>(data)+size_t(y)*pitch;
        if(swap){memcpy(row.data(),pixels,row.size());for(int x=0;x<width;++x)std::swap(row[x*4],row[x*4+2]);pixels=row.data();}
        WriteFile(file,pixels,width*4,&written,nullptr);}
    CloseHandle(file);return true;
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int) {
    int argc=0;auto argv=CommandLineToArgvW(GetCommandLineW(),&argc); bool dx9=false,full=false,borderless=false;int seconds=10,requestedWidth=0,requestedHeight=0;std::wstring report,capture;
    for(int i=1;i<argc;++i){std::wstring a=argv[i];if(a==L"--api"&&i+1<argc)dx9=wcscmp(argv[++i],L"dx9")==0;
        else if(a==L"--fullscreen")full=true;else if(a==L"--borderless")borderless=true;else if(a==L"--seconds"&&i+1<argc)seconds=std::clamp(_wtoi(argv[++i]),3,60);
        else if(a==L"--width"&&i+1<argc)requestedWidth=std::clamp(_wtoi(argv[++i]),640,7680);
        else if(a==L"--height"&&i+1<argc)requestedHeight=std::clamp(_wtoi(argv[++i]),480,4320);
        else if(a==L"--report"&&i+1<argc)report=argv[++i];else if(a==L"--capture"&&i+1<argc)capture=argv[++i];}
    for(int i=1;i<argc;++i)if(wcscmp(argv[i],L"--busy-cursor")==0)busyCursor=true;
    LocalFree(argv); SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW wc{};wc.hInstance=instance;wc.lpfnWndProc=proc;wc.hCursor=LoadCursorW(nullptr,MAKEINTRESOURCEW(32512));wc.lpszClassName=L"TheOrder.GuideFixture";RegisterClassW(&wc);
    int width=full||borderless?GetSystemMetrics(SM_CXSCREEN):1280,height=full||borderless?GetSystemMetrics(SM_CYSCREEN):800;
    if(requestedWidth)width=requestedWidth;if(requestedHeight)height=requestedHeight;
    HWND window=CreateWindowW(wc.lpszClassName,L"The Order · 合成全屏攻略验证",full||borderless?WS_POPUP:WS_OVERLAPPEDWINDOW,
        0,0,width,height,nullptr,nullptr,instance,nullptr);ShowWindow(window,SW_SHOW);UpdateWindow(window);
    guide::Connection connection;wchar_t session[128]{};if(GetEnvironmentVariableW(L"THE_ORDER_GUIDE_SESSION",session,128))connection.open(session);
    IDirect3D9* d9=nullptr;IDirect3DDevice9* device9=nullptr;ID3D11Device* device11=nullptr;ID3D11DeviceContext* context=nullptr;
    IDXGISwapChain* chain=nullptr;ID3D11RenderTargetView* target=nullptr;ID3D11Texture2D* back=nullptr;
    HRESULT result=E_FAIL;BOOL actualFull=FALSE;unsigned frames=0;bool captured=false, baseline=false, graphCapture=false, hiddenCapture=false, interactiveSeen=false;
    int visibleTransitions=0, lastVisible=0, hiddenFrames=0, graphFrames=0, maxStep=0;
    int originalFrames=0,jumpFrames=0,routeFrames=0;bool originalCapture=false,jumpCapture=false,routeCapture=false;HRESULT lastPresent=S_OK;
    int pointerSamples=0,nativePointerVisibleSamples=0,originalBusySamples=0,restoredPointerSamples=0;
    if(dx9){d9=Direct3DCreate9(D3D_SDK_VERSION);if(d9){D3DDISPLAYMODE display{};d9->GetAdapterDisplayMode(D3DADAPTER_DEFAULT,&display);
        D3DPRESENT_PARAMETERS parameters{};parameters.Windowed=!full;parameters.hDeviceWindow=window;parameters.SwapEffect=D3DSWAPEFFECT_DISCARD;
        parameters.BackBufferWidth=width;parameters.BackBufferHeight=height;parameters.BackBufferFormat=full?display.Format:D3DFMT_UNKNOWN;
        parameters.PresentationInterval=D3DPRESENT_INTERVAL_ONE;
        result=d9->CreateDevice(D3DADAPTER_DEFAULT,D3DDEVTYPE_HAL,window,D3DCREATE_HARDWARE_VERTEXPROCESSING,&parameters,&device9);actualFull=!parameters.Windowed;}}
    else{DXGI_SWAP_CHAIN_DESC desc{};desc.BufferDesc.Width=width;desc.BufferDesc.Height=height;desc.BufferDesc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.BufferCount=1;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.OutputWindow=window;desc.Windowed=TRUE;desc.SampleDesc.Count=1;desc.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
        result=D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&desc,&chain,&device11,nullptr,&context);
        if(SUCCEEDED(result)){chain->GetBuffer(0,IID_PPV_ARGS(&back));device11->CreateRenderTargetView(back,nullptr,&target);
            if(full){HRESULT mode=chain->SetFullscreenState(TRUE,nullptr);if(FAILED(mode)&&mode!=DXGI_ERROR_NOT_CURRENTLY_AVAILABLE)result=mode;}chain->GetFullscreenState(&actualFull,nullptr);}}
    auto save=[&](const wchar_t* suffix){
        if(capture.empty())return false;DWORD front=0;GetWindowThreadProcessId(GetForegroundWindow(),&front);if(front!=GetCurrentProcessId())return false;
        auto path=capture+suffix+L".bmp";bool saved=false;
        if(dx9&&full){D3DDISPLAYMODE display{};device9->GetDisplayMode(0,&display);IDirect3DSurface9* surface=nullptr;
            if(SUCCEEDED(device9->CreateOffscreenPlainSurface(display.Width,display.Height,D3DFMT_A8R8G8B8,D3DPOOL_SYSTEMMEM,&surface,nullptr))){
                if(SUCCEEDED(device9->GetFrontBufferData(0,surface))){D3DLOCKED_RECT bits{};if(SUCCEEDED(surface->LockRect(&bits,nullptr,D3DLOCK_READONLY))){saved=bmp(path,bits.pBits,display.Width,display.Height,bits.Pitch);surface->UnlockRect();}}
                surface->Release();}}
        else if(!dx9&&back){D3D11_TEXTURE2D_DESC desc{};back->GetDesc(&desc);desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.Usage=D3D11_USAGE_STAGING;desc.MiscFlags=0;
            ID3D11Texture2D* staging=nullptr;if(SUCCEEDED(device11->CreateTexture2D(&desc,nullptr,&staging))){context->CopyResource(staging,back);D3D11_MAPPED_SUBRESOURCE mapped{};
                if(SUCCEEDED(context->Map(staging,0,D3D11_MAP_READ,0,&mapped))){saved=bmp(path,mapped.pData,desc.Width,desc.Height,mapped.RowPitch,true);context->Unmap(staging,0);}staging->Release();}}
        return saved;
    };
    ULONGLONG start=GetTickCount64();
    while(SUCCEEDED(result)&&!stop&&GetTickCount64()-start<ULONGLONG(seconds)*1000){
        MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
        if(full&&!dx9&&!actualFull&&GetForegroundWindow()==window){if(SUCCEEDED(chain->SetFullscreenState(TRUE,nullptr)))chain->GetFullscreenState(&actualFull,nullptr);}
        if(busyCursor)SetCursor(LoadCursorW(nullptr,MAKEINTRESOURCEW(32650)));
        if(dx9){device9->Clear(0,nullptr,D3DCLEAR_TARGET,D3DCOLOR_XRGB(44,74,118),1,0);lastPresent=device9->Present(nullptr,nullptr,nullptr,nullptr);}
        else{float color[]={.17f,.29f,.46f,1};context->OMSetRenderTargets(1,&target,nullptr);context->ClearRenderTargetView(target,color);lastPresent=chain->Present(1,0);}
        ++frames;if(!baseline&&frames>30&&(!connection.header||!connection.header->visible))baseline=save(L"-baseline");
        if(connection.header)InterlockedExchange(&connection.header->gameFrames,LONG(frames));
        if(connection.header){auto* h=connection.header;
            if(busyCursor){CURSORINFO cursor{sizeof(cursor)};POINT at{};RECT client{};DWORD cursorPid=0;GetWindowThreadProcessId(GetForegroundWindow(),&cursorPid);
                if(GetCursorInfo(&cursor)&&GetCursorPos(&at)&&ScreenToClient(window,&at)&&GetClientRect(window,&client)&&PtInRect(&client,at)&&cursorPid==GetCurrentProcessId()){
                    if(h->visible&&h->renderedFrames>10){++pointerSamples;if(cursor.flags&CURSOR_SHOWING)++nativePointerVisibleSamples;if(cursor.hCursor==LoadCursorW(nullptr,MAKEINTRESOURCEW(32650)))++originalBusySamples;}
                    else if(!h->visible&&(cursor.flags&CURSOR_SHOWING))++restoredPointerSamples;}}
            if(lastVisible!=h->visible){++visibleTransitions;lastVisible=h->visible;}
            interactiveSeen=interactiveSeen||h->interactive!=0;maxStep=std::max(maxStep,int(h->step));
            if(!captured&&h->visible&&h->renderedFrames>10&&frames>120)captured=save(L"-overlay");
            graphFrames=h->visible&&h->graph?graphFrames+1:0;
            if(!graphCapture&&graphFrames>10)graphCapture=save(L"-graph");
            originalFrames=h->visible&&h->showOriginal?originalFrames+1:0;
            if(!originalCapture&&originalFrames>10)originalCapture=save(L"-original");
            jumpFrames=h->visible&&h->step>20?jumpFrames+1:0;
            if(!jumpCapture&&jumpFrames>10)jumpCapture=save(L"-position");
            routeFrames=h->visible&&h->route>0?routeFrames+1:0;
            if(!routeCapture&&routeFrames>10)routeCapture=save(L"-route");
            hiddenFrames=!h->visible&&captured?hiddenFrames+1:0;
            if(!hiddenCapture&&hiddenFrames>10)hiddenCapture=save(L"-hidden");}
        if(FAILED(lastPresent)&&lastPresent!=D3DERR_DEVICELOST)break;
        Sleep(1);
    }
    if(chain)chain->SetFullscreenState(FALSE,nullptr);
    if(!report.empty()){
        HANDLE file=CreateFileW(report.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(file!=INVALID_HANDLE_VALUE){char json[1536]{};DWORD written=0;int n=snprintf(json,sizeof(json),
            "{\"pid\":%lu,\"dx9\":%s,\"requestedFullscreen\":%s,\"reportedFullscreen\":%s,\"createResult\":%ld,\"presentResult\":%ld,\"frames\":%u,\"baseline\":%s,\"overlayCapture\":%s,\"gameClicks\":%d,\"gameKeys\":%d,\"visibleTransitions\":%d,\"graphCapture\":%s,\"hiddenCapture\":%s,\"interactiveSeen\":%s,\"maxStep\":%d,\"finalStep\":%d,\"busyCursor\":%s,\"pointerSamples\":%d,\"nativePointerVisibleSamples\":%d,\"originalBusySamples\":%d,\"restoredPointerSamples\":%d}",
            GetCurrentProcessId(),dx9?"true":"false",full?"true":"false",actualFull?"true":"false",long(result),long(lastPresent),frames,baseline?"true":"false",captured?"true":"false",clicks,keys,
            visibleTransitions,graphCapture?"true":"false",hiddenCapture?"true":"false",interactiveSeen?"true":"false",maxStep,connection.header?int(connection.header->step):-1,busyCursor?"true":"false",pointerSamples,nativePointerVisibleSamples,originalBusySamples,restoredPointerSamples);
            WriteFile(file,json,n,&written,nullptr);CloseHandle(file);}}
    if(target)target->Release();if(back)back->Release();if(chain)chain->Release();if(context)context->Release();if(device11)device11->Release();
    if(device9)device9->Release();if(d9)d9->Release();DestroyWindow(window);return SUCCEEDED(result)?0:8;
}
