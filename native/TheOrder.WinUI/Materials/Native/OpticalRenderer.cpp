#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_4.h>
#include <d3dcompiler.h>
#include <microsoft.ui.xaml.media.dxinterop.h>
#include <wrl/client.h>
#include <memory>
#include <cstring>
#include <stdexcept>
#include <string>
#include <cmath>
#include <d2d1_1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <vector>
using Microsoft::WRL::ComPtr;
namespace {
UINT64 resourceCounts[6]={}; // UI-thread diagnostic totals, not driver residency.
void Check(HRESULT hr) { if (FAILED(hr)) throw hr; }
#include "GallerySource.h"
// Original, local approximation: rounded-rectangle distance field, bounded bevel,
// one coordinate per channel. No Apple code, private hook or desktop capture.
const char* shader = R"shader(
cbuffer Parameters : register(b0) {
 float4 viewSource; // stage DIP, source DIP
 float4 lens;       // center DIP, half extent DIP
 float4 optics;     // corner radius, bevel width, optical depth, dispersion DIP
 float4 lighting;   // gloss, dark, background translation DIP
 float4 raster;     // scale, rev1 frost amount (0 keeps the accepted C1 output), fully clear (1 with material.x), reserved
 float4 frostTint;  // stage-plate body tint rgb, tint alpha (rev2: minimum tint)
 float4 material;   // rev2 continuous glass: enable, luminance bound, polarity (+1 lift/-1 deepen), edge density
};
Texture2D sourceImage : register(t0);
Texture2D frostImage : register(t1); // same-scene Gaussian copy; bound only while valid
SamplerState linearClamp : register(s0);
struct Vertex { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
Vertex VS(uint id : SV_VertexID) {
 Vertex o; float2 uv=float2((id<<1)&2,id&2);
 o.position=float4(uv*float2(2,-2)+float2(-1,1),0,1);o.uv=uv;return o;
}
float3 Read(float2 p) { return sourceImage.SampleLevel(linearClamp,(p-lighting.zw)/viewSource.zw,0).rgb; }
float3 ReadFrost(float2 p) { return frostImage.SampleLevel(linearClamp,(p-lighting.zw)/viewSource.zw,0).rgb; }
float4 PS(Vertex input) : SV_TARGET {
 float2 p=input.uv*viewSource.xy;
 float3 original=Read(p);
 float2 local=p-lens.xy;
 float2 q=abs(local)-lens.zw+optics.x;
 float2 outside=max(q,0);
 float sd=length(outside)+min(max(q.x,q.y),0)-optics.x;
 float coverage=saturate(.5-sd*raster.x);
 if(coverage<=0) return float4(original,1);
 float d=max(-sd,0);
 // Smooth the inner medial transition where a bevel extends beyond corner radius.
 float2 soft=log(1+exp(clamp(q/2,-40,40)))*2;
 float2 normal=normalize(soft+float2(.00001,.00001))*sign(local);
 float t=saturate(d/optics.y);
 // Bounded derivative avoids the grazing-angle fold / repeated glyph segments
 // observed in round 1. Both profiles keep 1.35 * .55 * depth / width < 1.
 float2 shift=-normal*(.55*optics.z*pow(1-t,1.35));
 float2 coord=p+shift;
 float spectral=optics.w*pow(1-t,2);
 float3 color=Read(coord);
 if(spectral>.001) {
   color.r=Read(coord+normal*spectral).r;
   color.b=Read(coord-normal*spectral).b;
 }
 // Frosted body: the clear refracting rim keeps sampling the sharp source; the
 // interior fades to the blurred copy under a bounded tint that sets text contrast.
 // The body has its own rounded field: inner iso-lines of the plate field turn square once
 // the bevel exceeds the corner radius, which read as a second card inside the glass.
 if(raster.y>0) {
   float inset=optics.y*.6, rb=max(optics.x*.6,8);
   float2 qb=abs(local)-(lens.zw-inset)+rb;
   float sdb=length(max(qb,0))+min(max(qb.x,qb.y),0)-rb;
   float body=(1-smoothstep(-6,6,sdb))*raster.y;
   color=lerp(color,lerp(ReadFrost(coord),frostTint.rgb,frostTint.a),body);
 }
 // Continuous glass (rev2): one material across rim and body. The whole lens sees the same
 // blurred scene through the same refraction and dispersion; tint is luminance-adaptive in
 // linear light, so contrast is bounded per pixel without a uniform backing panel; the rim
 // reads as thicker glass through a density term from the same distance field.
 // raster.z = 1 selects the fully clear variant: the same material over the sharp refracted sample.
 if(material.x>0) {
   float3 c=color;
   if(raster.z<=0) { c=ReadFrost(coord); if(spectral>.001) { c.r=ReadFrost(coord+normal*spectral).r; c.b=ReadFrost(coord-normal*spectral).b; } }
   const float3 luma=float3(.2126,.7152,.0722);
   float3 lin=pow(max(c,0),2.2), tintLin=pow(frostTint.rgb,2.2);
   float y=dot(lin,luma), yt=dot(tintLin,luma);
   float need=material.z>0 ? (y<material.y ? (material.y-y)/max(yt-y,1e-4) : 0)
                           : (y>material.y ? (y-material.y)/max(y-yt,1e-4) : 0);
   float k=max(frostTint.a,saturate(need));
   float edge=pow(1-t,3)*material.w;
   lin=lerp(lerp(lin,tintLin,k),float3(1,1,1),edge*.35);
   color=pow(lin,1/2.2)*material.x+color*(1-material.x);
 }
 float incidence=dot(normal,normalize(float2(-.55,-.83)));
 float bright=pow(saturate(incidence),3);
 float reverse=pow(saturate(-incidence),3);
 float lobe=exp(-d/1.1)*.65+exp(-pow((d-optics.y*.22)/(optics.y*.16),2))*.16;
 color=lerp(color,float3(1,1,1),saturate(lighting.x*bright*lobe));
 color*=1-lighting.x*reverse*exp(-d/2.2)*.22;
 return float4(lerp(original,color,coverage),1);
}
)shader";
struct Renderer {
 Renderer() { ++resourceCounts[0]; }
 ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
 ComPtr<IDXGISwapChain1> chain; ComPtr<ISwapChainPanelNative> panel;
 ComPtr<ID3D11RenderTargetView> target; ComPtr<ID3D11VertexShader> vertex;
 ComPtr<ID3D11PixelShader> pixel, backgroundPixel; ComPtr<ID3D11Buffer> constants;
 ComPtr<ID3D11SamplerState> sampler; ComPtr<ID3D11ShaderResourceView> source;
 ComPtr<ID3D11Texture2D> sourceTexture;
 ComPtr<ID3D11Texture2D> frostTexture; ComPtr<ID3D11ShaderResourceView> frost;
 ComPtr<ID3D11Texture2D> frameReadback;
 bool frostValid=false; // only for the current scene revision at the current source size
 UINT64 frostCreated=0, frostPasses=0;
 GallerySource scene;
 UINT sourceWidth=0,sourceHeight=0;
 UINT width=0,height=0;
 ~Renderer() { if(panel) panel->SetSwapChain(nullptr); if(context) { context->ClearState(); context->Flush(); } ++resourceCounts[1]; }
 void Resize(UINT w,UINT h) {
   if(w==width&&h==height) return;
   context->OMSetRenderTargets(0,nullptr,nullptr);target.Reset();
   if(chain) { Check(chain->ResizeBuffers(2,w,h,DXGI_FORMAT_B8G8R8A8_UNORM,0)); ++resourceCounts[5]; }
   else {
    ComPtr<IDXGIDevice> dxgi;Check(device.As(&dxgi));
    ComPtr<IDXGIAdapter> adapter;Check(dxgi->GetAdapter(&adapter));
    ComPtr<IDXGIFactory2> factory;Check(adapter->GetParent(IID_PPV_ARGS(&factory)));
    DXGI_SWAP_CHAIN_DESC1 desc={};desc.Width=w;desc.Height=h;
    desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;
    desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;
    desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;desc.AlphaMode=DXGI_ALPHA_MODE_IGNORE;
    desc.Scaling=DXGI_SCALING_STRETCH;
    Check(factory->CreateSwapChainForComposition(device.Get(),&desc,nullptr,&chain));
    ++resourceCounts[4];
    Check(panel->SetSwapChain(chain.Get()));
   }
   ComPtr<ID3D11Texture2D> back;Check(chain->GetBuffer(0,IID_PPV_ARGS(&back)));
   Check(device->CreateRenderTargetView(back.Get(),nullptr,&target)); width=w;height=h;
 }
 void Init(IUnknown* p) {
  Check(p->QueryInterface(IID_PPV_ARGS(&panel)));
  D3D_FEATURE_LEVEL feature;
  Check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
    nullptr,0,D3D11_SDK_VERSION,&device,&feature,&context));
  ComPtr<ID3DBlob> vs,ps,error;
  Check(D3DCompile(shader,strlen(shader),"TheOrderOptical",nullptr,nullptr,"VS","vs_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&vs,&error));
  Check(D3DCompile(shader,strlen(shader),"TheOrderOptical",nullptr,nullptr,"PS","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&ps,&error));
  Check(device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&vertex));
  Check(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&pixel));
  // Separate plain presentation entry; the accepted optical shader is unchanged.
  const auto background=std::string(shader)+"\nfloat4 BackgroundPS(Vertex input):SV_TARGET { return float4(Read(input.uv*viewSource.xy),1); }";
  Check(D3DCompile(background.c_str(),background.size(),"TheOrderGalleryBackground",nullptr,nullptr,"BackgroundPS","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&ps,&error));
  Check(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&backgroundPixel));
  D3D11_BUFFER_DESC buffer={};buffer.ByteWidth=112;buffer.Usage=D3D11_USAGE_DEFAULT;buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
  Check(device->CreateBuffer(&buffer,nullptr,&constants));
  D3D11_SAMPLER_DESC sample={};sample.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
  sample.AddressU=sample.AddressV=sample.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sample.MaxLOD=D3D11_FLOAT32_MAX;
  Check(device->CreateSamplerState(&sample,&sampler));
 }
};
template<class F> HRESULT Guard(F&& f) noexcept { try { f(); return S_OK; } catch(HRESULT hr) { return hr; } catch(...) { return E_FAIL; } }
}
extern "C" __declspec(dllexport) HRESULT OpticalCreate(IUnknown* panel, void** result) noexcept {
 if(!panel||!result)return E_INVALIDARG;*result=nullptr;
 return Guard([&] { auto r=std::make_unique<Renderer>();r->Init(panel);*result=r.release(); });
}
extern "C" __declspec(dllexport) HRESULT OpticalUpload(void* handle,const BYTE* data,UINT w,UINT h,UINT length) noexcept {
 if(!handle||!data||w==0||h==0||w>8192||h>8192||static_cast<UINT64>(w)*h*4!=length)return E_INVALIDARG;
 return Guard([&] {auto r=static_cast<Renderer*>(handle);r->frostValid=false; // frost described the previous source
  if(r->sourceTexture&&w==r->sourceWidth&&h==r->sourceHeight) {
   // The immediate context preserves upload/draw ordering, including in-flight
   // reads. UpdateSubresource copies caller bytes before this call returns.
   r->context->UpdateSubresource(r->sourceTexture.Get(),0,nullptr,data,w*4,0);
   ++resourceCounts[2];return;
  }
  D3D11_TEXTURE2D_DESC d={};d.Width=w;d.Height=h;d.MipLevels=1;d.ArraySize=1;
  d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
  D3D11_SUBRESOURCE_DATA s={data,w*4,0};ComPtr<ID3D11Texture2D> texture;
  Check(r->device->CreateTexture2D(&d,&s,&texture));
  ++resourceCounts[3];
  ComPtr<ID3D11ShaderResourceView> view;Check(r->device->CreateShaderResourceView(texture.Get(),nullptr,&view));
  r->source=std::move(view);
  r->sourceTexture=std::move(texture);r->sourceWidth=w;r->sourceHeight=h;
  ++resourceCounts[2];
 });
}
static HRESULT Draw(void* handle,UINT width,UINT height,const float* input,UINT count,bool background,bool cacheFrame=false) noexcept {
 if(!handle||!input||(count!=20&&count!=24&&count!=28)||width==0||height==0||width>8192||height>8192)return E_INVALIDARG;
 float p[28]={};std::memcpy(p,input,count*sizeof(float)); // legacy 20/24-float callers keep later fields 0
 if(!(p[17]>=0&&p[17]<=1)||!(p[23]>=0&&p[23]<=1))return E_INVALIDARG;
 // rev2: enable/bound/edge in [0,1], polarity exactly +/-1 when enabled; rev1 and rev2 are exclusive.
 if(!(p[24]>=0&&p[24]<=1)||!(p[25]>=0&&p[25]<=1)||!(p[27]>=0&&p[27]<=1)||(p[24]>0&&(p[26]!=1&&p[26]!=-1))||(p[24]>0&&p[17]>0))return E_INVALIDARG;
 // Fully clear (c[18] = 1) is a variant of the continuous material only; it needs no blurred copy.
 const bool clear=p[18]!=0;
 if((p[18]!=0&&p[18]!=1)||(clear&&p[24]<=0))return E_INVALIDARG;
 return Guard([&] {auto r=static_cast<Renderer*>(handle);if(!r->source)throw E_UNEXPECTED;
 // A frost request without this revision's blurred copy fails closed; the owner falls back.
 if(!background&&(p[17]>0||(p[24]>0&&!clear))&&!r->frostValid)throw E_UNEXPECTED;
 r->Resize(width,height);
 ComPtr<IDXGISwapChain2> scaled;Check(r->chain.As(&scaled));
 DXGI_MATRIX_3X2_F matrix={1/p[16],0,0,1/p[16],0,0};Check(scaled->SetMatrixTransform(&matrix));
 r->context->UpdateSubresource(r->constants.Get(),0,nullptr,p,0,0);
 r->context->OMSetRenderTargets(1,r->target.GetAddressOf(),nullptr);
 D3D11_VIEWPORT viewport={0,0,static_cast<float>(width),static_cast<float>(height),0,1};r->context->RSSetViewports(1,&viewport);
 r->context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
 r->context->VSSetShader(r->vertex.Get(),nullptr,0);r->context->PSSetShader(background?r->backgroundPixel.Get():r->pixel.Get(),nullptr,0);
 r->context->VSSetConstantBuffers(0,1,r->constants.GetAddressOf());r->context->PSSetConstantBuffers(0,1,r->constants.GetAddressOf());
 r->context->PSSetSamplers(0,1,r->sampler.GetAddressOf());
 ID3D11ShaderResourceView* views[2]={r->source.Get(),r->frostValid&&!background?r->frost.Get():nullptr};r->context->PSSetShaderResources(0,2,views);
 r->context->Draw(3,0);
 if(cacheFrame) {
  r->frameReadback.Reset();
  ComPtr<ID3D11Resource> rendered;
  r->target->GetResource(&rendered);
  D3D11_TEXTURE2D_DESC desc={};
  desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
  desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;
  desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
  // A cache allocation failure must not affect presentation of the live frame.
  if(SUCCEEDED(r->device->CreateTexture2D(&desc,nullptr,&r->frameReadback)))
   r->context->CopyResource(r->frameReadback.Get(),rendered.Get());
 }
 Check(r->chain->Present(1,0));
 });
}
extern "C" __declspec(dllexport) HRESULT OpticalDraw(void* h,UINT w,UINT height,const float* p) noexcept { return Draw(h,w,height,p,20,false); }
extern "C" __declspec(dllexport) HRESULT OpticalDrawBackground(void* h,UINT w,UINT height,const float* p) noexcept { return Draw(h,w,height,p,20,true); }
extern "C" __declspec(dllexport) HRESULT OpticalDrawEx(void* h,UINT w,UINT height,const float* p,UINT count,BOOL background) noexcept { return Draw(h,w,height,p,count,background!=FALSE); }
extern "C" __declspec(dllexport) HRESULT OpticalDrawForCache(void* h,UINT w,UINT height,const float* p,UINT count) noexcept {
 if(static_cast<UINT64>(w)*height*4>24ULL*1024*1024)return E_INVALIDARG;
 return Draw(h,w,height,p,count,false,true);
}
extern "C" __declspec(dllexport) HRESULT OpticalReadCachedFrame(void* h,BYTE* data,UINT length) noexcept {
 if(!h)return E_INVALIDARG;
 auto r=static_cast<Renderer*>(h);
 if(!data&&length==0){r->frameReadback.Reset();return S_OK;}
 if(!data||!r->frameReadback)return E_INVALIDARG;
 D3D11_TEXTURE2D_DESC desc={};r->frameReadback->GetDesc(&desc);
 if(static_cast<UINT64>(desc.Width)*desc.Height*4!=length)return E_INVALIDARG;
 D3D11_MAPPED_SUBRESOURCE map={};
 const HRESULT hr=r->context->Map(r->frameReadback.Get(),0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&map);
 if(hr==DXGI_ERROR_WAS_STILL_DRAWING)return S_FALSE;
 if(FAILED(hr))return hr;
 const UINT rowBytes=desc.Width*4;
 for(UINT y=0;y<desc.Height;y++)
  std::memcpy(data+static_cast<size_t>(y)*rowBytes,static_cast<const BYTE*>(map.pData)+static_cast<size_t>(y)*map.RowPitch,rowBytes);
 r->context->Unmap(r->frameReadback.Get(),0);
 r->frameReadback.Reset();
 return S_OK;
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneBegin(void* handle,UINT w,UINT h,float scale,UINT color) noexcept {
 if(!handle||w==0||h==0||w>8192||h>8192||!std::isfinite(scale)||scale<=0)return E_INVALIDARG;
 return Guard([&]{auto r=static_cast<Renderer*>(handle);r->scene.Init(r->device.Get());
  if(r->scene.drawing)throw E_ILLEGAL_METHOD_CALL;
  ID3D11ShaderResourceView* empty[2]={};r->context->PSSetShaderResources(0,2,empty);r->frostValid=false;
  r->context->OMSetRenderTargets(0,nullptr,nullptr);
  if(!r->scene.target||r->sourceWidth!=w||r->sourceHeight!=h){
   D3D11_TEXTURE2D_DESC d={};d.Width=w;d.Height=h;d.MipLevels=1;d.ArraySize=1;
   d.Format=DXGI_FORMAT_B8G8R8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_DEFAULT;
   d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;
   ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11ShaderResourceView> view;
   Check(r->device->CreateTexture2D(&d,nullptr,&texture));Check(r->device->CreateShaderResourceView(texture.Get(),nullptr,&view));
   r->scene.SetSurface(texture.Get(),scale);
   r->sourceTexture=std::move(texture);r->source=std::move(view);r->sourceWidth=w;r->sourceHeight=h;++resourceCounts[3];
  }
  r->scene.Begin(scale,color);
 });
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneRect(void* h,float x,float y,float w,float height,UINT color) noexcept {
 if(!h)return E_INVALIDARG;return Guard([&]{static_cast<Renderer*>(h)->scene.Rect(x,y,w,height,color);});
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneText(void* h,const wchar_t* text,float x,float y,float size,UINT color,UINT weight) noexcept {
 if(!h||!text)return E_INVALIDARG;return Guard([&]{static_cast<Renderer*>(h)->scene.Text(text,x,y,size,color,weight);});
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneImage(void* h,const wchar_t* path,float x,float y,float w,float height,BOOL fill) noexcept {
 if(!h||!path)return E_INVALIDARG;return Guard([&]{static_cast<Renderer*>(h)->scene.Image(path,x,y,w,height,fill!=FALSE);});
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneImageBlur(void* h,const wchar_t* path,float x,float y,float w,float height,BOOL fill,float sigma) noexcept {
 if(!h||!path||!std::isfinite(sigma)||sigma<=0||sigma>64)return E_INVALIDARG;
 return Guard([&]{static_cast<Renderer*>(h)->scene.Image(path,x,y,w,height,fill!=FALSE,sigma);});
}
// Artwork stage: premultiplied BGRA pixels already decoded by the app's image pipeline.
extern "C" __declspec(dllexport) HRESULT OpticalScenePixels(void* h,const BYTE* data,UINT pw,UINT ph,UINT length,float x,float y,float w,float height,BOOL fill,float sigma) noexcept {
 if(!h||!data||pw==0||ph==0||pw>8192||ph>8192||static_cast<UINT64>(pw)*ph*4!=length||!std::isfinite(sigma)||sigma<0||sigma>64)return E_INVALIDARG;
 return Guard([&]{static_cast<Renderer*>(h)->scene.Pixels(data,pw,ph,x,y,w,height,fill!=FALSE,sigma);});
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneEnd(void* h) noexcept {
 if(!h)return E_INVALIDARG;return static_cast<Renderer*>(h)->scene.End();
}
extern "C" __declspec(dllexport) HRESULT OpticalSceneDiagnostics(void* h,UINT64* values) noexcept {
 if(!h||!values)return E_INVALIDARG;auto& s=static_cast<Renderer*>(h)->scene;
 values[0]=s.completed;values[1]=s.images;values[2]=s.texts;values[3]=s.targets;
 values[4]=s.drawing?1:0;values[5]=s.frameImages.size();values[6]=s.context?1:0;return S_OK;
}
// Event-driven: called once after a scene revision completes, never per frame.
// sigma<=0 retires the copy. A copy is valid only until the next Begin/Upload.
extern "C" __declspec(dllexport) HRESULT OpticalSceneFrost(void* handle,float sigma) noexcept {
 if(!handle||!std::isfinite(sigma)||sigma>64)return E_INVALIDARG;
 return Guard([&]{auto r=static_cast<Renderer*>(handle);
  ID3D11ShaderResourceView* empty[2]={};r->context->PSSetShaderResources(0,2,empty);r->frostValid=false;
  if(sigma<=0){r->scene.ReleaseFrost();r->frost.Reset();r->frostTexture.Reset();return;}
  if(!r->scene.target||r->scene.drawing||!r->sourceTexture)throw E_ILLEGAL_METHOD_CALL;
  D3D11_TEXTURE2D_DESC d={};r->sourceTexture->GetDesc(&d);
  D3D11_TEXTURE2D_DESC f={};if(r->frostTexture)r->frostTexture->GetDesc(&f);
  if(!r->frostTexture||f.Width!=d.Width||f.Height!=d.Height){
   r->scene.ReleaseFrost();r->frost.Reset();r->frostTexture.Reset();
   d.BindFlags=D3D11_BIND_SHADER_RESOURCE|D3D11_BIND_RENDER_TARGET;d.MiscFlags=0;
   Check(r->device->CreateTexture2D(&d,nullptr,&r->frostTexture));Check(r->device->CreateShaderResourceView(r->frostTexture.Get(),nullptr,&r->frost));
   ++r->frostCreated;
  }
  Check(r->scene.Frost(r->frostTexture.Get(),sigma));++r->frostPasses;r->frostValid=true;
 });
}
extern "C" __declspec(dllexport) HRESULT OpticalFrostDiagnostics(void* h,UINT64* values) noexcept {
 if(!h||!values)return E_INVALIDARG;auto r=static_cast<Renderer*>(h);
 values[0]=r->frostCreated;values[1]=r->frostPasses;values[2]=r->frostValid?1:0;values[3]=r->frostTexture?1:0;return S_OK;
}
extern "C" __declspec(dllexport) HRESULT OpticalDestroy(void* handle) noexcept {
 if(!handle)return S_OK;auto r=static_cast<Renderer*>(handle);
 // Detach on the owning UI thread before releasing the graph; retain for retry on failure.
 HRESULT hr=r->panel->SetSwapChain(nullptr);if(FAILED(hr))return hr;
 r->panel.Reset();delete r;return S_OK;
}

// Queried only by the explicit Test Gallery probe. No device, sampler or timer
// is created. Values describe this process across adapters, including XAML.
extern "C" __declspec(dllexport) HRESULT OpticalDiagnostics(UINT64* counts,UINT64* rows,UINT capacity,UINT* count) noexcept {
 if(!counts||!rows||!count||capacity<10)return E_INVALIDARG;*count=0;
 return Guard([&] {
  std::memcpy(counts,resourceCounts,sizeof(resourceCounts));
  ComPtr<IDXGIFactory1> factory;Check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
  for(UINT index=0;index<capacity/10;++index) {
   ComPtr<IDXGIAdapter1> adapter;HRESULT hr=factory->EnumAdapters1(index,&adapter);
   if(hr==DXGI_ERROR_NOT_FOUND)break;Check(hr);
   DXGI_ADAPTER_DESC1 desc={};Check(adapter->GetDesc1(&desc));
   auto p=rows+index*10;p[0]=desc.AdapterLuid.LowPart;p[1]=static_cast<UINT>(desc.AdapterLuid.HighPart);p[2]=desc.Flags;p[3]=index;
   ComPtr<IDXGIAdapter3> memory;hr=adapter.As(&memory);
   DXGI_QUERY_VIDEO_MEMORY_INFO local={},nonlocal={};
   HRESULT a=FAILED(hr)?hr:memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_LOCAL,&local);
   HRESULT b=FAILED(hr)?hr:memory->QueryVideoMemoryInfo(0,DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL,&nonlocal);
   p[4]=static_cast<UINT>(a);p[5]=local.CurrentUsage;p[6]=local.Budget;
   p[7]=static_cast<UINT>(b);p[8]=nonlocal.CurrentUsage;p[9]=nonlocal.Budget;++*count;
  }
 });
}
