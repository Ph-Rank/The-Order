#pragma once
#include "GuideFlowCanvas.hpp"

namespace guide::local {
using flow::Rect; using flow::Color;
inline bool pdfPage(const Reading& reading){
    if(reading.file<0||reading.file>=int(reading.files.size()))return false;
    const auto& name=reading.files[reading.file];if(name.size()<4)return false;
    std::string extension=name.substr(name.size()-4);for(auto& c:extension)if(c>='A'&&c<='Z')c=char(c-'A'+'a');return extension==".pdf";
}
struct Hit {Rect box;int command,argument;};
struct State {
    LONG revision=-1;float scrollX=0,scrollY=0,maximumX=0,maximumY=0,zoom=1,scale=1;bool filesOpen=false,pagesOpen=false,dragging=false,restorePending=false;int fileFirst=0,pageFirst=0;
    Point previous{};Rect canvas{};std::vector<Hit> hits;
    void changed(LONG value,const Reading& r){if(revision!=value){revision=value;scrollX=scrollY=0;zoom=r.zoom;filesOpen=pagesOpen=dragging=false;restorePending=true;}}
    bool wheel(Point p,float delta,Connection* c=nullptr){if(!canvas.has(p))return false;if(filesOpen)fileFirst=std::max(0,fileFirst-int(delta*3));else if(pagesOpen)pageFirst=std::max(0,pageFirst-int(delta*12));else if(c&&!pdfPage(c->document.reading)&&delta<0&&scrollY>=maximumY-.5f&&c->document.reading.page+1<c->document.reading.pages)c->request(5,1);else if(c&&!pdfPage(c->document.reading)&&delta>0&&scrollY<=.5f&&c->document.reading.page>0)c->request(5,-1);else scrollY=std::clamp(scrollY-delta*90*scale,0.f,maximumY);return true;}
    bool navigate(int key,Connection& c){const auto& r=c.document.reading;if(r.pages<1||!c.action(3))return false;
        if(key==VK_RIGHT)return r.page+1<r.pages&&c.request(4,r.page+1);if(key==VK_LEFT)return r.page>0&&c.request(4,r.page-1);
        if(key==VK_HOME){scrollY=0;return true;}if(key==VK_END){scrollY=maximumY;return true;}
        if(key==VK_NEXT||key==VK_SPACE){if(scrollY>=maximumY-.5f&&r.page+1<r.pages)return c.request(5,1);scrollY=std::min(maximumY,scrollY+canvas.h*.85f);return true;}
        if(key==VK_PRIOR){if(scrollY<=.5f&&r.page>0)return c.request(5,-1);scrollY=std::max(0.f,scrollY-canvas.h*.85f);return true;}return false;
    }
    bool down(Point p){if(!canvas.has(p)||filesOpen||pagesOpen)return false;previous=p;dragging=true;return true;}
    bool move(Point p){if(!dragging)return false;scrollX=std::clamp(scrollX+previous.x-p.x,0.f,maximumX);scrollY=std::clamp(scrollY+previous.y-p.y,0.f,maximumY);previous=p;return true;}
    bool up(Point p,Connection& c){if(dragging){dragging=false;return true;}for(const auto& h:hits)if(h.box.has(p)){if(h.command==10){filesOpen=!filesOpen;pagesOpen=false;}else if(h.command==14){pagesOpen=!pagesOpen;filesOpen=false;pageFirst=std::max(0,c.document.reading.page-8);}else if(h.command==11)zoom=std::max(.6f,zoom/1.2f);else if(h.command==12)zoom=std::min(4.f,zoom*1.2f);else if(h.command==13){zoom=1;scrollX=scrollY=0;}else {if(c.request(h.command,h.argument)&&h.command==2)c.header->channel=h.argument;filesOpen=pagesOpen=false;}return true;}return false;}
};
template<class P> void control(P& p,State& state,std::string label,Rect box,int command,int argument,bool active,bool light,float size){
    p.box(box,light?Color{241,242,243,255}:Color{43,46,53,255},active?Color{72,194,245,255}:light?Color{80,90,110,35}:Color{210,220,235,28},3*state.scale,active?1.5f:1.f);
    p.text(label,box,size,light?Color{32,34,39,255}:Color{235,238,243,255},true,active);state.hits.push_back({box,command,argument});
}
template<class P> void sourceBar(P& p,State& state,const Document& d,Header* h,Rect area,float scale,bool light){
    state.scale=scale;state.hits.clear();const float gap=6*scale,height=24*scale;
    const char* names[]={"网络攻略","本地攻略","说明文件"};const float width=std::min(78*scale,(area.w-2*gap)/3);
    for(int i=0;i<3;++i)control(p,state,names[i],{area.x+i*(width+gap),area.y,width,height},2,i,h->channel==i,light,12*scale);
    (void)d;
}
template<class P> void render(P& p,State& state,const Document& d,Header* h,Rect area,float scale,bool light){
    const auto& r=d.reading;state.changed(h->sequence,r);state.scale=scale;
    const Color readingInk=light?Color{37,40,46,255}:Color{233,237,244,255},readingMuted=light?Color{91,99,111,255}:Color{164,174,190,255},line=light?Color{80,90,110,36}:Color{200,213,235,30};
    const float height=24*scale,gap=6*scale;float fileWidth=std::max(70*scale,area.w-282*scale);bool narrow=area.w<370*scale;
    if(narrow)fileWidth=area.w;
    control(p,state,r.files.empty()?"选择文件":r.files[std::clamp(r.file,0,int(r.files.size())-1)]+"  v",{area.x,area.y,fileWidth,height},10,0,false,light,13*scale);
    float toolsY=area.y+(narrow?30*scale:0),x=narrow?area.x:area.x+fileWidth+gap;
    control(p,state,"‹",{x,toolsY,30*scale,height},4,std::max(0,r.page-1),false,light,17*scale);x+=34*scale;
    control(p,state,std::to_string(r.pages?r.page+1:0)+" / "+std::to_string(r.pages),{x,toolsY,66*scale,height},14,0,false,light,12*scale);x+=70*scale;
    control(p,state,"›",{x,toolsY,30*scale,height},4,std::min(std::max(0,r.pages-1),r.page+1),false,light,17*scale);x+=38*scale;
    control(p,state,"−",{x,toolsY,30*scale,height},11,0,false,light,15*scale);x+=34*scale;
    control(p,state,"+",{x,toolsY,30*scale,height},12,0,false,light,15*scale);x+=34*scale;
    control(p,state,"适应",{x,toolsY,46*scale,height},13,0,false,light,12*scale);
    const float top=toolsY+30*scale;state.canvas={area.x,top,area.w,std::max(1.f,area.y+area.h-top)};
    p.clip(state.canvas);
    p.box(state.canvas,light?Color{248,247,244,255}:Color{21,23,27,255},Color{0,0,0,0},1);
    if(state.pagesOpen){
        int columns=std::max(1,int(area.w/(64*scale))),visible=std::max(1,int(state.canvas.h/(38*scale)))*columns;state.pageFirst=std::clamp(state.pageFirst,0,std::max(0,r.pages-visible));
        for(int i=state.pageFirst;i<std::min(r.pages,state.pageFirst+visible);++i){int at=i-state.pageFirst;control(p,state,std::to_string(i+1),{area.x+(at%columns)*(area.w/columns)+2*scale,top+(at/columns)*38*scale,area.w/columns-4*scale,34*scale},4,i,i==r.page,light,13*scale);}p.unclip();return;
    }
    if(state.filesOpen){
        int visible=std::max(1,int(state.canvas.h/(34*scale)));state.fileFirst=std::clamp(state.fileFirst,0,std::max(0,int(r.files.size())-visible));
        p.box(state.canvas,light?Color{249,249,249,255}:Color{30,33,39,255},line,3*scale);
        if(r.files.empty())p.text("没有找到文件",state.canvas,14*scale,readingMuted,true);
        for(int i=state.fileFirst;i<std::min(int(r.files.size()),state.fileFirst+visible);++i)control(p,state,r.files[i],{area.x+4*scale,top+(i-state.fileFirst)*34*scale,area.w-8*scale,32*scale},3,i,r.file==i,light,13*scale);
        p.unclip();return;
    }
    if(h->channel!=r.channel){p.text("正在切换…",state.canvas,16*scale,readingMuted,true);p.unclip();return;}
    const float font=16*scale*state.zoom;float width=std::min(900*scale*state.zoom,area.w-24*scale),left=area.x+(area.w-width)/2;
    float y=top-state.scrollY+8*scale;
    if(!r.notice.empty()&&r.blocks.empty()&&r.rgba.empty()){float size=12*scale;float measured=p.paragraph(r.notice,left,y,width,size,readingMuted,false,true);y+=measured+12*scale;}
    if(r.width>0&&r.height>0){
        const float baseWidth=std::max(1.f,area.w-24*scale);
        const float fittedWidth=pdfPage(r)?std::min(baseWidth,std::max(1.f,state.canvas.h-40*scale)*r.width/r.height):baseWidth;
        float w=fittedWidth*state.zoom,hImage=w*r.height/r.width;
        p.image({area.x+std::max(12*scale,(area.w-w)/2)-state.scrollX,y,w,hImage},r);y+=hImage+12*scale;state.maximumX=std::max(0.f,w-area.w+24*scale);
    }else state.maximumX=0;
    for(const auto& block:r.blocks){
        if(block.kind=="space"){y+=6*scale;continue;}
        if(block.kind=="table"){
            y+=p.paragraph(block.text,left,y,width,13*scale,readingMuted,false,true)+10*scale;
            size_t columns=0;for(const auto& row:block.rows)columns=std::max(columns,row.size());if(!columns)continue;
            float cellWidth=std::max(135*scale*state.zoom,width/float(columns)),tableWidth=cellWidth*float(columns),tableX=area.x+12*scale-state.scrollX;
            state.maximumX=std::max(state.maximumX,std::max(0.f,tableWidth-area.w+24*scale));
            for(size_t row=0;row<block.rows.size();++row){
                float rowHeight=30*scale;
                for(const auto& cell:block.rows[row])rowHeight=std::max(rowHeight,p.paragraph(cell,0,0,cellWidth-16*scale,font,readingInk,row==0,false)+14*scale);
                for(size_t col=0;col<columns;++col){Rect box{tableX+col*cellWidth,y,cellWidth,rowHeight};
                    if(row==0)p.box(box,light?Color{234,237,241,255}:Color{43,48,57,255},line,1);
                    p.line({box.x,box.y+box.h},{box.x+box.w,box.y+box.h},line,1);p.line({box.x+box.w,box.y},{box.x+box.w,box.y+box.h},line,1);
                    if(col<block.rows[row].size())p.paragraph(block.rows[row][col],box.x+8*scale,y+7*scale,cellWidth-16*scale,font,readingInk,row==0,true);
                }y+=rowHeight;
            }y+=18*scale;
        }else{bool heading=block.kind=="heading";y+=p.paragraph(block.text,left,y,width,heading?font*1.18f:font,readingInk,heading,true)+(heading?14.f:9.f)*scale;}
    }
    state.maximumY=std::max(0.f,y+state.scrollY-top-state.canvas.h+18*scale);state.scrollY=std::min(state.scrollY,state.maximumY);state.scrollX=std::min(state.scrollX,state.maximumX);
    if(state.restorePending){state.restorePending=false;state.scrollY=state.maximumY*r.progress;if(r.progress>0){p.unclip();render(p,state,d,h,area,scale,light);return;}}
    if(state.maximumY>0){float track=state.canvas.h,thumb=std::max(24*scale,track*track/(track+state.maximumY)),position=state.scrollY/state.maximumY*(track-thumb);p.box({area.x+area.w-4*scale,top+position,3*scale,thumb},readingMuted,readingMuted,1);}
    p.unclip();
}
}
