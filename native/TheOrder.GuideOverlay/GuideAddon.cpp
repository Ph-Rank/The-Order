#include "GuideFlowCanvas.hpp"
#include "GuideLocalReader.hpp"
#define ImTextureID ImU64
#include <imgui.h>
#include <reshade.hpp>

static guide::Connection connection;
static bool attempted=false,keyDown=false,showOriginal=false;
static bool readingKeysDown[7]{};
static int selectedSource=-1,previousRoute=-1,previousStep=-1;
static ImVec4 ink(.95f,.96f,.99f,1),muted(.66f,.72f,.82f,1),accent(.72f,.81f,1,1);
static bool light=false;
static bool compactWindow=false;
static bool jumpRequested=false;
static int pageMotion=0;
static guide::local::State localState;
static ULONGLONG pointerDownAt=0;
struct LocalTexture {
    reshade::api::effect_runtime* runtime=nullptr;reshade::api::resource resource{};reshade::api::resource_view view{};LONG revision=-1;
    void clear(){if(runtime&&resource.handle){runtime->get_command_queue()->wait_idle();auto* device=runtime->get_device();if(view.handle)device->destroy_resource_view(view);device->destroy_resource(resource);}resource={};view={};revision=-1;runtime=nullptr;}
    void update(reshade::api::effect_runtime* current,const guide::Reading& page,LONG value){
        if(runtime==current&&revision==value)return;clear();runtime=current;revision=value;if(page.rgba.empty())return;
        using namespace reshade::api;subresource_data data{const_cast<BYTE*>(page.rgba.data()),uint32_t(page.width*4),uint32_t(page.width*page.height*4)};
        auto* device=current->get_device();if(!device->create_resource(resource_desc(uint32_t(page.width),uint32_t(page.height),1,1,format::r8g8b8a8_unorm,1,memory_heap::default_,resource_usage::shader_resource),&data,resource_usage::shader_resource,&resource))return;
        if(!device->create_resource_view(resource,resource_usage::shader_resource,resource_view_desc(format::r8g8b8a8_unorm),&view)){device->destroy_resource(resource);resource={};}
    }
};
static LocalTexture localTexture;
static void releaseLocalTexture(reshade::api::effect_runtime* runtime){if(localTexture.runtime==runtime)localTexture.clear();}
static ImFont* face(int role){
    auto* atlas=ImGui::GetIO().Fonts;
    if(role==2&&atlas->Fonts.Size>2&&atlas->Fonts[2]->IsGlyphInFont(0x4e2d))return atlas->Fonts[2];
    if(role==1&&atlas->Fonts.Size>1)return atlas->Fonts[1];
    return nullptr;
}
static bool foreground(){DWORD pid=0;GetWindowThreadProcessId(GetForegroundWindow(),&pid);return pid==GetCurrentProcessId();}
static bool plainKey(int key){return guide::activeModifiers()==0&&(GetAsyncKeyState(key)&0x8000);}
static bool edge(bool value,bool& old){bool changed=value&&!old;old=value;return changed;}
static float measure(const std::string& value,float width,float font,float scale,int role=0){
    if(role==1&&std::any_of(value.begin(),value.end(),[](unsigned char c){return c>=128;}))role=0;
    ImGui::PushFont(face(role),font*scale);float h=ImGui::CalcTextSize(value.c_str(),nullptr,false,std::max(20.f,width)).y;ImGui::PopFont();return h;
}
static void text(const std::string& value,float font,float scale,ImVec4 color=ink,float wrap=0,int role=0){
    if(role==1&&std::any_of(value.begin(),value.end(),[](unsigned char c){return c>=128;}))role=0;
    ImGui::PushFont(face(role),font*scale);ImGui::PushStyleColor(ImGuiCol_Text,color);ImGui::PushTextWrapPos(wrap);
    ImGui::TextUnformatted(value.c_str(),value.c_str()+value.size());ImGui::PopTextWrapPos();ImGui::PopStyleColor();ImGui::PopFont();
}
static ImVec4 tone(const guide::Step& step){
    if(light)return step.kind=="save"?ImVec4(.58f,.34f,.06f,1):step.kind=="load"?ImVec4(.43f,.27f,.69f,1):step.achievement?ImVec4(.13f,.45f,.31f,1):accent;
    return step.kind=="save"?ImVec4(1,.76f,.44f,1):step.kind=="load"?ImVec4(.78f,.71f,1,1):step.achievement?ImVec4(.56f,.87f,.72f,1):accent;
}
struct Heights {
    LONG revision=-1;
    int route=-1;
    bool original=false;
    bool compact=false;
    float width=0,scale=0;
    std::vector<float> offsets;
};
static Heights heights;
static int readingFirst=0,chapterFirst=0;
static void prepareHeights(const guide::Route& route,int routeIndex,float width,float scale) {
    if(heights.revision==connection.header->sequence&&heights.route==routeIndex&&heights.original==showOriginal&&heights.compact==compactWindow&&heights.width==width&&heights.scale==scale)return;
    heights={connection.header->sequence,routeIndex,showOriginal,compactWindow,width,scale,{0}};
    for(const auto& step:route.steps) {
        const float h=guide::reading::rowHeight(step,width,scale,showOriginal,
            [&](const std::string& value,float wrap,float font,int role){return measure(value,wrap,font,scale,role);},guide::reading::rowInset(guide::reading::stepCount(route),compactWindow),compactWindow);
        heights.offsets.push_back(heights.offsets.back()+h);
    }
}
static void stepRow(const guide::Step& step,int index,int number,int selected,float width,float height,float scale,float rowInset) {
    const auto p=ImGui::GetCursorScreenPos();
    auto* list=ImGui::GetWindowDrawList();
    const auto color=tone(step);
    const auto type=guide::reading::style(compactWindow);
    const float inset=rowInset*scale;
    const float available=width-inset-12*scale;
    ImGui::PushID(index);
    if(!step.heading) {
        if(index==selected) {
            list->AddRectFilled(p,ImVec2(p.x+width,p.y+height-3*scale),
                light?IM_COL32(97,116,142,17):IM_COL32(180,195,220,13),3*scale);
            list->AddRectFilled(p,ImVec2(p.x+2*scale,p.y+height-3*scale),
                ImGui::ColorConvertFloat4ToU32(accent),scale);
        }
        ImGui::SetCursorScreenPos(ImVec2(p.x+(compactWindow?5.f:8.f)*scale,p.y+6*scale));
        text((number<10?"0":"")+std::to_string(number),type.label,scale,muted,0,1);
        ImGui::SetCursorScreenPos(ImVec2(p.x+(rowInset-(compactWindow?30.f:35.f))*scale,p.y+6*scale));
        text(guide::kind(step.kind),type.label,scale,color);
    } else {
        ImGui::SetCursorScreenPos(ImVec2(p.x+5*scale,p.y+(compactWindow?11.f:14.f)*scale));
        text("阶段",type.label,scale,muted);
        list->AddLine(ImVec2(p.x+inset,p.y+height-5*scale),ImVec2(p.x+width-12*scale,p.y+height-5*scale),
            light?IM_COL32(95,100,111,36):IM_COL32(185,191,202,32),scale);
    }
    float y=p.y+(step.heading?(compactWindow?10.f:13.f):(compactWindow?4.f:5.f))*scale;
    auto line=[&](const std::string& value,float font,ImVec4 inkColor,int role=0) {
        ImGui::SetCursorScreenPos(ImVec2(p.x+inset,y));
        text(value,font,scale,inkColor,ImGui::GetCursorPosX()+available,role);
        y+=measure(value,available,font,scale,role);
    };
    line(guide::reading::primary(step),step.heading?type.heading:type.body,step.heading?accent:ink,step.heading?2:0);
    if(showOriginal&&guide::reading::translated(step)) {
        y+=(compactWindow?4.f:6.f)*scale;line(step.original,type.original,muted);
    }
    if(step.achievement) {y+=(compactWindow?4.f:6.f)*scale;line("成就相关",type.label,color);}
    if(!step.alternatives.empty()) {
        y+=(compactWindow?5.f:7.f)*scale;line("其他选项",type.label,muted);y+=(compactWindow?3.f:5.f)*scale;
        for(const auto& alt:step.alternatives) {
            line("· "+guide::reading::primary(alt),type.alternative,muted);y+=(compactWindow?3.f:4.f)*scale;
            if(showOriginal&&guide::reading::translated(alt)) {line(alt.original,type.alternativeOriginal,muted);y+=(compactWindow?3.f:4.f)*scale;}
        }
    }
    if(y>p.y+height)++connection.header->overflowRows;
    ImGui::SetCursorScreenPos(p);
    if(ImGui::InvisibleButton("step",ImVec2(width,height-2*scale)))connection.header->step=index;
    ImGui::PopID();
}
static void reading(const guide::Route& route,int routeIndex,int selected,float scale,bool restore) {
    if(ImGui::BeginChild("steps",ImVec2(0,0),ImGuiChildFlags_None)) {
        const float width=ImGui::GetContentRegionAvail().x-8*scale;
        const bool changedOriginal=heights.route==routeIndex&&heights.original!=showOriginal;
        const int firstBefore=readingFirst;
        prepareHeights(route,routeIndex,width,scale);
        const float y=ImGui::GetScrollY(),viewport=ImGui::GetWindowHeight();
        auto* h=connection.header;
        h->scrollY=LONG(y);h->visibleSteps=0;h->overflowRows=0;
        const auto type=guide::reading::style(compactWindow);h->rowPitch=LONG((type.body+type.padding+type.gap)*scale);
        if(!route.steps.empty()&&(restore||previousStep!=selected)) {
            float target=heights.offsets[selected];
            if(restore||target<y||heights.offsets[selected+1]>y+viewport)ImGui::SetScrollY(target);
        }
        const auto& offsets=heights.offsets;
        if(changedOriginal&&!route.steps.empty())ImGui::SetScrollY(offsets[std::clamp(firstBefore,0,int(route.steps.size())-1)]);
        if(pageMotion){ImGui::SetScrollY(std::max(0.f,y+pageMotion*viewport*.8f));pageMotion=0;}
        readingFirst=std::clamp(int(std::upper_bound(offsets.begin(),offsets.end(),y+4*scale)-offsets.begin())-1,
            0,std::max(0,int(route.steps.size())-1));
        chapterFirst=0;
        for(int i=0;i<=readingFirst&&i<int(route.steps.size());++i)if(guide::reading::anchor(route.steps[i]))chapterFirst=i;
        h->readingFirst=readingFirst;h->visibleStage=chapterFirst;
        int first=std::max(0,int(std::upper_bound(offsets.begin(),offsets.end(),std::max(0.f,y-80*scale))-offsets.begin())-1);
        for(int i=first;i<int(route.steps.size())&&offsets[i]<y+viewport+80*scale;++i) {
            if(offsets[i+1]>y&&offsets[i]<y+viewport)++h->visibleSteps;
            ImGui::SetCursorPosY(offsets[i]+4*scale);
            stepRow(route.steps[i],i,guide::reading::stepNumber(route,i),selected,width,offsets[i+1]-offsets[i],scale,guide::reading::rowInset(guide::reading::stepCount(route),compactWindow));
        }
        ImGui::SetCursorPosY(offsets.back()+14*scale);
        text("本段结束  ·  "+route.to,13,scale,muted);
        ImGui::Dummy(ImVec2(0,18*scale));
    }
    ImGui::EndChild();
}
static void routeList(const guide::Document& document,int selected,float width,float scale) {
    text("路线目录",13,scale,muted);
    ImGui::Dummy(ImVec2(0,8*scale));
    for(int i=0;i<int(document.routes.size());++i) {
        ImGui::PushID(i);
        const auto& route=document.routes[i];
        const auto p=ImGui::GetCursorScreenPos();
        const float wrap=width-42*scale;
        const std::string meta=route.to+" · "+std::to_string(guide::reading::stepCount(route))+"步";
        const float height=measure(guide::reading::routeName(route),wrap,17,scale,2)+measure(meta,wrap,12,scale)+20*scale;
        if(i!=selected&&ImGui::IsMouseHoveringRect(p,ImVec2(p.x+width,p.y+height)))
            ImGui::GetWindowDrawList()->AddRectFilled(p,ImVec2(p.x+width,p.y+height),
                light?IM_COL32(105,123,150,10):IM_COL32(180,195,220,8),3*scale);
        if(i==selected) {
            ImGui::GetWindowDrawList()->AddRectFilled(p,ImVec2(p.x+width,p.y+height),
                light?IM_COL32(105,123,150,19):IM_COL32(180,195,220,15),3*scale);
            ImGui::GetWindowDrawList()->AddRectFilled(p,ImVec2(p.x+2*scale,p.y+height),
                ImGui::ColorConvertFloat4ToU32(accent),scale);
        }
        ImGui::SetCursorScreenPos(ImVec2(p.x+10*scale,p.y+10*scale));
        text((i+1<10?"0":"")+std::to_string(i+1),11,scale,muted,0,1);
        ImGui::SetCursorScreenPos(ImVec2(p.x+34*scale,p.y+8*scale));
        text(guide::reading::routeName(route),17,scale,i==selected?accent:ink,ImGui::GetCursorPosX()+wrap,2);
        ImGui::SetCursorScreenPos(ImVec2(p.x+34*scale,p.y+12*scale+measure(guide::reading::routeName(route),wrap,17,scale,2)));
        text(meta,12,scale,muted,ImGui::GetCursorPosX()+wrap);
        ImGui::SetCursorScreenPos(p);
        if(ImGui::InvisibleButton("route",ImVec2(width,height))) {
            connection.header->route=i;connection.header->step=0;connection.header->graph=0;jumpRequested=true;
        }
        ImGui::Dummy(ImVec2(0,4*scale));
        ImGui::PopID();
    }
}
static bool tab(const char* label,bool active,float width,float scale) {
    const auto p=ImGui::GetCursorScreenPos();
    const bool click=ImGui::InvisibleButton(label,ImVec2(width,26*scale));
    const bool hover=ImGui::IsItemHovered();
    const float font=13.f;ImGui::PushFont(nullptr,font*scale);const float labelWidth=ImGui::CalcTextSize(label).x;ImGui::PopFont();
    const float labelX=p.x+(width-labelWidth)/2;
    if(active)ImGui::GetWindowDrawList()->AddLine(ImVec2(labelX,p.y+22*scale),ImVec2(labelX+labelWidth,p.y+22*scale),
        ImGui::ColorConvertFloat4ToU32(accent),2*scale);
    ImGui::SetCursorScreenPos(ImVec2(labelX,p.y+4*scale));
    text(label,font,scale,active||hover?ink:muted);
    ImGui::SetCursorScreenPos(ImVec2(p.x+width,p.y));
    return click;
}
static float routeHeader(const guide::Route& route,int index,int count,float width,float scale) {
    const auto p=ImGui::GetCursorScreenPos();
    if(compactWindow){
        text(guide::reading::routeName(route)+" · "+std::to_string(index+1)+"/"+std::to_string(count),17,scale,ink,ImGui::GetCursorPosX()+width,2);
        text(route.from+" → "+route.to,11,scale,muted);
        const auto split=route.title.find(" · ");if(split!=std::string::npos)text(route.title.substr(split+4),12,scale,muted);
        return ImGui::GetCursorScreenPos().y-p.y;
    }
    text("路线 "+std::to_string(index+1)+" / "+std::to_string(count),12,scale,muted);
    text(guide::reading::routeName(route),23,scale,ink,0,2);
    text(route.from+"  →  "+route.to,13,scale,muted);
    const auto separator=route.title.find(" · ");
    if(separator!=std::string::npos)text("说明："+route.title.substr(separator+4),13,scale,muted);
    const float y=ImGui::GetCursorScreenPos().y;
    (void)width;
    return y-p.y;
}
static void indexList(const guide::Route& route,float width,float scale) {
    text("本段定位",13,scale,muted);
    ImGui::Dummy(ImVec2(0,6*scale));
    bool any=false;
    for(int i=0;i<int(route.steps.size());++i) {
        const auto& step=route.steps[i];if(!guide::reading::anchor(step))continue;
        any=true;ImGui::PushID(i);
        const auto p=ImGui::GetCursorScreenPos();
        const float height=measure(guide::reading::primary(step),width-16*scale,14,scale)+15*scale;
        if(ImGui::IsMouseHoveringRect(p,ImVec2(p.x+width,p.y+height)))
            ImGui::GetWindowDrawList()->AddRectFilled(p,ImVec2(p.x+width,p.y+height),
                light?IM_COL32(105,123,150,10):IM_COL32(180,195,220,8),3*scale);
        if(i==chapterFirst)ImGui::GetWindowDrawList()->AddLine(p,ImVec2(p.x,p.y+height-3*scale),ImGui::ColorConvertFloat4ToU32(accent),2*scale);
        ImGui::SetCursorScreenPos(ImVec2(p.x+10*scale,p.y+5*scale));
        text(guide::reading::primary(step),14,scale,i==chapterFirst?ink:muted,ImGui::GetCursorPosX()+width-16*scale);
        ImGui::SetCursorScreenPos(p);
        if(ImGui::InvisibleButton("anchor",ImVec2(width,height))) {connection.header->step=i;connection.header->graph=0;jumpRequested=true;}
        ImGui::PopID();
    }
    if(!any)text("本段没有独立日期或存读档节点。",13,scale,muted);
    ImGui::Dummy(ImVec2(0,16*scale));
    text("阅读位置",12,scale,muted);
    text(guide::reading::position(route,readingFirst),14,scale,ink,0,1);
}
static void overview(const guide::Document& document,float scale) {
    if(ImGui::BeginChild("overview",ImVec2(0,0),ImGuiChildFlags_None)) {
        if(pageMotion){ImGui::SetScrollY(std::max(0.f,ImGui::GetScrollY()+pageMotion*ImGui::GetWindowHeight()*.8f));pageMotion=0;}
        for(int i=0;i<int(document.routes.size());++i) {
            const auto& route=document.routes[i];ImGui::PushID(i);
            const float width=ImGui::GetContentRegionAvail().x-8*scale;
            const auto p=ImGui::GetCursorScreenPos();
            const auto meta=route.from+" → "+route.to+(compactWindow?" · "+std::to_string(guide::reading::stepCount(route))+"步":"");
            const float height=measure(route.title,width-24*scale,compactWindow?16.f:19.f,scale,2)+measure(meta,width-24*scale,compactWindow?11.f:13.f,scale)+(compactWindow?12.f:43.f)*scale;
            text(route.title,compactWindow?16.f:19.f,scale,ink,ImGui::GetCursorPosX()+width-24*scale,2);
            ImGui::SetCursorScreenPos(ImVec2(p.x,p.y+measure(route.title,width-24*scale,compactWindow?16.f:19.f,scale,2)+(compactWindow?3.f:8.f)*scale));
            text(meta,compactWindow?11.f:13.f,scale,muted);
            if(!compactWindow)text(std::to_string(guide::reading::stepCount(route))+" 个步骤 · 点击阅读",12,scale,muted);
            ImGui::GetWindowDrawList()->AddLine(ImVec2(p.x,p.y+height-5*scale),ImVec2(p.x+width,p.y+height-5*scale),
                light?IM_COL32(95,100,111,32):IM_COL32(185,191,202,30),scale);
            ImGui::SetCursorScreenPos(p);
            if(ImGui::InvisibleButton("overviewRoute",ImVec2(width,height))) {
                connection.header->route=i;connection.header->step=0;connection.header->graph=0;jumpRequested=true;
            }
            ImGui::PopID();
        }
    }
    ImGui::EndChild();
}
static guide::flow::State flowState;
struct FlowImGuiPainter {
    ImDrawList* list;
    static ImU32 color(guide::flow::Color c){return IM_COL32(c.r,c.g,c.b,c.a);}
    void box(guide::flow::Rect r,guide::flow::Color fill,guide::flow::Color stroke,float radius,float thickness=1){list->AddRectFilled({r.x,r.y},{r.x+r.w,r.y+r.h},color(fill),radius);list->AddRect({r.x,r.y},{r.x+r.w,r.y+r.h},color(stroke),radius,0,thickness);}
    void line(guide::Point a,guide::Point b,guide::flow::Color c,float width){list->AddLine({a.x,a.y},{b.x,b.y},color(c),width);}
    void polyline(const std::vector<guide::Point>& points,guide::flow::Color c,float width){if(points.size()<2)return;std::vector<ImVec2> vertices;vertices.reserve(points.size());for(auto p:points)vertices.emplace_back(p.x,p.y);list->AddPolyline(vertices.data(),int(vertices.size()),color(c),0,width);}
    void polygon(const std::vector<guide::Point>& points,guide::flow::Color fill,guide::flow::Color stroke,float thickness=1){std::vector<ImVec2> vertices;for(auto p:points)vertices.emplace_back(p.x,p.y);list->AddConvexPolyFilled(vertices.data(),int(vertices.size()),color(fill));for(size_t i=0;i<points.size();++i)line(points[i],points[(i+1)%points.size()],stroke,thickness);}
    void text(const std::string& value,guide::flow::Rect r,float size,guide::flow::Color c,bool center,bool strong=false){
        if(r.w<4||r.h<4)return;ImGui::PushFont(nullptr,size);std::string label=value;
        if(ImGui::CalcTextSize(label.c_str()).x>r.w-8){std::vector<size_t> cuts{0};for(size_t i=1;i<=label.size();++i)if(i==label.size()||(static_cast<unsigned char>(label[i])&0xc0)!=0x80)cuts.push_back(i);size_t low=0,high=cuts.size()-1;while(low<high){size_t mid=(low+high+1)/2;auto part=label.substr(0,cuts[mid])+"…";if(ImGui::CalcTextSize(part.c_str()).x<=r.w-8)low=mid;else high=mid-1;}label=label.substr(0,cuts[low])+"…";}
        auto measured=ImGui::CalcTextSize(label.c_str());list->PushClipRect({r.x,r.y},{r.x+r.w,r.y+r.h},true);
        list->AddText(ImGui::GetFont(),size,{center?r.x+(r.w-measured.x)/2:r.x+4,r.y+(r.h-measured.y)/2},color(c),label.c_str());if(strong)list->AddText(ImGui::GetFont(),size,{(center?r.x+(r.w-measured.x)/2:r.x+4)+std::max(.5f,size*.035f),r.y+(r.h-measured.y)/2},color(c),label.c_str());list->PopClipRect();ImGui::PopFont();
    }
    void clip(guide::flow::Rect r){list->PushClipRect({r.x,r.y},{r.x+r.w,r.y+r.h},true);}
    void unclip(){list->PopClipRect();}
    float paragraph(const std::string& value,float x,float y,float width,float size,guide::flow::Color c,bool strong,bool draw){
        ImGui::PushFont(nullptr,size);auto measured=ImGui::CalcTextSize(value.c_str(),nullptr,false,width);float height=std::max(size*1.5f,measured.y);
        if(draw){list->AddText(ImGui::GetFont(),size,{x,y},color(c),value.c_str(),nullptr,width);if(strong)list->AddText(ImGui::GetFont(),size,{x+.5f,y},color(c),value.c_str(),nullptr,width);}ImGui::PopFont();return height;
    }
    void image(guide::flow::Rect r,const guide::Reading&){if(localTexture.view.handle)list->AddImage(localTexture.view.handle,{r.x,r.y},{r.x+r.w,r.y+r.h});}
};
struct WindowScope{~WindowScope(){ImGui::End();ImGui::PopFont();ImGui::PopStyleColor(12);ImGui::PopStyleVar(8);}};
static void draw(reshade::api::effect_runtime* runtime){
    try{
        if(!attempted){attempted=true;wchar_t name[128]{};DWORD n=GetEnvironmentVariableW(L"THE_ORDER_GUIDE_SESSION",name,128);if(n==0||n>=128||!connection.open(name))return;}
        auto* h=connection.header;auto& io=ImGui::GetIO();
        if(!h||h->pid!=LONG(GetCurrentProcessId())||h->created!=guide::creation(GetCurrentProcess())||!connection.refresh()){io.MouseDrawCursor=false;return;}
        InterlockedExchange64(&h->rendererPulse,static_cast<LONG64>(GetTickCount64()));const bool front=foreground();
        if(edge(front&&(GetAsyncKeyState(guide::shortcutKey(h->hotkey))&0x8000),keyDown)
            &&h->status!=2&&guide::shortcutMatches(h->hotkey,guide::activeModifiers()))connection.toggle();
        const int readingKeys[]={VK_NEXT,VK_PRIOR,VK_RIGHT,VK_LEFT,VK_HOME,VK_END,VK_SPACE};for(int i=0;i<7;++i)if(edge(front&&h->visible&&plainKey(readingKeys[i]),readingKeysDown[i])){
            if(h->channel!=0)localState.navigate(readingKeys[i],connection);
            else if(connection.action(3)){if(readingKeys[i]==VK_RIGHT)InterlockedIncrement(&h->step);else if(readingKeys[i]==VK_LEFT)InterlockedDecrement(&h->step);else if(readingKeys[i]==VK_NEXT||readingKeys[i]==VK_SPACE)pageMotion=1;else if(readingKeys[i]==VK_PRIOR)pageMotion=-1;}
        }
        io.MouseDrawCursor=false;if(!front||!h->visible){flowState.cancel();localState.dragging=false;return;}
        if((flowState.dragging||localState.dragging)&&guide::mouseReleasedAfter(h->mouseSample,pointerDownAt,GetTickCount64())&&!ImGui::IsMouseReleased(0)){flowState.cancel();localState.dragging=false;}
        h->interactive=1;runtime->block_input_next_frame();auto& d=connection.document;
        light=h->lightTheme!=0;ink=light?ImVec4(.17f,.18f,.20f,1):ImVec4(.91f,.92f,.94f,1);
        muted=light?ImVec4(.46f,.47f,.49f,1):ImVec4(.59f,.61f,.65f,1);accent=light?ImVec4(.26f,.32f,.40f,1):ImVec4(.76f,.79f,.84f,1);
        if(selectedSource!=h->selected){if(selectedSource>=0)h->route=h->step=0;selectedSource=h->selected;previousRoute=-1;readingFirst=chapterFirst=0;}
        int routeIndex=d.routes.empty()?-1:std::clamp(int(h->route),0,int(d.routes.size())-1);h->route=std::max(0,routeIndex);
        const guide::Route* route=routeIndex<0?nullptr:&d.routes[routeIndex];
        int selected=route?std::clamp(int(h->step),0,std::max(0,int(route->steps.size())-1)):0;h->step=selected;
        auto layout=guide::reading::layout(io.DisplaySize.x,io.DisplaySize.y,float(h->dpi>0?h->dpi:96));const float s=layout.scale;
        compactWindow=layout.compact;
        h->panelWidth=LONG(layout.width);h->panelHeight=LONG(layout.height);h->bodyFontPx=LONG(guide::reading::style(compactWindow).body*s);h->showOriginal=showOriginal;h->readerBackend=1;
        h->readingWidth=LONG(layout.readerWidth);h->navigationWidth=LONG(layout.sidebar);h->indexWidth=LONG(layout.tocWidth);
        h->layoutMode=layout.tocWidth>0?3:layout.sidebar>0?2:1;h->fontRoles=ImGui::GetIO().Fonts->Fonts.Size;
        if(previousRoute!=routeIndex)readingFirst=chapterFirst=0;
        ImGui::SetNextWindowSize(ImVec2(layout.width,layout.height),ImGuiCond_Always);ImGui::SetNextWindowPos(ImVec2(layout.x,layout.y),ImGuiCond_Always);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,ImVec2((compactWindow?14.f:28.f)*s,(compactWindow?10.f:22.f)*s));ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding,8*s);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize,0);ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding,3*s);
        ImGui::PushStyleVar(ImGuiStyleVar_FramePadding,ImVec2(9*s,5*s));ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing,ImVec2(10*s,(compactWindow?5.f:8.f)*s));
        ImGui::PushStyleVar(ImGuiStyleVar_ScrollbarSize,6*s);ImGui::PushStyleVar(ImGuiStyleVar_PopupRounding,10*s);
        ImGui::PushStyleColor(ImGuiCol_Text,ink);ImGui::PushStyleColor(ImGuiCol_FrameBg,ImVec4(.14f,.15f,.17f,1));
        ImGui::PushStyleColor(ImGuiCol_Button,ImVec4(.14f,.15f,.17f,1));ImGui::PushStyleColor(ImGuiCol_ButtonHovered,ImVec4(.23f,.24f,.27f,1));
        ImGui::PushStyleColor(ImGuiCol_ButtonActive,ImVec4(.28f,.29f,.32f,1));ImGui::PushStyleColor(ImGuiCol_Header,ImVec4(.17f,.18f,.20f,1));
        ImGui::PushStyleColor(ImGuiCol_PopupBg,ImVec4(.08f,.09f,.10f,1));ImGui::PushStyleColor(ImGuiCol_ScrollbarBg,ImVec4(0,0,0,0));
        ImGui::PushStyleColor(ImGuiCol_ScrollbarGrab,ImVec4(.46f,.49f,.52f,.55f));ImGui::PushStyleColor(ImGuiCol_FrameBgHovered,ImVec4(.23f,.24f,.27f,1));
        ImGui::PushStyleColor(ImGuiCol_HeaderHovered,ImVec4(.24f,.25f,.27f,1));ImGui::PushStyleColor(ImGuiCol_HeaderActive,ImVec4(.28f,.29f,.32f,1));
        if(light){auto& c=ImGui::GetStyle().Colors;
            c[ImGuiCol_FrameBg]=ImVec4(.91f,.91f,.90f,1);c[ImGuiCol_Button]=ImVec4(.91f,.91f,.90f,1);c[ImGuiCol_ButtonHovered]=ImVec4(.86f,.87f,.87f,1);
            c[ImGuiCol_ButtonActive]=ImVec4(.80f,.82f,.83f,1);c[ImGuiCol_Header]=ImVec4(.89f,.89f,.88f,1);c[ImGuiCol_PopupBg]=ImVec4(.98f,.98f,.97f,1);
            c[ImGuiCol_ScrollbarGrab]=ImVec4(.52f,.54f,.57f,.55f);c[ImGuiCol_FrameBgHovered]=ImVec4(.86f,.87f,.87f,1);
            c[ImGuiCol_HeaderHovered]=ImVec4(.85f,.85f,.83f,1);c[ImGuiCol_HeaderActive]=ImVec4(.81f,.82f,.80f,1);
        }
        ImGui::PushFont(nullptr,(compactWindow?13.f:16.f)*s);
        const auto flags=ImGuiWindowFlags_NoTitleBar|ImGuiWindowFlags_NoResize|ImGuiWindowFlags_NoMove|ImGuiWindowFlags_NoCollapse|
            ImGuiWindowFlags_NoSavedSettings|ImGuiWindowFlags_NoFocusOnAppearing|ImGuiWindowFlags_NoBackground|ImGuiWindowFlags_NoScrollbar|ImGuiWindowFlags_NoScrollWithMouse;
        const bool shown=ImGui::Begin("TheOrderGuide",nullptr,flags);WindowScope window;
        if(shown){
            auto p=ImGui::GetWindowPos();auto end=ImVec2(p.x+layout.width,p.y+layout.height);auto* list=ImGui::GetWindowDrawList();
            ImGui::GetBackgroundDrawList()->AddRectFilled(ImVec2(0,0),io.DisplaySize,IM_COL32(0,0,0,light?40:70));
            list->AddRectFilled(p,end,light?IM_COL32(248,247,244,246):IM_COL32(21,23,27,240),8*s);list->AddRect(p,end,light?IM_COL32(90,92,98,45):IM_COL32(200,203,210,40),8*s,0,s);
            const bool attribution=h->channel==0&&!d.attribution.empty();
            const auto heading=guide::reading::header(layout,d.reading.present,attribution);layout.contentTop=heading.contentTop;
            const ImVec2 origin(p.x+layout.inset,p.y+heading.top);
            ImGui::SetCursorScreenPos(ImVec2(origin.x,origin.y+2*s));
            const float titleWidth=heading.titleWidth;
            list->PushClipRect(origin,ImVec2(origin.x+titleWidth,origin.y+24*s),true);
            text(d.title,compactWindow?16.f:18.f,s,ink,ImGui::GetCursorPosX()+titleWidth,2);
            list->PopClipRect();
            ImGui::SetCursorScreenPos(ImVec2(end.x-layout.inset-48*s,origin.y));
            ImGui::PushFont(nullptr,13*s);if(ImGui::Button("收起",ImVec2(48*s,24*s)))h->visible=h->interactive=0;ImGui::PopFont();
            if(attribution){ImGui::SetCursorScreenPos(ImVec2(origin.x,p.y+heading.attributionY));
                list->PushClipRect(ImVec2(origin.x,p.y+heading.attributionY),ImVec2(end.x-layout.inset,p.y+heading.attributionY+14*s),true);
                text(d.attribution,11.f,s,muted,ImGui::GetCursorPosX()+layout.width-2*layout.inset);list->PopClipRect();}
            list->AddLine(ImVec2(origin.x,p.y+heading.ruleY),ImVec2(end.x-layout.inset,p.y+heading.ruleY),
                light?IM_COL32(95,100,111,28):IM_COL32(185,191,202,28),s);
            FlowImGuiPainter localPainter{list};
            if(d.reading.present){guide::local::sourceBar(localPainter,localState,d,h,{p.x+heading.sourceX,p.y+heading.sourceY,heading.sourceWidth,24*s},s,light);}
            const float areaHeight=layout.contentBottom-layout.contentTop;
            if(h->channel!=0) {
                localTexture.update(runtime,d.reading,h->sequence);
                guide::local::render(localPainter,localState,d,h,{origin.x,p.y+layout.contentTop,layout.width-2*layout.inset,areaHeight},s,light);
                ImGui::SetCursorScreenPos({origin.x,p.y+layout.contentTop});ImGui::InvisibleButton("LocalReadingCanvas",{layout.width-2*layout.inset,areaHeight});
                guide::Point mouse{io.MousePos.x,io.MousePos.y};if(ImGui::IsMouseClicked(0)){pointerDownAt=GetTickCount64();localState.down(mouse);}if(ImGui::IsMouseDown(0))localState.move(mouse);if(ImGui::IsMouseReleased(0))localState.up(mouse,connection);
                if(io.MouseWheel)localState.wheel(mouse,io.MouseWheel,&connection);
                h->navigationWidth=h->indexWidth=0;h->readingWidth=LONG(layout.width-2*layout.inset);
                h->layoutMode=4;h->visibleSteps=LONG(d.reading.blocks.size());h->readingFirst=d.reading.page;h->bodyFontPx=LONG(16*s*localState.zoom);h->scrollY=LONG(localState.scrollY);h->visibleStage=LONG(localState.maximumY);h->flowZoom=LONG(localState.zoom*1000);h->readerAck=h->sequence;h->overflowRows=d.reading.rgba.empty()?0:localTexture.view.handle?0:1;
            } else if(h->graph&&!d.flow.nodes.empty()) {
                const ImVec2 toolbar(p.x+layout.inset,p.y+layout.contentTop);
                ImGui::SetCursorScreenPos(toolbar);tab("流程图",true,80*s,s);
                ImGui::SetCursorScreenPos(ImVec2(toolbar.x+96*s,toolbar.y));if(tab("连续阅读",false,96*s,s))h->graph=0;
                FlowImGuiPainter painter{list};guide::flow::Rect area{toolbar.x,toolbar.y+30*s,layout.width-2*layout.inset,areaHeight-30*s};
                guide::flow::render(painter,flowState,d,area,s,light,routeIndex,h->sequence);
                ImGui::SetCursorScreenPos({area.x,area.y});ImGui::InvisibleButton("GuideFlowCanvas",{area.w,area.h});
                guide::Point mouse{io.MousePos.x,io.MousePos.y};
                if(ImGui::IsMouseClicked(0)){pointerDownAt=GetTickCount64();flowState.down(mouse);}
                if(ImGui::IsMouseDown(0))flowState.move(mouse);
                if(ImGui::IsMouseReleased(0))flowState.up(mouse,d);
                if(io.MouseWheel&&flowState.canvas.has(mouse))flowState.wheel(mouse,io.MouseWheel);
                if(pageMotion){flowState.panY-=pageMotion*flowState.canvas.h*.75f;flowState.autoFit=false;pageMotion=0;}
                if(flowState.readRoute>=0){h->route=flowState.readRoute;h->step=flowState.readStep;h->graph=0;jumpRequested=true;flowState.readRoute=-1;}
                h->flowNodes=LONG(flowState.activeNodes.size());h->flowEdges=LONG(flowState.activeEdges.size());h->flowSelected=flowState.selectedEdge;h->flowZoom=LONG(flowState.zoom*1000);
                h->navigationWidth=h->indexWidth=0;h->readingWidth=LONG(area.w);
            } else {
            if(layout.sidebar>0) {
                ImGui::SetCursorScreenPos(ImVec2(p.x+layout.inset,p.y+layout.contentTop));
                if(ImGui::BeginChild("routeRail",ImVec2(layout.sidebar,areaHeight),ImGuiChildFlags_None))
                    routeList(d,routeIndex,layout.sidebar-6*s,s);
                ImGui::EndChild();
            }
            ImGui::SetCursorScreenPos(ImVec2(p.x+layout.readerX,p.y+layout.contentTop));
            if(ImGui::BeginChild("readingColumn",ImVec2(layout.readerWidth,areaHeight),ImGuiChildFlags_None,
                ImGuiWindowFlags_NoScrollbar|ImGuiWindowFlags_NoScrollWithMouse)) {
                if(d.sources.size()>1) {
                    ImGui::SetNextItemWidth(-1);int source=std::clamp(int(h->selected),0,int(d.sources.size())-1);
                    if(ImGui::BeginCombo("##source",d.sources[source].c_str())) {
                        for(int i=0;i<int(d.sources.size());++i) {
                            ImGui::PushID(i);
                            if(ImGui::Selectable(d.sources[i].c_str(),i==source)) {h->argument=i;MemoryBarrier();h->command=1;}
                            ImGui::PopID();
                        }
                        ImGui::EndCombo();
                    }
                }
                if(layout.sidebar==0&&route&&!h->graph) {
                    ImGui::SetNextItemWidth(-1);
                    const auto label=guide::reading::routeName(*route)+(compactWindow?" · "+std::to_string(routeIndex+1)+"/"+std::to_string(d.routes.size()):"");
                    if(ImGui::BeginCombo("##route",label.c_str())) {
                        for(int i=0;i<int(d.routes.size());++i){
                            ImGui::PushID(i);const auto item=d.routes[i].title+" → "+d.routes[i].to;
                            if(ImGui::Selectable(item.c_str(),i==routeIndex)) {h->route=i;h->step=0;h->graph=0;jumpRequested=true;}ImGui::PopID();}
                        ImGui::EndCombo();
                    }
                }
                if(h->graph&&compactWindow){
                    const auto head=ImGui::GetCursorScreenPos();text("路线概览",17,s,ink,0,2);
                    ImGui::SetCursorScreenPos(ImVec2(head.x+94*s,head.y+6*s));text(std::to_string(d.routes.size())+" 条路线 · "+std::to_string(guide::reading::stepCount(d))+" 步",11,s,muted);
                    ImGui::SetCursorScreenPos(ImVec2(head.x,head.y+28*s));
                } else if(h->graph) {
                    text("攻略结构",12,s,muted);text("路线概览",23,s,ink,0,2);
                    text(std::to_string(d.routes.size())+" 条路线段 · "+std::to_string(guide::reading::stepCount(d))+" 个步骤",13,s,muted);
                } else if(route){
                    if(compactWindow&&layout.sidebar==0){text(route->from+" → "+route->to,11,s,muted);const auto split=route->title.find(" · ");if(split!=std::string::npos)text(route->title.substr(split+4),12,s,muted);}
                    else routeHeader(*route,routeIndex,int(d.routes.size()),layout.readerWidth,s);
                }
                const auto toolbar=ImGui::GetCursorScreenPos();
                if(tab(d.flow.nodes.empty()?"路线概览":"流程图",h->graph!=0,80*s,s))h->graph=1;
                ImGui::SetCursorScreenPos(ImVec2(toolbar.x+96*s,toolbar.y));
                if(tab("连续阅读",!h->graph,96*s,s))h->graph=0;
                ImGui::SetCursorScreenPos(ImVec2(toolbar.x+layout.readerWidth-116*s,toolbar.y+2*s));
                ImGui::BeginDisabled(h->graph!=0);ImGui::PushFont(nullptr,13*s);ImGui::Checkbox("原文对照",&showOriginal);ImGui::PopFont();ImGui::EndDisabled();
                ImGui::SetCursorScreenPos(ImVec2(toolbar.x,toolbar.y+(compactWindow?30.f:39.f)*s));
                if(!d.status.empty())text(d.status,18,s,accent);
                if(route) {
                    if(!h->graph) {
                        const int first=std::clamp(readingFirst,0,std::max(0,int(route->steps.size())-1));
                        text(guide::reading::sectionAt(*route,first)+"  ·  阅读位置 "+guide::reading::position(*route,first),compactWindow?11.f:12.f,s,muted);
                    }
                    if(h->graph)overview(d,s);
                    else {reading(*route,routeIndex,selected,s,jumpRequested||previousRoute!=routeIndex);jumpRequested=false;}
                }
            }
            ImGui::EndChild();
            if(layout.tocWidth>0&&route) {
                ImGui::SetCursorScreenPos(ImVec2(p.x+layout.tocX,p.y+layout.contentTop));
                if(ImGui::BeginChild("chapterRail",ImVec2(layout.tocWidth,areaHeight),ImGuiChildFlags_None)) {
                    if(!h->graph)indexList(*route,layout.tocWidth-6*s,s);
                    else {
                        text("阅读提示",13,s,muted);ImGui::Dummy(ImVec2(0,12*s));
                        text("选择路线进入正文。",14,s,ink);
                        text("路线段按原攻略顺序排列，存读档关联保持不变。",13,s,muted);
                    }
                }
                ImGui::EndChild();
            }
            }
            if(ImGui::IsMouseReleased(0)&&h->channel==0)localState.up({io.MousePos.x,io.MousePos.y},connection);
            io.MouseDrawCursor=false;
        }
        previousRoute=routeIndex;previousStep=selected;InterlockedIncrement(&h->renderedFrames);
    }catch(...){ImGui::GetIO().MouseDrawCursor=false;if(connection.header)connection.header->visible=connection.header->interactive=0;}
}
extern "C" __declspec(dllexport) const char* NAME="The Order Guide";
extern "C" __declspec(dllexport) const char* DESCRIPTION="Read-only game walkthrough companion. No game data or credentials are read.";
BOOL APIENTRY DllMain(HMODULE module,DWORD reason,LPVOID){
    if(reason==DLL_PROCESS_ATTACH){if(!reshade::register_addon(module))return FALSE;reshade::register_event<reshade::addon_event::reshade_overlay>(draw);reshade::register_event<reshade::addon_event::destroy_effect_runtime>(releaseLocalTexture);}
    else if(reason==DLL_PROCESS_DETACH)reshade::unregister_addon(module);return TRUE;
}
