#pragma once
#include "GuideProtocol.hpp"

namespace guide::reading {
constexpr float opacity = .94f, bodyFont = 20.f, originalFont = 16.f;
constexpr float textInset = 66.f, stepGap = 4.f;
struct Layout {
    float x, y, width, height, scale, sidebar;
    float inset, readerX, readerWidth, tocX, tocWidth, contentTop, contentBottom, footerY;
    bool compact;
};
struct HeaderLayout {
    float top,titleWidth,sourceX,sourceY,sourceWidth,attributionY,ruleY,contentTop;
};
inline HeaderLayout header(const Layout& layout,bool sources,bool attribution) {
    const float s=layout.scale,available=layout.width-2*layout.inset,top=8*s;
    const bool inlineSources=sources&&available>=620*s;
    const float sourceWidth=std::min(246*s,available),titleWidth=available-60*s-(inlineSources?sourceWidth+14*s:0);
    const float sourceX=inlineSources?layout.inset+titleWidth+14*s:layout.inset;
    const float sourceY=inlineSources?top:top+30*s;
    const float rowBottom=(sources&&!inlineSources?sourceY:top)+24*s;
    const float attributionY=rowBottom+3*s,ruleY=attribution?attributionY+15*s:rowBottom+4*s;
    return {top,titleWidth,sourceX,sourceY,sourceWidth,attributionY,ruleY,ruleY+6*s};
}
inline Layout layout(float width, float height, float dpi = 96.f) {
    const float density=std::clamp(dpi/96.f,1.f,3.f);
    const bool compact=width/density<820||height/density<600;
    const float fit=compact?std::clamp(std::min(width/(density*560),height/(density*320)),.8f,1.f):
        std::clamp(std::min(width/(density*720),height/(density*480)),.85f,1.f);
    const float scale=density*fit;
    const float w=std::min(width*(compact?.96f:.92f),1160*scale),h=height*(compact?.94f:.90f);
    const float navigation=w>=820*scale?(compact?168.f:184.f)*scale:0;
    const float toc=!compact&&w>=1056*scale?136*scale:0;
    const float gaps=(navigation>0?24*scale:0)+(toc>0?24*scale:0);
    const float reader=std::min(640*scale,w-(compact?28.f:48.f)*scale-navigation-toc-gaps);
    const float inset=(w-navigation-toc-gaps-reader)/2;
    const float readerX=inset+navigation+(navigation>0?24*scale:0);
    return {(width-w)/2,(height-h)/2,w,h,scale,navigation,inset,readerX,reader,
        readerX+reader+(toc>0?24*scale:0),toc,(compact?56.f:86.f)*scale,h-10*scale,h-10*scale,compact};
}
struct Style {float body,original,heading,padding,stagePadding,gap,label,alternative,alternativeOriginal;};
inline Style style(bool compact){return compact?Style{16,13,18,10,18,2,10,13,12}:Style{20,16,22,14,24,4,12,16,15};}
inline bool anchor(const Step& step){return step.heading||step.kind=="save"||step.kind=="load";}
inline std::string routeName(const Route& route){return route.title.substr(0,route.title.find(" · "));}
inline int stepNumber(const Route& route,int index){
    int number=0;for(int i=0;i<=index&&i<int(route.steps.size());++i)if(!route.steps[i].heading)++number;return number;
}
inline int stepCount(const Route& route){return stepNumber(route,int(route.steps.size())-1);}
inline float rowInset(int count,bool compact=false){return (compact?54.f:textInset)+std::max(0,int(std::to_string(std::max(1,count)).size())-2)*7.f;}
inline int stepCount(const Document& document){int count=0;for(const auto& route:document.routes)count+=stepCount(route);return count;}
inline std::string position(const Route& route,int index){
    const int total=stepCount(route);if(total==0)return "—";
    const int at=std::clamp(index,0,int(route.steps.size())-1);
    const int number=std::clamp(stepNumber(route,at)+(route.steps[at].heading?1:0),1,total);
    return std::to_string(number)+" / "+std::to_string(total);
}
inline std::string sectionAt(const Route& route,int index){
    for(int i=std::min(index,int(route.steps.size())-1);i>=0;--i)if(route.steps[i].heading)return route.steps[i].display.empty()?route.steps[i].original:route.steps[i].display;
    return "步骤";
}
inline const std::string& primary(const Step& step) { return step.display.empty() ? step.original : step.display; }
inline const std::string& primary(const Alternative& step) { return step.display.empty() ? step.original : step.display; }
inline bool translated(const Step& step) { return !step.display.empty() && step.display != step.original; }
inline bool translated(const Alternative& step) { return !step.display.empty() && step.display != step.original; }
template<class Measure> inline float rowHeight(const Step& step,float width,float scale,bool original,Measure measure,float inset=textInset,bool compact=false){
    const auto type=style(compact);
    const float textWidth=std::max(32*scale,width-(inset+12)*scale);
    float h=(step.heading?type.stagePadding:type.padding)*scale+measure(primary(step),textWidth,step.heading?type.heading:type.body,step.heading?2:0);
    if(original&&translated(step))h+=(compact?4.f:6.f)*scale+measure(step.original,textWidth,type.original,0);
    if(step.achievement)h+=(compact?14.f:18.f)*scale;
    if(!step.alternatives.empty()){
        h+=(compact?18.f:24.f)*scale;
        for(const auto& a:step.alternatives){
            h+=measure("· "+primary(a),textWidth,type.alternative,0)+(compact?3.f:4.f)*scale;
            if(original&&translated(a))h+=measure(a.original,textWidth,type.alternativeOriginal,0)+(compact?3.f:4.f)*scale;
        }
    }
    return h+type.gap*scale;
}
}
