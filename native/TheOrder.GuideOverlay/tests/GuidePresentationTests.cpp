#include "../GuidePresentation.hpp"
#include "../GuideLocalReader.hpp"
#include "../GuideChat.hpp"
#include <cstdio>
#include <stdexcept>
#pragma comment(lib, "user32.lib")

static int checks=0;
static void require(bool ok,const char* label){++checks;if(!ok)throw std::runtime_error(label);}
struct Window {
    HWND handle;
    Window(const wchar_t* name,int x,int y,int width,int height,HWND owner=nullptr,DWORD style=WS_POPUP)
        :handle(CreateWindowExW(WS_EX_NOACTIVATE,name,L"The Order presentation test",style|WS_VISIBLE,x,y,width,height,owner,nullptr,GetModuleHandleW(nullptr),nullptr)){
        require(handle!=nullptr,"create fixture window");
    }
    ~Window(){DestroyWindow(handle);}
};
static void properties(HWND output,HWND source,RECT r){
    SetPropW(output,L"Magpie.SrcHWND",source);
    SetPropW(output,L"Magpie.DestLeft",reinterpret_cast<HANDLE>(INT_PTR(r.left)));
    SetPropW(output,L"Magpie.DestTop",reinterpret_cast<HANDLE>(INT_PTR(r.top)));
    SetPropW(output,L"Magpie.DestRight",reinterpret_cast<HANDLE>(INT_PTR(r.right)));
    SetPropW(output,L"Magpie.DestBottom",reinterpret_cast<HANDLE>(INT_PTR(r.bottom)));
}
struct LayoutPainter {
    std::vector<std::string> labels;
    void box(guide::flow::Rect,guide::flow::Color,guide::flow::Color,float,float=1){}
    void line(guide::Point,guide::Point,guide::flow::Color,float){}
    void text(const std::string& value,guide::flow::Rect,float,guide::flow::Color,bool,bool=false){labels.push_back(value);}
    void clip(guide::flow::Rect){}
    void unclip(){}
    void image(guide::flow::Rect,const guide::Reading&){}
    void polyline(const std::vector<guide::Point>&,guide::flow::Color,float){}
    void polygon(const std::vector<guide::Point>&,guide::flow::Color,guide::flow::Color,float=1){}
    void draft(const std::string&,guide::flow::Rect,float,guide::flow::Color,const guide::chat::Header&){}
    float paragraph(const std::string&,float,float,float,float size,guide::flow::Color,bool,bool){return size*1.5f;}
    float measureChatRun(std::string_view value,float size,int) { return float(value.size())*size*.5f; }
    void drawChatRun(std::string_view value,float,float,float,guide::flow::Color,int) { labels.emplace_back(value); }
};
static void controlsFit(float width,float height,float dpi) {
    using namespace guide;
    Header h{};h.sourceFlags=8;h.sequence=2;
    Document doc;doc.reading.present=true;doc.reading.files={"说明.pdf"};doc.reading.pages=12;
    auto layout=reading::layout(width,height,dpi);
    const float s=layout.scale;
    const float inset=layout.controlInset,right=layout.width-inset;
    const bool beside=reading::attributionBesideFlowModes(layout);
    auto heading=reading::header(layout,4,!beside);
    LayoutPainter painter;
    local::State local;
    local::sourceBar(painter,local,&h,{heading.sourceX,heading.sourceY,heading.sourceWidth,heading.rowHeight},4,s,false);
    require(local.hits.size()==4,"all four tabs available");
    for(const auto& hit:local.hits)
        require(hit.box.x>=inset-.01f&&hit.box.x+hit.box.w<=right+.01f
            &&hit.box.y+hit.box.h<=heading.ruleY,"tabs inside panel and above separator");
    const float top=heading.contentTop+reading::flowModeHeight(layout)+2*s;
    flow::State graph;
    flow::render(painter,graph,doc,{inset,top,right-inset,layout.contentBottom-top},s,false,2);
    require(graph.hits.size()==6,"route, zoom, fit, overview, filter stay available");
    for(size_t i=0;i<graph.hits.size();++i) {
        const auto& box=graph.hits[i].box;
        require(box.x>=inset-.01f&&box.x+box.w<=right+.01f&&box.y+box.h<=graph.canvas.y,"graph control fully accessible");
        for(size_t j=0;j<i;++j){
            const auto& other=graph.hits[j].box;
            require(box.x>=other.x+other.w-.01f||other.x>=box.x+box.w-.01f
                ||box.y>=other.y+other.h-.01f||other.y>=box.y+box.h-.01f,"graph controls do not overlap");
        }
    }
    require(graph.canvas.h>=layout.height*.43f,"graph gets usable space on short window");
    if(width>=540&&height>=350)require(graph.canvas.h>=layout.height*.70f,"graph occupies most of the panel");
    heading=reading::header(layout,4,false);
    const flow::Rect body{inset,heading.contentTop,right-inset,layout.contentBottom-heading.contentTop};
    h.channel=1;doc.reading.channel=1;
    doc.reading.progress=.5f;
    for(int i=0;i<50;++i)doc.reading.blocks.push_back({"text","示例正文",{}});
    local.hits.clear();
    local::render(painter,local,doc,&h,body,s,false);
    require(local.canvas.h>=layout.height*.60f,"document gets most of the panel");
    require(local.scrollY>0&&local.hits.size()==7,"saved reading position and one set of toolbar hits");
    local.zoom=1.2f;local.hits.clear();local::render(painter,local,doc,&h,body,s,false);
    require(local.zoom==1.2f,"repaint preserves document zoom");
    for(const auto& hit:local.hits)
        require(hit.box.x>=inset-.01f&&hit.box.x+hit.box.w<=right+.01f,"document tools fit");
    chat::Canvas canvas;
    chat::Header chatHeader{};
    chat::Connection chat;
    chat.h=&chatHeader;chat.state.models={"Gemini 4 Hydrogen","Gemini 4 Carbon"};
    chat.state.messages.push_back({"assistant","粗体 content","",{{0,6,chat::bold}}});
    chat::render(painter,canvas,chat,body,s,false);
    require(canvas.hits.size()==7,"all conversation controls available");
    require(canvas.rowLayouts.size()==1&&canvas.rowLayouts[0].runs[0].style==chat::bold,"chat uses bold spans");
    require(std::find(painter.labels.begin(),painter.labels.end(),"粗体")!=painter.labels.end(),"chat painter receives formatted CJK");
    require(heading.closeWidth>=38*s,"close label gets one horizontal row");
    require(canvas.body.h>=layout.height*.37f&&canvas.body.y+canvas.body.h<=canvas.input.y,"usable messages and composer");
    for(const auto& hit:canvas.hits)
        require(hit.box.x>=inset-.01f&&hit.box.x+hit.box.w<=right+.01f,"chat tools fit");
    require(canvas.click({canvas.hits[0].box.x+2,canvas.hits[0].box.y+2},chat)&&canvas.modelsOpen,"model menu opens");
    chat::render(painter,canvas,chat,body,s,false);
    require(canvas.hits.size()==9,"both model options remain visible");
}
static void namedRoutesAndViewLifetime(){
    using namespace guide;
    Document doc;
    doc.routes={{"common","共通线","START","分歧",{}},{"nagisa","渚线 · 选择 A","分歧","渚 END",{}},
        {"tomoyo","智代线 · 选择 B","分歧","智代 END",{}}};
    auto& f=doc.flow;
    f.width=720;f.height=600;
    f.nodes={{"start","START","",360,45,100,40},{"save","分歧","",360,180,100,40},
        {"ending","渚 END","heroine",130,520,110,44},{"ending","智代 END","heroine",590,520,110,44}};
    f.edges={{0,1,0,0,0,0,0,false,{{360,65},{360,95},{360,125},{360,160}},{360,114}},
        {1,2,1,0,0,0,1,false,{{360,200},{360,300},{130,420},{130,498}},{245,350}},
        {1,3,2,0,0,0,2,false,{{360,200},{360,300},{590,420},{590,498}},{475,350}}};
    flow::State graph;
    LayoutPainter painter;
    auto render=[&](LONG revision){painter.labels.clear();flow::render(painter,graph,doc,{0,0,530,370},1,false,revision);};
    auto has=[&](const char* label){return std::find(painter.labels.begin(),painter.labels.end(),label)!=painter.labels.end();};
    auto choose=[&](int action,int index){
        const auto hit=std::find_if(graph.hits.begin(),graph.hits.end(),[&](const auto& h){return h.action==action&&h.index==index;});
        require(hit!=graph.hits.end(),"route action visible");
        require(graph.up({hit->box.x+hit->box.w/2,hit->box.y+hit->box.h/2},doc),"route action handles click");
    };
    render(2);
    require(graph.section==-1&&graph.activeEdges.size()==3&&has("全部路线"),"short-window first frame includes all routes");
    for(int node:graph.activeNodes){
        const auto& n=graph.positions[size_t(node)];const auto a=graph.screen({n.x,n.y});
        require(graph.canvas.has(a),"full map initially fits the canvas");
    }
    choose(5,0);render(2);
    require(has("共通线")&&has("渚线")&&has("智代线"),"menu names are real route titles");
    choose(9,2);render(2);
    require(graph.section==2&&graph.activeEdges.size()==2&&has("智代线")&&!has("当前路线"),"named branch preserves its common path");
    const Point center{graph.canvas.x+graph.canvas.w/2,graph.canvas.y+graph.canvas.h/2};
    graph.zoomAt(1.25f,center);const float zoom=graph.zoom,pan=graph.panX;
    Header h{};Connection session;session.header=&h;
    session.toggle();require(h.visible,"guide opened");
    h.lastToggle=0;session.toggle();graph.cancel();require(!h.visible,"guide hidden");
    h.lastToggle=0;session.toggle();render(2);
    require(h.visible&&graph.section==2&&graph.zoom==zoom&&graph.panX==pan,"reopening preserves route and viewport");
    flow::State next;
    flow::render(painter,next,doc,{0,0,530,370},1,false,2);
    require(next.section==-1&&next.activeEdges.size()==3&&next.fitWhole,"fresh game begins with the full map");
    choose(5,0);render(2);choose(9,-1);render(2);
    require(graph.section==-1&&graph.activeEdges.size()==3,"user can return to all routes");
}
int main(){
    using namespace guide::presentation;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    WNDCLASSW cls{};cls.hInstance=GetModuleHandleW(nullptr);cls.lpfnWndProc=DefWindowProcW;
    cls.lpszClassName=L"TheOrder.PresentationTest";RegisterClassW(&cls);
    cls.lpszClassName=magpieClass;RegisterClassW(&cls);
    try{
        Window game(L"TheOrder.PresentationTest",-9000,-9000,640,480);
        Window output(magpieClass,-8000,-6000,1600,900);
        {
            Window popup(L"TheOrder.PresentationTest",-8900,-8900,430,300,game.handle);
            Window child(L"TheOrder.PresentationTest",0,0,300,250,game.handle,WS_CHILD);
            require(gameRoot(game.handle),"game top-level window can anchor reader");
            require(!gameRoot(popup.handle)&&!onSurface(game.handle,popup.handle),"owned popup cannot become the reader surface");
            require(!gameRoot(child.handle)&&onSurface(game.handle,child.handle),"child of the game stays within its surface");
            require(!onSurface(game.handle,output.handle),"same process alone does not grant foreground ownership");
        }
        const DWORD pid=GetCurrentProcessId();Surface surface{};RECT frame{};
        require(clientBounds(output.handle,frame),"physical output rectangle");
        require(!readMagpie(output.handle,pid,surface),"unbound output ignored");
        properties(output.handle,game.handle,frame);
        require(readMagpie(output.handle,pid,surface),"bound output detected");
        require(surface.source==game.handle&&surface.window==output.handle,"source identity retained");
        require(EqualRect(&surface.bounds,&frame)!=FALSE,"negative screen coordinates retained");
        require(surface.dpi==GetDpiForWindow(output.handle),"output DPI used");
        require(!surface.screenCapture,"window capture permits reader screenshots");
        require(SetWindowDisplayAffinity(output.handle,WDA_EXCLUDEFROMCAPTURE)!=FALSE,"screen capture fixture affinity");
        require(readMagpie(output.handle,pid,surface)&&surface.screenCapture,"desktop duplication exclusion detected");
        require(SetWindowDisplayAffinity(output.handle,WDA_NONE)!=FALSE,"window capture fixture affinity restored");
        require(!readMagpie(output.handle,pid+1,surface),"other game rejected");
        require(!readMagpie(output.handle,0,surface),"unbound PID rejected");
        RECT letterbox=frame;InflateRect(&letterbox,-100,-50);properties(output.handle,game.handle,letterbox);
        require(readMagpie(output.handle,pid,surface)&&EqualRect(&surface.bounds,&letterbox),"black bars excluded");
        RECT overscan=frame;InflateRect(&overscan,200,200);properties(output.handle,game.handle,overscan);
        require(readMagpie(output.handle,pid,surface)&&EqualRect(&surface.bounds,&frame),"overscan clipped to output");
        RECT invalid=frame;invalid.right=invalid.left;properties(output.handle,game.handle,invalid);
        require(!readMagpie(output.handle,pid,surface),"zero width rejected");
        invalid=frame;OffsetRect(&invalid,3000,0);properties(output.handle,game.handle,invalid);
        require(!readMagpie(output.handle,pid,surface),"nonoverlapping props rejected");
        invalid=frame;invalid.right=invalid.left+200;properties(output.handle,game.handle,invalid);
        require(!readMagpie(output.handle,pid,surface),"incomplete small output rejected");
        properties(output.handle,game.handle,frame);
        properties(game.handle,game.handle,frame);
        require(!readMagpie(game.handle,pid,surface),"ordinary window with properties rejected");
        require(resolve(pid,game.handle).window==output.handle,"enumeration resolves bound game");
        require(resolve(pid,game.handle,output.handle).window==output.handle,"cached output revalidated");
        ShowWindow(output.handle,SW_HIDE);
        require(!resolve(pid,game.handle,output.handle).window,"hidden output falls back");
        ShowWindow(output.handle,SW_SHOWNOACTIVATE);
        ShowWindow(game.handle,SW_MINIMIZE);
        require(!readMagpie(output.handle,pid,surface),"minimized source falls back");
        ShowWindow(game.handle,SW_SHOWNOACTIVATE);ShowWindow(game.handle,SW_RESTORE);
        require(readMagpie(output.handle,pid,surface),"restored source detected");
        {
            Window second(magpieClass,-6000,-6000,1200,800);
            Window secondGame(L"TheOrder.PresentationTest",-6000,-9000,640,480);
            RECT bounds{};clientBounds(second.handle,bounds);properties(second.handle,secondGame.handle,bounds);
            require(!resolve(pid,nullptr).window,"ambiguous source not guessed");
            require(resolve(pid,game.handle).window==output.handle,"preferred source resolves ambiguity");
            require(resolve(pid,secondGame.handle).window==second.handle,"second source is independent");
        }
        RemovePropW(output.handle,L"Magpie.SrcHWND");
        require(!resolve(pid,game.handle,output.handle).window,"stopped scaling invalidates cached binding");
        guide::Header h{};h.presentationPulse=2000;
        using guide::CompanionSurface;
        require(guide::companionSurface(&h,2000)==CompanionSurface::Scaled,"scaled lease immediately valid");
        require(guide::companionSurface(&h,2999)==CompanionSurface::Scaled,"fresh companion lease valid");
        require(guide::companionSurface(&h,3000)==CompanionSurface::None,"stalled companion releases renderer");
        require(guide::companionSurface(&h,1999)==CompanionSurface::None,"future lease rejected");
        h.presentationPulse=-2000;
        require(guide::companionSurface(&h,2000)==CompanionSurface::Window,"ordinary window owns its desktop input");
        require(guide::companionSurface(&h,3000)==CompanionSurface::None,"expired ordinary lease releases renderer");
        h.rendererPulse=h.windowedPulse=2000;
        require(guide::rendererActive(&h,2100)&&guide::windowedPresentation(&h,2100),"fresh composited renderer eligible");
        h.windowedPulse=0;
        require(guide::rendererActive(&h,2100)&&!guide::windowedPresentation(&h,2100),"exclusive renderer keeps its surface");
        require(!guide::rendererActive(&h,3300),"stopped renderer permits fallback");
        h.presentationPulse=0;
        require(guide::companionSurface(&h,2000)==CompanionSurface::None,"ordinary renderer remains available");
        {
            Window captioned(L"TheOrder.PresentationTest",40,40,640,480);
            SetWindowLongPtrW(captioned.handle,GWL_STYLE,WS_OVERLAPPEDWINDOW|WS_VISIBLE);
            SetWindowPos(captioned.handle,nullptr,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_FRAMECHANGED);
            const auto client=windowBounds(captioned.handle);
            RECT outer{};GetWindowRect(captioned.handle,&outer);
            require(area(client)&&client.top>outer.top,"ordinary overlay excludes window titlebar");
            ShowWindow(captioned.handle,SW_HIDE);
            require(!area(windowBounds(captioned.handle)),"hidden ordinary game does not bind overlay");
        }
        controlsFit(1840,1380,192);
        controlsFit(900,590,192);
        controlsFit(640,420,144);
        controlsFit(540,350,192);
        controlsFit(320,240,192);
        namedRoutesAndViewLifetime();
        const auto sourceLayout=guide::reading::layout(640,480,192);
        const auto scaled=guide::reading::layout(2560,1440,192);
        require(scaled.width>sourceLayout.width*2&&scaled.height>sourceLayout.height*2,"reader uses output space");
        require(scaled.scale==2&&scaled.width<=2560&&scaled.height<=1440,"native density fits output");
        const RECT desktop{0,0,2880,1800},partlyOutside{-535,2,1335,1280},expected{0,2,1335,1280};
        const auto cropped=readingBounds(partlyOutside,desktop);
        require(EqualRect(&cropped,&expected),"windowed reader stays on visible screen");
        const RECT secondMonitor{-1920,0,0,1080},negativeOutput{-1800,20,-200,1000};
        const auto onSecond=readingBounds(negativeOutput,secondMonitor);
        require(EqualRect(&onSecond,&negativeOutput),"real negative monitor coordinates preserved");
        const auto invisible=readingBounds(negativeOutput,desktop);
        require(!area(invisible),"entirely offscreen output has no stale reader");
        const RECT tooNarrow{-900,0,200,900};
        require(!area(readingBounds(tooNarrow,desktop)),"unreadable offscreen sliver rejected");
        const auto visibleLayout=guide::reading::layout(float(cropped.right-cropped.left),float(cropped.bottom-cropped.top),192);
        require(cropped.left+visibleLayout.x>=desktop.left&&cropped.left+visibleLayout.x+visibleLayout.width<=desktop.right,"reader controls fit desktop after clipping");
        std::printf("{\"passed\":%d,\"failed\":0,\"protocolVersion\":%u,\"headerBytes\":%zu}\n",checks,guide::version,sizeof(h));
        return 0;
    }catch(const std::exception& error){std::fprintf(stderr,"FAILED after %d checks: %s\n",checks,error.what());return 1;}
}
