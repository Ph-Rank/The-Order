#pragma once
#include "GuideFlowCanvas.hpp"

namespace guide::chat {
constexpr LONG magic=0x54414348,version=1,inputOffset=256,inputBytes=16384,draftOffset=16640,stateOffset=33024,stateBytes=1048576,pixelOffset=1081600,pixelBytes=8388608,capacity=pixelOffset+pixelBytes;
struct Header {
    LONG magic,version;volatile LONG sequence,length,request,ack,command,argument,inputLength,busy,focus,capture;
    volatile LONG captureRequest,captureAck,captureStatus,width,height,pixelLength;
    volatile LONG64 inputPulse;volatile LONG draftSequence,draftLength;volatile LONG64 capturedAt;
    LONG pid,reserved100;LONG64 created;LONG owner;volatile LONG writer,imeX,imeY,captureWriter,caret,selectionStart,selectionEnd,editorSequence;BYTE reserved148[108];
};
static_assert(sizeof(Header)==256&&offsetof(Header,inputPulse)==72&&offsetof(Header,created)==104&&offsetof(Header,writer)==116);
struct Message{std::string role,text,meta;};
struct Snapshot{std::string status,model="选择模型",effort="high";bool busy=false,image=false,search=true,spoiler=true;std::vector<std::string> models;std::vector<Message> messages;};
inline Snapshot decode(const std::vector<BYTE>& bytes){
    Cursor c{bytes.data(),bytes.size()};Snapshot s;s.status=c.text();s.model=c.text();s.effort=c.text();s.busy=c.count(1)!=0;s.image=c.count(1)!=0;s.search=c.count(1)!=0;s.spoiler=c.count(1)!=0;
    for(int n=c.count(512);n--;)s.models.push_back(c.text());
    for(int n=c.count(32);n--;){Message m;m.role=c.text();m.text=c.text();m.meta=c.text();s.messages.push_back(std::move(m));}
    if(c.offset!=c.size)throw std::runtime_error("chat trailer");return s;
}
class Connection{
    HANDLE mapping=nullptr;BYTE* memory=nullptr;LONG seen=-1;
public:
    Header* h=nullptr;Snapshot state;std::wstring name;
    bool open(const std::wstring& base){if(!validName(base))return false;name=base;mapping=OpenFileMappingW(FILE_MAP_READ|FILE_MAP_WRITE,FALSE,(base+L".chat").c_str());if(!mapping)return false;
        memory=static_cast<BYTE*>(MapViewOfFile(mapping,FILE_MAP_READ|FILE_MAP_WRITE,0,0,capacity));if(!memory){CloseHandle(mapping);mapping=nullptr;return false;}h=reinterpret_cast<Header*>(memory);return h->magic==magic&&h->version==version;}
    bool valid(const guide::Header* main)const{return h&&h->magic==magic&&h->version==version&&h->pid==main->pid&&h->created==main->created&&h->owner==main->ownerPid;}
    bool refresh(){if(!h||h->magic!=magic)return false;LONG seq=h->sequence;if(seq==seen||(seq&1))return true;MemoryBarrier();LONG length=h->length;if(length<=0||length>stateBytes)return false;
        std::vector<BYTE> data(size_t(length),0);memcpy(data.data(),memory+stateOffset,size_t(length));MemoryBarrier();if(seq!=h->sequence||(h->sequence&1))return true;
        try{state=decode(data);seen=seq;}catch(...){return false;}return true;}
    LONG revision()const{return seen;}
    bool request(int command,int argument=0,const std::string& text=""){
        if(!h||text.size()>=inputBytes||InterlockedCompareExchange(&h->writer,1,0)!=0)return false;
        bool ok=h->request==h->ack;if(ok){memcpy(memory+inputOffset,text.data(),text.size());h->inputLength=LONG(text.size());h->argument=argument;h->command=command;MemoryBarrier();InterlockedIncrement(&h->request);}InterlockedExchange(&h->writer,0);return ok;}
    std::string draft()const{if(!h)return "";LONG seq=h->draftSequence;if(seq&1)return "";LONG n=h->draftLength;if(n<0||n>=inputBytes)return "";MemoryBarrier();std::string s(reinterpret_cast<char*>(memory+draftOffset),size_t(n));MemoryBarrier();return seq==h->draftSequence?s:"";}
    void draft(const std::string& value){if(!h||value.size()>=inputBytes)return;InterlockedIncrement(&h->draftSequence);memcpy(memory+draftOffset,value.data(),value.size());h->draftLength=LONG(value.size());MemoryBarrier();InterlockedIncrement(&h->draftSequence);}
    bool injectedInput()const{auto now=GetTickCount64();return h&&h->inputPulse>0&&now>=ULONGLONG(h->inputPulse)&&now-ULONGLONG(h->inputPulse)<700;}
    void frame(int request,const BYTE* pixels,int width,int height,int stride,bool bgra){
        if(!h||width<1||height<1||width>8192||height>8192||InterlockedCompareExchange(&h->captureWriter,1,0)!=0)return;
        struct Unlock{Header* h;~Unlock(){InterlockedExchange(&h->captureWriter,0);}} unlock{h};
        if(request!=h->captureRequest)return;
        float ratio=std::min(1.f,1600.f/float(std::max(width,height)));int w=std::max(1,int(width*ratio)),ht=std::max(1,int(height*ratio));
        if(size_t(w)*ht*4>pixelBytes)return;auto* dest=memory+pixelOffset;
        // Bound transport independently of the game's back-buffer resolution.
        for(int y=0;y<ht;++y)for(int x=0;x<w;++x){int sx=int((x+.5f)*width/w),sy=int((y+.5f)*height/ht);const BYTE* p=pixels+size_t(sy)*stride+size_t(sx)*4;BYTE* d=dest+(size_t(y)*w+x)*4;d[0]=p[bgra?2:0];d[1]=p[1];d[2]=p[bgra?0:2];d[3]=255;}
        if(request!=h->captureRequest){SecureZeroMemory(dest,size_t(w)*ht*4);return;}
        h->width=w;h->height=ht;h->pixelLength=w*ht*4;InterlockedExchange64(&h->capturedAt,LONG64(GetTickCount64()));h->captureStatus=1;MemoryBarrier();InterlockedExchange(&h->captureAck,request);
    }
    void failFrame(int request){if(h&&h->captureRequest==request){h->pixelLength=0;h->captureStatus=-1;MemoryBarrier();InterlockedExchange(&h->captureAck,request);}}
    ~Connection(){if(memory)UnmapViewOfFile(memory);if(mapping)CloseHandle(mapping);}
};
using flow::Rect;using flow::Color;
struct Hit{Rect box;int command,argument;};
struct Canvas{
    Rect outer{},body{},input{};float scroll=0,maximum=0,scale=1;bool modelsOpen=false;int modelFirst=0;size_t count=0;std::vector<Hit> hits;
    std::vector<float> rowHeights;LONG measuredRevision=-1;float measuredWidth=0,measuredScale=0;
    bool wheel(Point point,float delta){if(!body.has(point))return false;if(modelsOpen){modelFirst=std::max(0,modelFirst-int(delta*2));return true;}scroll=std::clamp(scroll-delta*75*scale,0.f,maximum);return true;}
    bool click(Point point,Connection& c){
        if(!c.h)return false;
        for(auto it=hits.rbegin();it!=hits.rend();++it)if(it->box.has(point)){
            if(it->command==10)modelsOpen=!modelsOpen;
            else if(it->command==11){c.h->capture=!c.h->capture;}
            else if(it->command==12){c.h->focus=1;}
            else if(it->command==1){auto text=c.draft();if(!text.empty()&&c.request(1,0,text)){/* Keep draft until the host confirms acceptance. */c.h->focus=0;}}
            else {if(c.request(it->command,it->argument))modelsOpen=false;}
            return true;
        }
        if(outer.has(point)){c.h->focus=0;modelsOpen=false;return true;}return false;
    }
};
template<class P> void render(P& p,Canvas& canvas,Connection& connection,Rect area,float s,bool light,bool nativeInput=false){
    auto& state=connection.state;auto* h=connection.h;canvas.outer=area;canvas.scale=s;canvas.hits.clear();if(!h){p.text("对话组件尚未连接",area,15*s,Color{150,156,166},true);return;}
    Color chatInk=light?Color{39,43,49}:Color{230,234,241},chatMuted=light?Color{103,110,121}:Color{151,163,181},line=light?Color{40,60,80,40}:Color{185,200,224,38},fill=light?Color{235,237,240}:Color{39,44,53},clear{0,0,0,0};
    auto button=[&](std::string label,Rect r,int command,int argument=0,bool selected=false){p.box(r,selected?fill:clear,line,4*s);p.text(label,r,12*s,selected?chatInk:chatMuted,true);canvas.hits.push_back({r,command,argument});};
    const bool compact=area.w<660*s||area.h<450*s;
    const float row=(compact?24.f:27.f)*s,gap=(compact?4.f:7.f)*s;
    const float effort=(compact?63.f:80.f)*s,toggle=(compact?51.f:68.f)*s,spoiler=(compact?65.f:86.f)*s;
    const float tools=effort+2*toggle+spoiler+3*gap;
    const bool twoRows=area.w<tools+gap+94*s;
    const float modelWidth=twoRows?area.w:std::min(255*s,area.w-tools-gap);
    button(state.model+"  ▾",{area.x,area.y,modelWidth,row},10);
    const float toolsY=area.y+(twoRows?row+gap:0);
    float x=area.x+(twoRows?0:modelWidth+gap);
    button(state.effort=="low"?"思考 · 低":state.effort=="medium"?"思考 · 中":state.effort=="xhigh"?"思考 · 极高":"思考 · 高",
        {x,toolsY,effort,row},4,state.effort=="low"?1:state.effort=="medium"?2:0);x+=effort+gap;
    button(h->capture?"画面 开":"画面 关",{x,toolsY,toggle,row},11,0,h->capture!=0);x+=toggle+gap;
    button(state.search?"联网 开":"联网 关",{x,toolsY,toggle,row},5,state.search?0:1,state.search);x+=toggle+gap;
    button(state.spoiler?"防剧透 开":"防剧透 关",{x,toolsY,spoiler,row},6,state.spoiler?0:1,state.spoiler);
    const float bodyY=toolsY+row+gap;
    const float composer=compact?std::clamp(area.h*.15f,44*s,68*s):std::clamp(area.h*.15f,60*s,90*s);
    const float notice=state.status.empty()?0:(compact?18.f:22.f)*s;
    canvas.body={area.x,bodyY,area.w,std::max(1.f,area.y+area.h-bodyY-composer-notice-6*s)};
    canvas.input={area.x,area.y+area.h-composer,area.w-48*s,composer};
    float width=std::min(790*s,area.w-24*s),left=area.x+(area.w-width)/2,y=canvas.body.y;
    bool bottom=canvas.scroll>=canvas.maximum-24*s;
    if(canvas.measuredRevision!=connection.revision()||canvas.measuredWidth!=width||canvas.measuredScale!=s){canvas.rowHeights.clear();for(auto& m:state.messages){float w=m.role=="user"?width*.84f:width;float hgt=p.paragraph(m.text,0,0,w-24*s,16*s,chatInk,false,false)+24*s+(m.meta.empty()?0:22*s);canvas.rowHeights.push_back(hgt);}canvas.measuredRevision=connection.revision();canvas.measuredWidth=width;canvas.measuredScale=s;}
    const auto& messageHeights=canvas.rowHeights;float total=0;for(auto height:messageHeights)total+=height+20*s;
    canvas.maximum=std::max(0.f,total-canvas.body.h);
    if(bottom||canvas.count!=state.messages.size())canvas.scroll=canvas.maximum;
    canvas.count=state.messages.size();
    canvas.scroll=std::clamp(canvas.scroll,0.f,canvas.maximum);y-=canvas.scroll;
    p.clip(canvas.body);
    if(state.messages.empty())p.text("随时聊聊眼前的这一幕",canvas.body,18*s,chatMuted,true);
    for(size_t i=0;i<state.messages.size();++i){auto& m=state.messages[i];float w=m.role=="user"?width*.84f:width,xx=left+(m.role=="user"?width-w:0),hgt=messageHeights[i];
        if(y+hgt>=canvas.body.y&&y<canvas.body.y+canvas.body.h){if(m.role=="user")p.box({xx,y,w,hgt},fill,clear,10*s);p.paragraph(m.text,xx+12*s,y+10*s,w-24*s,16*s,chatInk,false,true);if(!m.meta.empty())p.text(m.meta,{xx+12*s,y+hgt-24*s,w-24*s,20*s},11*s,chatMuted,false);}
        y+=hgt+20*s;
    }
    p.unclip();
    if(canvas.maximum>0){float thumb=std::max(20*s,canvas.body.h*canvas.body.h/(canvas.body.h+canvas.maximum));p.box({area.x+area.w-3*s,canvas.body.y+(canvas.body.h-thumb)*canvas.scroll/canvas.maximum,2*s,thumb},line,clear,s);}
    if(notice)p.text(state.status,{area.x,canvas.input.y-notice-3*s,area.w,notice},11*s,chatMuted,false);
    p.box(canvas.input,light?Color{251,251,250}:Color{30,34,41},h->focus?Color{115,156,188,150}:line,10*s);
    if(!nativeInput)p.draft(connection.draft(),{canvas.input.x+12*s,canvas.input.y+9*s,canvas.input.w-24*s,canvas.input.h-18*s},15*s,chatInk,*h);
    canvas.hits.push_back({canvas.input,12,0});
    Rect send{area.x+area.w-39*s,canvas.input.y+(composer-32*s)/2,32*s,32*s};p.box(send,fill,clear,16*s);p.text(state.busy?"■":"↑",send,20*s,chatInk,true);canvas.hits.push_back({send,state.busy?2:1,0});
    if(canvas.modelsOpen){Rect popup{area.x,canvas.body.y,std::min(area.w,390*s),std::min(float(state.models.size())*34*s+8*s,canvas.body.h)};p.box(popup,light?Color{248,248,247}:Color{30,34,42},line,6*s);p.clip(popup);
        int visible=std::max(1,int((popup.h-8*s)/(34*s)));canvas.modelFirst=std::clamp(canvas.modelFirst,0,std::max(0,int(state.models.size())-visible));
        for(int i=canvas.modelFirst;i<std::min(int(state.models.size()),canvas.modelFirst+visible);++i){Rect modelRow{popup.x+4*s,popup.y+4*s+float(i-canvas.modelFirst)*34*s,popup.w-8*s,32*s};p.text(state.models[size_t(i)],modelRow,13*s,chatInk,false);canvas.hits.push_back({modelRow,3,i});}p.unclip();}
}
}
