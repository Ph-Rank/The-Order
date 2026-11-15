#pragma once
#include "GuideChat.hpp"
#include <d3d11.h>
#include <dxgi.h>
#include <dwmapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <future>
#include <chrono>
#pragma comment(lib,"d3d11.lib")
#pragma comment(lib,"dxgi.lib")
#pragma comment(lib,"dwmapi.lib")
#pragma comment(lib,"windowsapp.lib")

namespace guide::chat {
// Fallback for renderers without an injected runtime. This selects only the
// tracked game HWND, never the monitor or Magpie's desktop-composited surface.
inline bool captureTarget(HWND window,LONG pid,LONG64 created){
    DWORD actual=0;GetWindowThreadProcessId(window,&actual);if(actual!=DWORD(pid)||!IsWindowVisible(window)||IsIconic(window))return false;
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,actual);if(!process)return false;bool valid=guide::creation(process)==created;CloseHandle(process);return valid;
}
inline void captureWindow(const std::wstring& name,HWND window,LONG pid,LONG64 created,int request){
    Connection channel;if(!channel.open(name))return;
    try{
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment{~Apartment(){winrt::uninit_apartment();}} apartment;
        using namespace winrt::Windows::Graphics::Capture;
        using namespace winrt::Windows::Graphics::DirectX;
        if(!captureTarget(window,pid,created)||channel.h->pid!=pid||channel.h->created!=created||!GraphicsCaptureSession::IsSupported())throw std::runtime_error("target");
        auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();GraphicsCaptureItem item{nullptr};
        winrt::check_hresult(interop->CreateForWindow(window,winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(item)));
        auto size=item.Size();if(size.Width<1||size.Height<1||size.Width>8192||size.Height>8192||int64_t(size.Width)*size.Height>20000000)throw std::runtime_error("dimensions");
        winrt::com_ptr<ID3D11Device> device;winrt::com_ptr<ID3D11DeviceContext> context;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,device.put(),nullptr,context.put()));
        auto dxgi=device.as<IDXGIDevice>();winrt::com_ptr<IInspectable> inspectable;winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),inspectable.put()));
        auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(inspectable.as<winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice>(),DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
        auto session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled(false);session.StartCapture();
        struct Close{GraphicsCaptureSession session;Direct3D11CaptureFramePool pool;~Close(){try{session.Close();}catch(...){}try{pool.Close();}catch(...){}}} close{session,pool};
        Direct3D11CaptureFrame frame{nullptr};auto began=GetTickCount64();
        while(GetTickCount64()-began<1500){frame=pool.TryGetNextFrame();if(frame)break;Sleep(12);}
        if(!frame||!captureTarget(window,pid,created)||channel.h->captureRequest!=request)throw std::runtime_error("frame");
        auto surface=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(surface->GetInterface(__uuidof(ID3D11Texture2D),texture.put_void()));
        D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);auto content=frame.ContentSize();
        if(content.Width!=size.Width||content.Height!=size.Height||desc.Width<uint32_t(content.Width)||desc.Height<uint32_t(content.Height)||desc.Width>8192||desc.Height>8192)throw std::runtime_error("resized");
        desc.BindFlags=0;desc.MiscFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        winrt::com_ptr<ID3D11Texture2D> staging;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,staging.put()));context->CopyResource(staging.get(),texture.get());
        D3D11_MAPPED_SUBRESOURCE data{};winrt::check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&data));
        if(!data.pData||data.RowPitch<uint32_t(size.Width)*4){context->Unmap(staging.get(),0);throw std::runtime_error("stride");}
        RECT frameBounds{},client{};POINT origin{};bool crop=SUCCEEDED(DwmGetWindowAttribute(window,DWMWA_EXTENDED_FRAME_BOUNDS,&frameBounds,sizeof(frameBounds)))&&GetClientRect(window,&client)&&ClientToScreen(window,&origin)&&frameBounds.right>frameBounds.left&&frameBounds.bottom>frameBounds.top;
        int x=0,y=0,w=size.Width,ht=size.Height;
        if(crop){float sx=float(size.Width)/(frameBounds.right-frameBounds.left),sy=float(size.Height)/(frameBounds.bottom-frameBounds.top);x=std::clamp(int((origin.x-frameBounds.left)*sx),0,size.Width-1);y=std::clamp(int((origin.y-frameBounds.top)*sy),0,size.Height-1);w=std::clamp(int((client.right-client.left)*sx),1,size.Width-x);ht=std::clamp(int((client.bottom-client.top)*sy),1,size.Height-y);}
        if(channel.h->pid==pid&&channel.h->created==created&&captureTarget(window,pid,created))channel.frame(request,static_cast<const BYTE*>(data.pData)+size_t(y)*data.RowPitch+size_t(x)*4,w,ht,int(data.RowPitch),true);
        context->Unmap(staging.get(),0);frame.Close();
    }catch(...){channel.failFrame(request);}
}
}
