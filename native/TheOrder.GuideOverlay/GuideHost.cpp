#include "GuideFlowCanvas.hpp"
#include "GuideLocalReader.hpp"
#include <objidl.h>
#include <gdiplus.h>
#include <detours.h>
#include <shellapi.h>
#include <windowsx.h>
#include <memory>
#pragma comment(lib, "gdiplus.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

static guide::Connection connection;
static DWORD targetPid = 0;
static HWND gameWindow = nullptr, overlayWindow = nullptr;
static int key = 0, hotkeyId = 701;
static ULONGLONG nextKeyAttempt = 0;
static bool registered = false, interactive = false;
static HANDLE keyboardThread=nullptr;static DWORD keyboardThreadId=0;static HANDLE keyboardReady=nullptr;static volatile LONG keyboardState=0;
static bool consumedKeys[256]{};
static std::wstring wide(const std::string& value) {
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), int(value.size()), nullptr, 0);
    std::wstring result(count, L'\0'); if (count) MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), int(value.size()), result.data(), count); return result;
}
static int inject(DWORD pid, LONG64 stamp, const std::wstring& path) {
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, FALSE, pid);
    if (!process) return 10;
    if (guide::creation(process) != stamp) { CloseHandle(process); return 11; }
    BOOL replaced = FALSE;
    int size = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, path.c_str(), -1, nullptr, 0, nullptr, &replaced);
    std::string name(size, '\0'); WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, path.c_str(), -1, name.data(), size, nullptr, &replaced);
    if (replaced || GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) { CloseHandle(process); return 12; }
    const char* dlls[] = { name.c_str() };
    // This helper is invoked only before the owned initial thread is resumed.
    BOOL ok = DetourUpdateProcessWithDll(process, dlls, 1); DWORD error = GetLastError(); CloseHandle(process);
    return ok ? 0 : int(error ? error : 13);
}
static BOOL CALLBACK findWindow(HWND window, LPARAM) {
    DWORD pid = 0; GetWindowThreadProcessId(window, &pid);
    if (pid == targetPid && IsWindowVisible(window) && !GetWindow(window, GW_OWNER)) { gameWindow = window; return FALSE; } return TRUE;
}
static bool front() {
    DWORD pid = 0; GetWindowThreadProcessId(GetForegroundWindow(), &pid);
    return pid == targetPid || GetForegroundWindow() == overlayWindow;
}
// Only the owned foreground game/reader consumes input. The dedicated hook
// thread posts a message; it never renders, reads documents, or logs key input.
static LRESULT CALLBACK keyboardInput(int code,WPARAM message,LPARAM value){
    auto* h=connection.header;if(code!=HC_ACTION||!h)return CallNextHookEx(nullptr,code,message,value);
    const auto* keyData=reinterpret_cast<const KBDLLHOOKSTRUCT*>(value);const DWORD keyCode=keyData->vkCode;
    if(keyCode>255)return CallNextHookEx(nullptr,code,message,value);
    bool up=message==WM_KEYUP||message==WM_SYSKEYUP,down=message==WM_KEYDOWN||message==WM_SYSKEYDOWN;
    if(up&&consumedKeys[keyCode]){consumedKeys[keyCode]=false;return 1;}
    if(!down)return CallNextHookEx(nullptr,code,message,value);
    if(consumedKeys[keyCode])return 1;
    int action=-1;
    const UINT modifiers=guide::activeModifiers();
    if(keyCode==DWORD(guide::shortcutKey(h->hotkey))&&guide::shortcutMatches(h->hotkey,modifiers)&&h->status!=2)action=hotkeyId;
    if(h->visible&&modifiers==0){const int keys[]={VK_NEXT,VK_PRIOR,VK_RIGHT,VK_LEFT,VK_HOME,VK_END,VK_SPACE};for(int i=0;i<7;++i)if(keyCode==DWORD(keys[i]))action=hotkeyId+6+i;}
    if(action<0||!connection.live()||!front())return CallNextHookEx(nullptr,code,message,value);
    consumedKeys[keyCode]=true;PostMessageW(overlayWindow,WM_HOTKEY,WPARAM(action),0);return 1;
}
static DWORD WINAPI keyboardLoop(LPVOID){
    MSG message{};PeekMessageW(&message,nullptr,WM_USER,WM_USER,PM_NOREMOVE);
    HHOOK hook=SetWindowsHookExW(WH_KEYBOARD_LL,keyboardInput,GetModuleHandleW(nullptr),0);InterlockedExchange(&keyboardState,hook?1:-1);SetEvent(keyboardReady);
    if(hook){while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}UnhookWindowsHookEx(hook);}return 0;
}
static void requestPaint() {
    if (!gameWindow || !IsWindow(gameWindow)) { gameWindow = nullptr; EnumWindows(findWindow, 0); }
    if (gameWindow) RedrawWindow(gameWindow, nullptr, nullptr, RDW_INVALIDATE);
}
static Gdiplus::RectF drawText(Gdiplus::Graphics& graphics, const std::wstring& value, float x, float y, float width, float size, Gdiplus::Color color, bool bold = false, const wchar_t* family = L"Microsoft YaHei UI") {
    Gdiplus::Font font(family, size, bold ? Gdiplus::FontStyleBold : Gdiplus::FontStyleRegular, Gdiplus::UnitPixel);
    Gdiplus::SolidBrush brush(color); Gdiplus::RectF box(x, y, width, 10000), measured;
    Gdiplus::StringFormat format; format.SetTrimming(Gdiplus::StringTrimmingNone);
    graphics.MeasureString(value.c_str(), -1, &font, box, &format, &measured);
    graphics.DrawString(value.c_str(), -1, &font, box, &format, &brush); return measured;
}
static void rounded(Gdiplus::Graphics& graphics, Gdiplus::Brush& brush, float x, float y, float width, float height, float radius) {
    Gdiplus::GraphicsPath path; float diameter=radius*2;
    path.AddArc(x,y,diameter,diameter,180,90);path.AddArc(x+width-diameter,y,diameter,diameter,270,90);
    path.AddArc(x+width-diameter,y+height-diameter,diameter,diameter,0,90);path.AddArc(x,y+height-diameter,diameter,diameter,90,90);
    path.CloseFigure();graphics.FillPath(&brush,&path);
}
struct Hit { RECT box; int action,argument; };
static std::vector<Hit> hits;
static bool showOriginal=false;
static bool compactWindow=false;
static float currentScale=1;
static int preserveFirst=-1;
static float bodyScroll=0,sideScroll=0,maximumScroll=0,sideMaximum=0;
static float savedReadingScroll=0,savedOverviewScroll=0;
static bool draggingBody=false;
static RECT bodyBox{},sideBox{};
static int lastRoute=-1,lastStep=-1;static LONG lastRevision=-1;
struct PaintStamp { LONG revision=-1,route=-1,step=-1,graph=-1,selected=-1,light=-1,dpi=0;RECT target{}; };
static PaintStamp painted;
static float textHeight(Gdiplus::Graphics& g,const std::string& value,float width,float size,const wchar_t* family=L"Microsoft YaHei UI") {
    Gdiplus::Font font(family,size,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
    Gdiplus::RectF result,box(0,0,width,100000);auto label=wide(value);g.MeasureString(label.c_str(),-1,&font,box,nullptr,&result);return result.Height;
}
static void hit(float x,float y,float w,float h,int action,int argument,const RECT* clip=nullptr) {
    RECT rect{LONG(x),LONG(y),LONG(x+w),LONG(y+h)};
    if(clip){RECT clipped{};if(!IntersectRect(&clipped,&rect,clip))return;rect=clipped;}
    hits.push_back({rect,action,argument});
}
static void button(Gdiplus::Graphics& g,const std::wstring& label,float x,float y,float w,float h,float scale,int action,int argument=0,bool selected=false) {
    bool light=connection.header&&connection.header->lightTheme;

    if(selected){Gdiplus::Pen line(light?Gdiplus::Color(255,67,82,103):Gdiplus::Color(255,194,201,214),scale);g.DrawLine(&line,x+10*scale,y+h-2*scale,x+w-10*scale,y+h-2*scale);}
    const float pad=(compactWindow?6.f:10.f)*scale;
    drawText(g,label,x+pad,y+4*scale,w-2*pad,(compactWindow?12.f:15.f)*scale,light?Gdiplus::Color(255,43,44,47):Gdiplus::Color(255,233,235,239));hit(x,y,w,h,action,argument);
}
static void tab(Gdiplus::Graphics& g,const wchar_t* label,float x,float y,float width,float scale,int page,bool active) {
    const bool light=connection.header&&connection.header->lightTheme;
    const auto ink=light?Gdiplus::Color(255,43,46,51):Gdiplus::Color(255,233,236,241);
    const auto muted=light?Gdiplus::Color(255,102,108,118):Gdiplus::Color(255,164,172,186);
    Gdiplus::Font font(L"Microsoft YaHei UI",13.f*scale,Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);
    Gdiplus::StringFormat format(Gdiplus::StringFormat::GenericTypographic());format.SetFormatFlags(format.GetFormatFlags()|Gdiplus::StringFormatFlagsNoWrap);
    Gdiplus::RectF measured;g.MeasureString(label,-1,&font,Gdiplus::PointF(0,0),&format,&measured);
    const float labelX=x+(width-measured.Width)/2;
    Gdiplus::SolidBrush brush(active?ink:muted);g.DrawString(label,-1,&font,Gdiplus::PointF(labelX,y+4*scale),&format,&brush);
    if(active){Gdiplus::Pen line(ink,2*scale);g.DrawLine(&line,labelX,y+22*scale,labelX+measured.Width,y+22*scale);}
    hit(x,y,width,26*scale,2,page);
}
static float tocScroll=0,tocMaximum=0;
static RECT tocBox{};
static guide::flow::State flowState;
static guide::local::State localState;
struct FlowGdiPainter {
    Gdiplus::Graphics& g;std::vector<Gdiplus::GraphicsState> clips;
    static Gdiplus::Color color(guide::flow::Color c){return Gdiplus::Color(BYTE(c.a),BYTE(c.r),BYTE(c.g),BYTE(c.b));}
    void box(guide::flow::Rect r,guide::flow::Color fill,guide::flow::Color stroke,float radius,float thickness=1){Gdiplus::SolidBrush brush(color(fill));rounded(g,brush,r.x,r.y,r.w,r.h,std::min(radius,std::min(r.w,r.h)/2));Gdiplus::Pen pen(color(stroke),thickness);Gdiplus::GraphicsPath path;float d=std::min(radius,std::min(r.w,r.h)/2)*2;path.AddArc(r.x,r.y,d,d,180,90);path.AddArc(r.x+r.w-d,r.y,d,d,270,90);path.AddArc(r.x+r.w-d,r.y+r.h-d,d,d,0,90);path.AddArc(r.x,r.y+r.h-d,d,d,90,90);path.CloseFigure();g.DrawPath(&pen,&path);}
    void line(guide::Point a,guide::Point b,guide::flow::Color c,float width){Gdiplus::Pen pen(color(c),width);g.DrawLine(&pen,a.x,a.y,b.x,b.y);}
    void polyline(const std::vector<guide::Point>& points,guide::flow::Color c,float width){if(points.size()<2)return;std::vector<Gdiplus::PointF> vertices;vertices.reserve(points.size());for(auto p:points)vertices.emplace_back(p.x,p.y);Gdiplus::Pen pen(color(c),width);pen.SetLineJoin(Gdiplus::LineJoinRound);pen.SetStartCap(Gdiplus::LineCapRound);pen.SetEndCap(Gdiplus::LineCapRound);g.DrawLines(&pen,vertices.data(),int(vertices.size()));}
    void polygon(const std::vector<guide::Point>& points,guide::flow::Color fill,guide::flow::Color stroke,float thickness=1){std::vector<Gdiplus::PointF> vertices;for(auto p:points)vertices.emplace_back(p.x,p.y);Gdiplus::SolidBrush brush(color(fill));Gdiplus::Pen pen(color(stroke),thickness);g.FillPolygon(&brush,vertices.data(),int(vertices.size()));g.DrawPolygon(&pen,vertices.data(),int(vertices.size()));}
    void text(const std::string& value,guide::flow::Rect r,float size,guide::flow::Color c,bool center,bool strong=false){if(r.w<4||r.h<4)return;Gdiplus::Font font(L"Microsoft YaHei UI",size,strong?Gdiplus::FontStyleBold:Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);Gdiplus::StringFormat format;format.SetFormatFlags(Gdiplus::StringFormatFlagsNoWrap);format.SetTrimming(Gdiplus::StringTrimmingEllipsisCharacter);format.SetAlignment(center?Gdiplus::StringAlignmentCenter:Gdiplus::StringAlignmentNear);format.SetLineAlignment(Gdiplus::StringAlignmentCenter);Gdiplus::SolidBrush brush(color(c));auto label=wide(value);g.DrawString(label.c_str(),-1,&font,Gdiplus::RectF(r.x+4,r.y,r.w-8,r.h),&format,&brush);}
    void clip(guide::flow::Rect r){clips.push_back(g.Save());g.SetClip(Gdiplus::RectF(r.x,r.y,r.w,r.h));}
    void unclip(){g.Restore(clips.back());clips.pop_back();}
    float paragraph(const std::string& value,float x,float y,float width,float size,guide::flow::Color c,bool strong,bool draw){
        Gdiplus::Font font(L"Microsoft YaHei UI",size,strong?Gdiplus::FontStyleBold:Gdiplus::FontStyleRegular,Gdiplus::UnitPixel);Gdiplus::StringFormat format;format.SetFormatFlags(Gdiplus::StringFormatFlagsMeasureTrailingSpaces);auto label=wide(value);Gdiplus::RectF measured;
        g.MeasureString(label.c_str(),-1,&font,Gdiplus::RectF(x,y,width,100000),&format,&measured);float height=std::max(size*1.5f,measured.Height);
        if(draw){Gdiplus::SolidBrush brush(color(c));g.DrawString(label.c_str(),-1,&font,Gdiplus::RectF(x,y,width,height+size),&format,&brush);}return height;
    }
    void image(guide::flow::Rect r,const guide::Reading& page){
        static LONG revision=-1;static std::vector<BYTE> bgra;static std::unique_ptr<Gdiplus::Bitmap> bitmap;
        if(revision!=connection.header->sequence){bitmap.reset();bgra=page.rgba;for(size_t i=0;i<bgra.size();i+=4)std::swap(bgra[i],bgra[i+2]);bitmap=std::make_unique<Gdiplus::Bitmap>(page.width,page.height,page.width*4,PixelFormat32bppARGB,bgra.data());revision=connection.header->sequence;}
        if(bitmap){g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);g.DrawImage(bitmap.get(),Gdiplus::RectF(r.x,r.y,r.w,r.h));}
    }
};
static bool localVisible(){return connection.header&&connection.header->channel!=0;}
static bool flowVisible(){return connection.header&&!localVisible()&&connection.header->graph&&!connection.document.flow.nodes.empty();}
static void readFlow(){if(flowState.readRoute<0)return;auto* h=connection.header;h->route=flowState.readRoute;h->step=flowState.readStep;h->graph=0;lastStep=-1;flowState.readRoute=-1;bodyScroll=0;}
static void paint(HWND window) {
    auto* h=connection.header;
    if(!h)return;
    auto& document=connection.document;
    RECT target{};
    if(!gameWindow||!GetWindowRect(gameWindow,&target)) {
        MONITORINFO monitor{sizeof(monitor)};
        GetMonitorInfoW(MonitorFromWindow(gameWindow,MONITOR_DEFAULTTOPRIMARY),&monitor);
        target=monitor.rcMonitor;
    }
    auto layout=guide::reading::layout(float(target.right-target.left),float(target.bottom-target.top),float(h->dpi>0?h->dpi:96));
    const float s=layout.scale;
    const bool compact=layout.compact;compactWindow=compact;currentScale=s;
    const auto type=guide::reading::style(compact);
    const int width=int(layout.width),height=int(layout.height);
    if(width<320||height<260)return;
    h->panelWidth=width;h->panelHeight=height;h->bodyFontPx=LONG(type.body*s);
    h->showOriginal=showOriginal;h->readerBackend=2;h->visibleSteps=0;h->overflowRows=0;
    h->readingWidth=LONG(layout.readerWidth);h->navigationWidth=LONG(layout.sidebar);h->indexWidth=LONG(layout.tocWidth);
    h->layoutMode=layout.tocWidth>0?3:layout.sidebar>0?2:1;h->fontRoles=3;
    BITMAPINFO info{};
    info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-height;
    info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
    void* pixels=nullptr;
    HDC screen=GetDC(nullptr),memory=CreateCompatibleDC(screen);
    HBITMAP image=CreateDIBSection(screen,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
    if(!image||!pixels) {if(image)DeleteObject(image);DeleteDC(memory);ReleaseDC(nullptr,screen);return;}
    HGDIOBJ previous=SelectObject(memory,image);
    memset(pixels,0,size_t(width)*height*4);hits.clear();
    {
        Gdiplus::Bitmap bitmap(width,height,width*4,PixelFormat32bppPARGB,static_cast<BYTE*>(pixels));
        Gdiplus::Graphics g(&bitmap);
        g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
        g.SetTextRenderingHint(Gdiplus::TextRenderingHintAntiAliasGridFit);
        const bool light=h->lightTheme!=0;
        const auto ink=light?Gdiplus::Color(255,43,46,51):Gdiplus::Color(255,233,236,241);
        const auto muted=light?Gdiplus::Color(255,102,108,118):Gdiplus::Color(255,164,172,186);
        const auto accent=light?Gdiplus::Color(255,68,85,108):Gdiplus::Color(255,193,206,226);
        const auto rule=light?Gdiplus::Color(32,95,100,111):Gdiplus::Color(30,185,191,202);
        Gdiplus::SolidBrush background(light?Gdiplus::Color(247,248,247,244):Gdiplus::Color(244,21,24,29));
        rounded(g,background,0,0,float(width),float(height),8*s);
        const float outer=layout.inset;
        const bool attribution=h->channel==0&&!document.attribution.empty();
        const auto heading=guide::reading::header(layout,document.reading.present,attribution);
        layout.contentTop=heading.contentTop;
        const float top=heading.top,closeWidth=48*s;
        g.SetClip(Gdiplus::RectF(outer,top,heading.titleWidth,24*s));
        drawText(g,wide(document.title),outer,top+2*s,heading.titleWidth,(compact?16.f:18.f)*s,ink,false,L"SimSun");
        g.ResetClip();
        if(attribution){g.SetClip(Gdiplus::RectF(outer,heading.attributionY,width-2*outer,14*s));
            drawText(g,wide(document.attribution),outer,heading.attributionY,width-2*outer,11*s,muted);g.ResetClip();}
        {FlowGdiPainter painter{g};painter.text("收起",{width-outer-closeWidth,top,closeWidth,24*s},13*s,
            light?guide::flow::Color{43,44,47}:guide::flow::Color{233,235,239},true);hit(width-outer-closeWidth,top,closeWidth,24*s,0,0);}
        Gdiplus::Pen divider(rule,s);
        g.DrawLine(&divider,outer,heading.ruleY,width-outer,heading.ruleY);
        if(document.reading.present){FlowGdiPainter painter{g};guide::local::sourceBar(painter,localState,document,h,{heading.sourceX,heading.sourceY,heading.sourceWidth,24*s},s,light);}
        if(localVisible()) {
            FlowGdiPainter painter{g};guide::local::render(painter,localState,document,h,{outer,layout.contentTop,width-2*outer,layout.contentBottom-layout.contentTop},s,light);
            sideBox=tocBox=bodyBox={};sideMaximum=tocMaximum=maximumScroll=0;h->navigationWidth=h->indexWidth=0;h->readingWidth=LONG(width-2*outer);
            h->layoutMode=4;h->visibleSteps=LONG(document.reading.blocks.size());h->readingFirst=document.reading.page;h->bodyFontPx=LONG(16*s*localState.zoom);h->scrollY=LONG(localState.scrollY);h->visibleStage=LONG(localState.maximumY);h->flowZoom=LONG(localState.zoom*1000);h->readerAck=h->sequence;
        } else if(h->graph&&!document.flow.nodes.empty()) {
            const float y=layout.contentTop;
            tab(g,L"流程图",outer,y,80*s,s,1,true);tab(g,L"连续阅读",outer+96*s,y,96*s,s,0,false);
            FlowGdiPainter painter{g};guide::flow::render(painter,flowState,document,{outer,y+30*s,width-2*outer,layout.contentBottom-y-30*s},s,light,h->route,h->sequence);
            h->flowNodes=LONG(flowState.activeNodes.size());h->flowEdges=LONG(flowState.activeEdges.size());h->flowSelected=flowState.selectedEdge;h->flowZoom=LONG(flowState.zoom*1000);
            sideBox=tocBox=bodyBox={};sideMaximum=tocMaximum=maximumScroll=0;h->navigationWidth=h->indexWidth=0;h->readingWidth=LONG(width-2*outer);
        } else {
        int routeIndex=document.routes.empty()?-1:std::clamp(int(h->route),0,int(document.routes.size())-1);
        if(lastRoute!=routeIndex||lastRevision!=h->sequence) {
            bodyScroll=savedReadingScroll=savedOverviewScroll=0;tocScroll=0;lastStep=-1;lastRoute=routeIndex;lastRevision=h->sequence;preserveFirst=-1;
        }
        if(layout.sidebar>0) {
            const float x=outer,w=layout.sidebar;
            drawText(g,L"路线目录",x,layout.contentTop,w,13*s,muted);
            sideBox={LONG(x),LONG(layout.contentTop+30*s),LONG(x+w),LONG(layout.contentBottom)};
            float y=float(sideBox.top)-sideScroll;
            g.SetClip(Gdiplus::RectF(x,float(sideBox.top),w,float(sideBox.bottom-sideBox.top)));
            for(int i=0;i<int(document.routes.size());++i) {
                const auto& route=document.routes[i];
                const std::string meta=route.to+" · "+std::to_string(guide::reading::stepCount(route))+"步";
                const float row=textHeight(g,guide::reading::routeName(route),w-42*s,17*s,L"SimSun")+textHeight(g,meta,w-42*s,12*s)+20*s;
                if(y+row>sideBox.top&&y<sideBox.bottom) {
                    if(i==routeIndex) {
                        Gdiplus::SolidBrush selected(light?Gdiplus::Color(19,105,123,150):Gdiplus::Color(15,180,195,220));
                        rounded(g,selected,x,y,w,row,3*s);
                        Gdiplus::Pen mark(accent,2*s);g.DrawLine(&mark,x,y,x,y+row);
                    }
                    drawText(g,std::to_wstring(i+1),x+9*s,y+10*s,22*s,11*s,muted,false,L"Consolas");
                    float at=y+8*s;
                    at+=drawText(g,wide(guide::reading::routeName(route)),x+34*s,at,w-42*s,17*s,i==routeIndex?accent:ink,false,L"SimSun").Height+4*s;
                    drawText(g,wide(meta),x+34*s,at,w-42*s,12*s,muted);
                    hit(x,y,w,row,1,i,&sideBox);
                }
                y+=row+4*s;
            }
            g.ResetClip();
            sideMaximum=std::max(0.f,y+sideScroll-sideBox.bottom);sideScroll=std::clamp(sideScroll,0.f,sideMaximum);
        } else {sideBox={};sideMaximum=0;}
        const float x=layout.readerX,w=layout.readerWidth;
        float y=layout.contentTop;
        if(document.sources.size()>1) {button(g,L"切换攻略来源",x,y,w,(compact?24.f:28.f)*s,s,6);y+=(compact?28.f:36.f)*s;}
        if(!compact&&layout.sidebar==0&&!h->graph) {button(g,L"下一路线",x,y,w,28*s,s,7,1);y+=36*s;}
        if(compact) {
            if(h->graph){drawText(g,L"路线概览",x,y,90*s,17*s,ink,false,L"SimSun");drawText(g,std::to_wstring(document.routes.size())+L" 条路线 · "+std::to_wstring(guide::reading::stepCount(document))+L" 步",x+94*s,y+6*s,w-94*s,11*s,muted);y+=28*s;}
            else if(routeIndex>=0){
                const auto& route=document.routes[routeIndex];const float arrows=layout.sidebar==0?66*s:0;
                g.SetClip(Gdiplus::RectF(x,y,w-arrows,25*s));drawText(g,wide(guide::reading::routeName(route))+L" · "+std::to_wstring(routeIndex+1)+L"/"+std::to_wstring(document.routes.size()),x,y,w-arrows,17*s,ink,false,L"SimSun");g.ResetClip();
                if(arrows){button(g,L"‹",x+w-62*s,y,28*s,24*s,s,7,-1);button(g,L"›",x+w-28*s,y,28*s,24*s,s,7,1);}y+=26*s;
                y+=drawText(g,wide(route.from+" → "+route.to),x,y,w,11*s,muted).Height+3*s;
                const auto separator=route.title.find(" · ");if(separator!=std::string::npos)y+=drawText(g,wide(route.title.substr(separator+4)),x,y,w,12*s,muted).Height+3*s;
            }
        } else if(h->graph) {
            drawText(g,L"攻略结构",x,y,w,12*s,muted);y+=22*s;
            y+=drawText(g,L"路线概览",x,y,w,23*s,ink,false,L"SimSun").Height+8*s;
            y+=drawText(g,std::to_wstring(document.routes.size())+L" 条路线段 · "+std::to_wstring(guide::reading::stepCount(document))+L" 个步骤",x,y,w,13*s,muted).Height+8*s;
        } else if(routeIndex>=0) {
            const auto& route=document.routes[routeIndex];
            drawText(g,L"路线 "+std::to_wstring(routeIndex+1)+L" / "+std::to_wstring(document.routes.size()),x,y,w,12*s,muted);
            y+=22*s;
            y+=drawText(g,wide(guide::reading::routeName(route)),x,y,w,23*s,ink,false,L"SimSun").Height+8*s;
            y+=drawText(g,wide(route.from+"  →  "+route.to),x,y,w,13*s,muted).Height+8*s;
            const auto separator=route.title.find(" · ");
            if(separator!=std::string::npos)y+=drawText(g,wide("说明："+route.title.substr(separator+4)),x,y,w,13*s,muted).Height+8*s;
        }
        tab(g,document.flow.nodes.empty()?L"路线概览":L"流程图",x,y,80*s,s,1,h->graph!=0);
        tab(g,L"连续阅读",x+96*s,y,96*s,s,0,!h->graph);
        if(!h->graph)button(g,showOriginal?L"✓ 原文对照":L"原文对照",x+w-116*s,y,116*s,30*s,s,3);
        else drawText(g,L"原文对照",x+w-106*s,y+5*s,96*s,(compact?12.f:15.f)*s,Gdiplus::Color(100,muted.GetR(),muted.GetG(),muted.GetB()));
        y+=(compact?30.f:39.f)*s;
        if(!document.status.empty())y+=drawText(g,wide(document.status),x,y,w,18*s,accent).Height+8*s;
        if(routeIndex>=0) {
            const auto& route=document.routes[routeIndex];
            const float locationY=y;
            if(!h->graph)y+=(compact?18.f:24.f)*s;
            bodyBox={LONG(x),LONG(y),LONG(x+w),LONG(layout.contentBottom)};
            const float bodyHeight=std::max(32.f,float(bodyBox.bottom-bodyBox.top));
            const float inset=guide::reading::rowInset(guide::reading::stepCount(route),compact);
            const float rowWidth=w-8*s,textWidth=rowWidth-(inset+12)*s;
            std::vector<float> offsets{0};
            if(h->graph) {
                for(const auto& item:document.routes)
                    offsets.push_back(offsets.back()+textHeight(g,item.title,rowWidth-24*s,(compact?16.f:19.f)*s,L"SimSun")+textHeight(g,item.from+" → "+item.to+(compact?" · "+std::to_string(guide::reading::stepCount(item))+"步":""),rowWidth-24*s,(compact?11.f:13.f)*s)+(compact?12.f:43.f)*s);
            } else {
                for(const auto& step:route.steps)
                    offsets.push_back(offsets.back()+guide::reading::rowHeight(step,rowWidth,s,showOriginal,
                        [&](const std::string& value,float wrap,float font,int role){return textHeight(g,value,wrap,font*s,role==2?L"SimSun":L"Microsoft YaHei UI");},inset,compact));
            }
            int selected=std::clamp(int(h->step),0,std::max(0,int(route.steps.size())-1));h->step=selected;
            if(!h->graph&&!route.steps.empty()&&lastStep!=selected) {
                if(lastStep<0||offsets[selected]<bodyScroll||offsets[selected+1]>bodyScroll+bodyHeight)bodyScroll=offsets[selected];
                lastStep=selected;
            }
            if(!h->graph&&preserveFirst>=0&&!route.steps.empty()) {
                bodyScroll=offsets[std::clamp(preserveFirst,0,int(route.steps.size())-1)];preserveFirst=-1;
            }
            maximumScroll=std::max(0.f,offsets.back()+40*s-bodyHeight);
            bodyScroll=std::clamp(bodyScroll,0.f,maximumScroll);h->scrollY=LONG(bodyScroll);
            int first=std::clamp(int(std::upper_bound(offsets.begin(),offsets.end(),bodyScroll+4*s)-offsets.begin())-1,0,std::max(0,int(route.steps.size())-1));
            int chapter=0;
            for(int i=0;i<=first&&i<int(route.steps.size());++i)if(guide::reading::anchor(route.steps[i]))chapter=i;
            h->readingFirst=first;h->visibleStage=chapter;h->rowPitch=LONG((type.body+type.padding+type.gap)*s);
            if(!h->graph)drawText(g,wide(guide::reading::sectionAt(route,first)+" · 阅读位置 "+guide::reading::position(route,first)),x,locationY,w,(compact?11.f:12.f)*s,muted);
            g.SetClip(Gdiplus::RectF(x,y,w,bodyHeight));
            for(int i=0;i<int(offsets.size())-1;++i) {
                const float at=y+offsets[i]-bodyScroll,row=offsets[i+1]-offsets[i];
                if(at+row<y||at>bodyBox.bottom)continue;
                if(h->graph) {
                    const auto& item=document.routes[i];float line=at;
                    line+=drawText(g,wide(item.title),x,line,rowWidth-24*s,(compact?16.f:19.f)*s,ink,false,L"SimSun").Height+(compact?3.f:8.f)*s;
                    line+=drawText(g,wide(item.from+" → "+item.to+(compact?" · "+std::to_string(guide::reading::stepCount(item))+"步":"")),x,line,rowWidth-24*s,(compact?11.f:13.f)*s,muted).Height+(compact?3.f:8.f)*s;
                    if(!compact)drawText(g,std::to_wstring(guide::reading::stepCount(item))+L" 个步骤 · 点击阅读",x,line,rowWidth-24*s,12*s,muted);
                    g.DrawLine(&divider,x,at+row-5*s,x+rowWidth,at+row-5*s);hit(x,at,rowWidth,row,1,i,&bodyBox);
                } else {
                    const auto& step=route.steps[i];++h->visibleSteps;
                    const auto color=step.kind=="save"?(light?Gdiplus::Color(255,148,87,25):Gdiplus::Color(255,228,196,143)):
                        step.kind=="load"?(light?Gdiplus::Color(255,110,79,166):Gdiplus::Color(255,194,182,221)):accent;
                    if(!step.heading) {
                        if(i==selected) {
                            Gdiplus::SolidBrush mark(light?Gdiplus::Color(17,97,116,142):Gdiplus::Color(13,180,195,220));
                            rounded(g,mark,x,at,rowWidth,row-3*s,3*s);
                        }
                        const int number=guide::reading::stepNumber(route,i);
                        drawText(g,(number<10?L"0":L"")+std::to_wstring(number),x+(compact?5.f:8.f)*s,at+6*s,(inset-(compact?35.f:44.f))*s,type.label*s,muted,false,L"Consolas");
                        drawText(g,wide(guide::kind(step.kind)),x+(inset-(compact?30.f:35.f))*s,at+6*s,(compact?25.f:30.f)*s,type.label*s,color);
                    } else {
                        drawText(g,L"阶段",x+5*s,at+(compact?11.f:14.f)*s,46*s,type.label*s,muted);
                        g.DrawLine(&divider,x+inset*s,at+row-5*s,x+rowWidth-12*s,at+row-5*s);
                    }
                    float line=at+(step.heading?(compact?10.f:13.f):(compact?4.f:5.f))*s;
                    auto paragraph=[&](const std::string& value,float font,Gdiplus::Color textColor,const wchar_t* family=L"Microsoft YaHei UI") {
                        line+=drawText(g,wide(value),x+inset*s,line,textWidth,font*s,textColor,false,family).Height;
                    };
                    paragraph(guide::reading::primary(step),step.heading?type.heading:type.body,step.heading?accent:ink,step.heading?L"SimSun":L"Microsoft YaHei UI");
                    if(showOriginal&&guide::reading::translated(step)) {line+=(compact?4.f:6.f)*s;paragraph(step.original,type.original,muted);}
                    if(step.achievement) {line+=(compact?4.f:6.f)*s;paragraph("成就相关",type.label,color);}
                    if(!step.alternatives.empty()) {
                        line+=(compact?5.f:7.f)*s;paragraph("其他选项",type.label,muted);line+=(compact?3.f:5.f)*s;
                        for(const auto& alt:step.alternatives) {
                            paragraph("· "+guide::reading::primary(alt),type.alternative,muted);line+=(compact?3.f:4.f)*s;
                            if(showOriginal&&guide::reading::translated(alt)) {paragraph(alt.original,type.alternativeOriginal,muted);line+=(compact?3.f:4.f)*s;}
                        }
                    }
                    if(line>at+row)++h->overflowRows;
                    hit(x,at,rowWidth,row,4,i,&bodyBox);
                }
            }
            g.ResetClip();
            if(maximumScroll>0) {
                const float trackX=x+w-4*s,thumb=std::max(20*s,bodyHeight*bodyHeight/(bodyHeight+maximumScroll));
                Gdiplus::SolidBrush scrollInk(light?Gdiplus::Color(110,115,124,137):Gdiplus::Color(120,155,165,180));
                g.FillRectangle(&scrollInk,trackX,y+(bodyHeight-thumb)*bodyScroll/maximumScroll,3*s,thumb);
            }
            if(layout.tocWidth>0&&!h->graph) {
                const float tx=layout.tocX,tw=layout.tocWidth;
                drawText(g,L"本段定位",tx,layout.contentTop,tw,13*s,muted);
                tocBox={LONG(tx),LONG(layout.contentTop+30*s),LONG(tx+tw),LONG(layout.contentBottom-56*s)};
                float at=float(tocBox.top)-tocScroll;
                g.SetClip(Gdiplus::RectF(tx,float(tocBox.top),tw,float(tocBox.bottom-tocBox.top)));
                for(int i=0;i<int(route.steps.size());++i) {
                    const auto& step=route.steps[i];if(!guide::reading::anchor(step))continue;
                    const float row=textHeight(g,guide::reading::primary(step),tw-16*s,14*s)+15*s;
                    if(at+row>tocBox.top&&at<tocBox.bottom) {
                        if(i==chapter){Gdiplus::Pen mark(accent,2*s);g.DrawLine(&mark,tx,at,tx,at+row-3*s);}
                        drawText(g,wide(guide::reading::primary(step)),tx+10*s,at+5*s,tw-16*s,14*s,i==chapter?ink:muted);
                        hit(tx,at,tw,row,8,i,&tocBox);
                    }
                    at+=row;
                }
                g.ResetClip();
                tocMaximum=std::max(0.f,at+tocScroll-tocBox.bottom);tocScroll=std::clamp(tocScroll,0.f,tocMaximum);
                drawText(g,L"阅读位置",tx,layout.contentBottom-43*s,tw,12*s,muted);
                drawText(g,wide(guide::reading::position(route,first)),tx,layout.contentBottom-23*s,tw,14*s,ink,false,L"Consolas");
            } else {
                tocBox={};
                if(layout.tocWidth>0) {
                    const float tx=layout.tocX,tw=layout.tocWidth;float at=layout.contentTop;
                    drawText(g,L"阅读提示",tx,at,tw,13*s,muted);at+=38*s;
                    at+=drawText(g,L"选择路线进入正文。",tx,at,tw,14*s,ink).Height+8*s;
                    drawText(g,L"路线段按原攻略顺序排列，存读档关联保持不变。",tx,at,tw,13*s,muted);
                }
            }
        }
        }
    }
    POINT destination{target.left+LONG(layout.x),target.top+LONG(layout.y)},origin{};
    SIZE size{width,height};BLENDFUNCTION blend{AC_SRC_OVER,0,255,AC_SRC_ALPHA};
    UpdateLayeredWindow(window,screen,&destination,&size,memory,&origin,0,&blend,ULW_ALPHA);
    SelectObject(memory,previous);DeleteObject(image);DeleteDC(memory);ReleaseDC(nullptr,screen);
    painted={h->sequence,h->route,h->step,h->graph,h->selected,h->lightTheme,h->dpi,target};
}
static void registerKeys(HWND window) {
    auto* h = connection.header; if (!h) return;
    if (!front()) {
        key = 0; nextKeyAttempt = 0; registered = false; h->status = 0; return;
    }
    if (key != h->hotkey || GetTickCount64() >= nextKeyAttempt) {
        key = h->hotkey;
        registered = RegisterHotKey(window, hotkeyId, MOD_NOREPEAT|guide::shortcutModifiers(key), guide::shortcutKey(key)) != FALSE;
        if(registered)UnregisterHotKey(window,hotkeyId);
        h->status = registered ? keyboardState<0?3:1 : 2;
        nextKeyAttempt = GetTickCount64() + 1000;
    }
}
static LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wp, LPARAM lp) {
    auto* h = connection.header;
    // The reader owns this HWND. Let DefWindowProc apply its system class
    // cursor; forwarding this request to a different input queue left stale
    // wait cursors behind when the game did not replace them.
    if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
    if (message == WM_HOTKEY && h && front()) {
        if (wp == WPARAM(hotkeyId)) connection.toggle();
        else if (h->visible) {
            if(wp>=WPARAM(hotkeyId+6)&&wp<WPARAM(hotkeyId+13)){
                const int readingKeys[]={VK_NEXT,VK_PRIOR,VK_RIGHT,VK_LEFT,VK_HOME,VK_END,VK_SPACE};int input=readingKeys[wp-hotkeyId-6];
                if(localVisible())localState.navigate(input,connection);
                else if(connection.action(3)){if(input==VK_RIGHT)InterlockedIncrement(&h->step);else if(input==VK_LEFT)InterlockedDecrement(&h->step);else if(input==VK_HOME)bodyScroll=0;else if(input==VK_END)bodyScroll=maximumScroll;else if(input==VK_NEXT||input==VK_SPACE||input==VK_PRIOR){int direction=input==VK_PRIOR?-1:1;if(flowVisible()){flowState.panY-=direction*flowState.canvas.h*.85f;flowState.autoFit=false;}else bodyScroll=std::clamp(bodyScroll+direction*float(bodyBox.bottom-bodyBox.top)*.85f,0.f,maximumScroll);}}
            }
        }
        requestPaint(); return 0;
    }
    if (message == WM_TIMER) {
        if (!connection.live()) { DestroyWindow(window); return 0; }
        registerKeys(window); connection.refresh();
        InterlockedExchange(&h->mouseSample,guide::mouseSample(h->visible&&front()&&(GetAsyncKeyState(VK_LBUTTON)&0x8000),GetTickCount64()));
        if(!gameWindow||!IsWindow(gameWindow)){gameWindow=nullptr;EnumWindows(findWindow,0);}
        if(!IsWindowVisible(window)&&gameWindow){RECT game{};if(GetWindowRect(gameWindow,&game))SetWindowPos(window,nullptr,(game.left+game.right)/2,(game.top+game.bottom)/2,0,0,SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE);}
        h->dpi=LONG(GetDpiForWindow(window));
        if(h->visible)h->interactive=1;
        bool visible = h->visible && front() && GetTickCount64() - ULONGLONG(h->rendererPulse) > 1300;
        if (!visible) { ShowWindow(window, SW_HIDE); return 0; }
        if (interactive != bool(h->interactive)) {
            interactive = h->interactive != 0; LONG_PTR style = GetWindowLongPtrW(window, GWL_EXSTYLE);
            SetWindowLongPtrW(window, GWL_EXSTYLE, interactive ? style & ~WS_EX_TRANSPARENT : style | WS_EX_TRANSPARENT);
        }
        RECT current{};bool moved=gameWindow&&GetWindowRect(gameWindow,&current)&&!EqualRect(&current,&painted.target);
        if(!IsWindowVisible(window)||moved||painted.revision!=h->sequence||painted.route!=h->route||painted.step!=h->step||painted.graph!=h->graph||painted.selected!=h->selected||painted.light!=h->lightTheme||painted.dpi!=h->dpi)paint(window);
        SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW); return 0;
    }
    if (message == WM_MOUSEWHEEL && h && h->visible) {
        POINT point{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};ScreenToClient(window,&point);
        if(localVisible()&&localState.wheel({float(point.x),float(point.y)},float(GET_WHEEL_DELTA_WPARAM(wp))/WHEEL_DELTA,&connection)){paint(window);return 0;}
        if(flowVisible()&&flowState.wheel({float(point.x),float(point.y)},float(GET_WHEEL_DELTA_WPARAM(wp))/WHEEL_DELTA)){paint(window);return 0;}
        float delta=(GET_WHEEL_DELTA_WPARAM(wp)<0?130.f:-130.f)*currentScale;
        if(PtInRect(&sideBox,point))sideScroll=std::clamp(sideScroll+delta,0.f,sideMaximum);
        else if(PtInRect(&tocBox,point))tocScroll=std::clamp(tocScroll+delta,0.f,tocMaximum);
        else bodyScroll=std::clamp(bodyScroll+delta,0.f,maximumScroll);
        paint(window);return 0;
    }
    if(h&&h->visible&&(message==WM_LBUTTONDOWN||message==WM_MOUSEMOVE||message==WM_LBUTTONUP)) {
        if(message==WM_MOUSEMOVE&&!(wp&MK_LBUTTON)){
            localState.dragging=false;flowState.cancel();draggingBody=false;
            if(GetCapture()==window)ReleaseCapture();
        }
        POINT point{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
        guide::Point mouse{float(point.x),float(point.y)};
        if(localVisible()){
            if(message==WM_LBUTTONDOWN&&localState.down(mouse)){SetCapture(window);return 0;}
            if(message==WM_MOUSEMOVE&&localState.move(mouse)){paint(window);return 0;}
            if(message==WM_LBUTTONUP&&localState.dragging){localState.up(mouse,connection);ReleaseCapture();paint(window);return 0;}
        }
        if(flowVisible()) {
            if(message==WM_LBUTTONDOWN&&flowState.down(mouse)){SetCapture(window);return 0;}
            if(message==WM_MOUSEMOVE&&flowState.move(mouse)){paint(window);return 0;}
            if(message==WM_LBUTTONUP&&(flowState.dragging||flowState.outer.has(mouse))){flowState.up(mouse,connection.document);ReleaseCapture();readFlow();paint(window);return 0;}
        }
        const float scale=currentScale;
        const bool onTrack=PtInRect(&bodyBox,point)&&point.x>=bodyBox.right-8*scale;
        if(message==WM_LBUTTONDOWN&&onTrack&&maximumScroll>0){draggingBody=true;SetCapture(window);}
        if(draggingBody) {
            const float track=std::max(1.f,float(bodyBox.bottom-bodyBox.top));
            bodyScroll=std::clamp((point.y-bodyBox.top)/track,0.f,1.f)*maximumScroll;
            if(message==WM_LBUTTONUP){draggingBody=false;ReleaseCapture();}
            paint(window);return 0;
        }
    }
    if(message==WM_CAPTURECHANGED||message==WM_CANCELMODE){draggingBody=false;flowState.cancel();localState.dragging=false;}
    if(message==WM_LBUTTONUP&&h&&h->visible){
        POINT point{GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};
        if(localState.up({float(point.x),float(point.y)},connection)){if(h->visible)paint(window);return 0;}
        for(const auto& item:hits)if(PtInRect(&item.box,point)){
            switch(item.action){
            case 0:h->visible=h->interactive=0;ShowWindow(window,SW_HIDE);break;
            case 1:h->route=item.argument;h->step=0;h->graph=0;bodyScroll=0;break;
            case 2:if(h->graph!=item.argument){if(h->graph)savedOverviewScroll=bodyScroll;else savedReadingScroll=bodyScroll;h->graph=item.argument;bodyScroll=h->graph?savedOverviewScroll:savedReadingScroll;}break;
            case 3:preserveFirst=h->readingFirst;showOriginal=!showOriginal;break;
            case 4:h->step=item.argument;break;
            case 8:h->step=item.argument;h->graph=0;lastStep=-1;break;
            case 5:if(flowVisible()){flowState.panY-=item.argument*flowState.canvas.h*.75f;flowState.autoFit=false;}else bodyScroll=std::clamp(bodyScroll+item.argument*float(bodyBox.bottom-bodyBox.top)*.75f,0.f,maximumScroll);break;
            case 6:if(!connection.document.sources.empty()){h->argument=(h->selected+1)%LONG(connection.document.sources.size());MemoryBarrier();h->command=1;}break;
            case 7:if(!connection.document.routes.empty()){const LONG count=LONG(connection.document.routes.size());h->route=(h->route+item.argument+count)%count;h->step=0;h->graph=0;bodyScroll=0;}break;
            }break;
        }
        if(h->visible)paint(window);return 0;
    }
    if (message == WM_DESTROY) { for (int i = 0; i < 13; ++i) UnregisterHotKey(window, hotkeyId + i); PostQuitMessage(0); return 0; }
    return DefWindowProcW(window, message, wp, lp);
}
static WNDCLASSW overlayClass(HINSTANCE instance){
    WNDCLASSW cls{};cls.hInstance=instance;cls.lpfnWndProc=windowProc;
    cls.hCursor=LoadCursorW(nullptr,MAKEINTRESOURCEW(32512));cls.lpszClassName=L"TheOrder.GuideOverlay";return cls;
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    int argc = 0; LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc); if (!argv || argc < 5) return 2;
    DWORD pid = wcstoul(argv[2], nullptr, 10); LONG64 stamp = _wcstoi64(argv[3], nullptr, 10); std::wstring name = argv[4];
    if (wcscmp(argv[1], L"--inject") == 0) {
        if (!connection.open(name) || connection.header->pid != LONG(pid) || connection.header->created != stamp) { LocalFree(argv); return 14; }
        int code = argc == 6 ? inject(pid, stamp, argv[5]) : 3; connection.header->injectResult = code; LocalFree(argv); return code;
    }
    LocalFree(argv); targetPid = pid;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, pid);
    if (!process || guide::creation(process) != stamp || !connection.open(name) || connection.header->pid != LONG(pid) || connection.header->created != stamp) { if (process) CloseHandle(process); return 4; }
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    Gdiplus::GdiplusStartupInput input; ULONG_PTR token = 0; if (Gdiplus::GdiplusStartup(&token, &input, nullptr) != Gdiplus::Ok) return 5;
    auto cls=overlayClass(instance);RegisterClassW(&cls);
    overlayWindow = CreateWindowExW(WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
        cls.lpszClassName, L"The Order · 游戏内攻略", WS_POPUP, 0, 0, 480, 700, nullptr, nullptr, instance, nullptr);
    if (!overlayWindow) { Gdiplus::GdiplusShutdown(token); CloseHandle(process); return 6; }
    keyboardReady=CreateEventW(nullptr,TRUE,FALSE,nullptr);keyboardThread=keyboardReady?CreateThread(nullptr,0,keyboardLoop,nullptr,0,&keyboardThreadId):nullptr;
    if(keyboardThread)WaitForSingleObject(keyboardReady,2000);else InterlockedExchange(&keyboardState,-1);
    if(keyboardReady){CloseHandle(keyboardReady);keyboardReady=nullptr;}
    EnumWindows(findWindow, 0); SetTimer(overlayWindow, 1, 100, nullptr);
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        if (WaitForSingleObject(process, 0) == WAIT_OBJECT_0) { DestroyWindow(overlayWindow); break; }
        TranslateMessage(&message); DispatchMessageW(&message);
    }
    if(keyboardThread){PostThreadMessageW(keyboardThreadId,WM_QUIT,0,0);if(WaitForSingleObject(keyboardThread,5000)!=WAIT_OBJECT_0)return 8;CloseHandle(keyboardThread);keyboardThread=nullptr;}
    connection.close(); CloseHandle(process); Gdiplus::GdiplusShutdown(token); return 0;
}
