#pragma once
#include "GuideReaderLayout.hpp"
#include <cmath>

// Renderer-independent canvas shared by the injected ImGui reader and the
// normal window fallback. Dagre geometry arrives from the manager over v3 wire.
namespace guide::flow {
struct Rect {float x,y,w,h;bool has(Point p)const{return p.x>=x&&p.x<=x+w&&p.y>=y&&p.y<=y+h;}};
struct Color {int r,g,b,a=255;};
struct Hit {Rect box;int action,index;};
inline Color routeColor(int section,bool light){
    const Color dark[]={{85,181,219},{161,139,200},{117,181,156},{197,161,102},{205,142,171},{143,169,218}};
    const Color day[]={{0,121,159},{118,85,160},{52,123,101},{150,112,44},{162,79,105},{82,111,166}};
    return (light?day:dark)[std::max(0,section)%6];
}
inline std::vector<Point> curve(const std::vector<Point>& points){
    std::vector<Point> result;if(points.empty())return result;result.push_back(points.front());
    result.reserve(points.size()*7);
    for(size_t i=1;i+2<points.size();i+=3){const auto a=points[i-1],b=points[i],c=points[i+1],d=points[i+2];
        for(int j=1;j<=20;++j){float t=j/20.f,u=1-t;result.push_back({u*u*u*a.x+3*u*u*t*b.x+3*u*t*t*c.x+t*t*t*d.x,u*u*u*a.y+3*u*u*t*b.y+3*u*t*t*c.y+t*t*t*d.y});}}
    return result;
}
inline Point midpoint(const std::vector<Point>& points){
    float length=0;for(size_t i=1;i<points.size();++i)length+=std::hypot(points[i].x-points[i-1].x,points[i].y-points[i-1].y);
    float at=0;for(size_t i=1;i<points.size();++i){float part=std::hypot(points[i].x-points[i-1].x,points[i].y-points[i-1].y);if(at+part>=length/2&&part>0){float t=(length/2-at)/part;return {points[i-1].x+t*(points[i].x-points[i-1].x),points[i-1].y+t*(points[i].y-points[i-1].y)};}at+=part;}return points.empty()?Point{}:points.back();
}
template<class Painter> void shape(Painter& painter,Rect r,Color fill,Color stroke,int kind,float thickness=1){
    if(kind==1){painter.box(r,fill,stroke,r.h/14,thickness);return;}
    if(kind==2){painter.polygon({{r.x+r.w/2,r.y},{r.x+r.w,r.y+r.h/2},{r.x+r.w/2,r.y+r.h},{r.x,r.y+r.h/2}},fill,stroke,thickness);return;}
    painter.box(r,fill,stroke,r.h/2,thickness);
}
struct State {
    LONG revision=-1;float panX=0,panY=0,zoom=1,scale=1,minX=0,minY=0,graphWidth=1,graphHeight=1;
    Rect outer{},canvas{};bool currentOnly=false,hideCollections=false,initialized=false,autoFit=true,dragging=false,moved=false;
    Point downAt{},panAt{};int selectedNode=-1,selectedEdge=-1,readRoute=-1,readStep=0,route=-1;
    std::vector<Hit> hits;std::vector<int> activeEdges,activeNodes;std::vector<std::vector<Point>> lines;std::vector<Point> centers;
    std::vector<FlowNode> positions;std::vector<int> layoutEdges;bool filteredLayout=false;
    void reset(LONG value,const Flow&){if(revision==value)return;revision=value;initialized=false;autoFit=true;selectedNode=selectedEdge=-1;lines.clear();centers.clear();positions.clear();layoutEdges.clear();}
    void reflow(const Flow& flow,bool filtered){
        if(positions.size()==flow.nodes.size()&&layoutEdges==activeEdges&&filteredLayout==filtered)return;
        positions=flow.nodes;layoutEdges=activeEdges;filteredLayout=filtered;lines.clear();centers.clear();for(const auto& e:flow.edges){lines.push_back(curve(e.points));centers.push_back(e.label);}if(!filtered||activeNodes.empty())return;
        // Same interval packing as the manager. Recompute only when filtering
        // changes; node/edge indices and original reading ranges remain intact.
        const size_t count=flow.nodes.size();std::vector<std::vector<int>> children(count);std::vector<int> incoming(count,-1),order,roots;
        for(int index:activeEdges){const auto& e=flow.edges[index];if(incoming[e.to]>=0)return;children[e.from].push_back(index);incoming[e.to]=index;}
        for(int index:activeNodes)if(incoming[index]<0)roots.push_back(index);
        for(auto& links:children)std::sort(links.begin(),links.end(),[&](int a,int b){return flow.nodes[flow.edges[a].to].x<flow.nodes[flow.edges[b].to].x;});
        auto queue=roots;std::vector<bool> visited(count);while(!queue.empty()){int index=queue.back();queue.pop_back();if(visited[index])return;visited[index]=true;order.push_back(index);for(int edge:children[index])queue.push_back(flow.edges[edge].to);}if(order.size()!=activeNodes.size())return;
        struct Profile{float left,right;};std::vector<std::vector<Profile>> profile(count);std::vector<float> leftBound(count),rightBound(count),offset(flow.edges.size());
        for(auto it=order.rbegin();it!=order.rend();++it){int index=*it;const auto& links=children[index];std::vector<Profile> merged;
            for(int edge:links){const auto& child=profile[flow.edges[edge].to];float shift=0;for(size_t i=0;i<std::min(merged.size(),child.size());++i)shift=std::max(shift,merged[i].right-child[i].left+32);offset[edge]=shift;
                for(size_t i=0;i<child.size();++i){Profile p{child[i].left+shift,child[i].right+shift};if(i>=merged.size())merged.push_back(p);else{merged[i].left=std::min(merged[i].left,p.left);merged[i].right=std::max(merged[i].right,p.right);}}}
            float center=0;size_t half=links.size()/2;if(!links.empty()){center=offset[links[half]];if(links.size()%2==0)center=(center+offset[links[half-1]])/2;}
            float label=incoming[index]>=0?(flow.edges[incoming[index]].choices?90.f:72.f):0,own=std::max(flow.nodes[index].width+16,label+24);Profile top{-own/2,own/2},between{32768,-32768};
            for(int edge:links){offset[edge]-=center;top.left=std::min(top.left,offset[edge]-8);top.right=std::max(top.right,offset[edge]+8);float halfLabel=(flow.edges[edge].choices?90.f:72.f)/2+12;between.left=std::min(between.left,offset[edge]-halfLabel);between.right=std::max(between.right,offset[edge]+halfLabel);}
            profile[index].push_back(top);if(!links.empty()){profile[index].push_back(between);for(auto p:merged)profile[index].push_back({p.left-center,p.right-center});}
            leftBound[index]=top.left;rightBound[index]=top.right;for(auto p:profile[index]){leftBound[index]=std::min(leftBound[index],p.left);rightBound[index]=std::max(rightBound[index],p.right);}
        }
        struct Placement{int index;float center,y;};float nextRoot=24,rootY=24;for(int root:roots)rootY=std::max(rootY,24+positions[root].height/2);
        for(int root:roots){std::vector<Placement> tasks{{root,nextRoot-leftBound[root],rootY}};while(!tasks.empty()){auto at=tasks.back();tasks.pop_back();auto& node=positions[at.index];node.x=at.center;node.y=at.y;
                for(int edge:children[at.index])tasks.push_back({flow.edges[edge].to,at.center+offset[edge],at.y+112});}
            nextRoot+=rightBound[root]-leftBound[root]+64;}
        for(int index:activeEdges){const auto& e=flow.edges[index];const auto& from=positions[e.from];const auto& to=positions[e.to];const auto& siblings=children[e.from];const size_t at=size_t(std::find(siblings.begin(),siblings.end(),index)-siblings.begin());
            bool side=from.kind=="save"&&siblings.size()>1&&!(siblings.size()%2&&at==siblings.size()/2);const float sign=at<siblings.size()/2?-1.f:1.f;
            Point start{from.x+(side?sign*from.width/2:0),from.y+(side?0:from.height/2)},target{to.x,to.y-to.height/2};std::vector<Point> controls{start};
            auto addLine=[&](Point end){auto a=controls.back();controls.push_back({a.x+(end.x-a.x)/3,a.y+(end.y-a.y)/3});controls.push_back({a.x+2*(end.x-a.x)/3,a.y+2*(end.y-a.y)/3});controls.push_back(end);};
            if(side){const float radius=std::min({14.f,std::abs(target.x-start.x)*.45f,(target.y-start.y)*.45f}),k=.55228475f;Point entry{target.x-sign*radius,start.y},exit{target.x,start.y+radius};addLine(entry);controls.push_back({entry.x+sign*radius*k,entry.y});controls.push_back({exit.x,exit.y-radius*k});controls.push_back(exit);}
            addLine(target);lines[index]=curve(controls);centers[index]={target.x,(from.y+to.y)/2};
        }
    }
    void fit(bool all=false){zoom=std::clamp(std::min((canvas.w-40*scale)/(graphWidth*scale),all?(canvas.h-40*scale)/(graphHeight*scale):1.25f),.005f,1.25f);panX=(canvas.w-graphWidth*zoom*scale)/2;panY=all?(canvas.h-graphHeight*zoom*scale)/2:20*scale;}
    Point screen(Point p)const{return {canvas.x+panX+(p.x-minX)*zoom*scale,canvas.y+panY+(p.y-minY)*zoom*scale};}
    void zoomAt(float factor,Point p){float old=zoom;zoom=std::clamp(old*factor,.005f,2.5f);panX=(p.x-canvas.x)-(p.x-canvas.x-panX)*zoom/old;panY=(p.y-canvas.y)-(p.y-canvas.y-panY)*zoom/old;autoFit=false;}
    bool wheel(Point p,float delta){if(!canvas.has(p))return false;zoomAt(std::pow(1.15f,std::clamp(delta,-4.f,4.f)),p);return true;}
    bool down(Point p){if(!outer.has(p))return false;dragging=canvas.has(p);moved=false;downAt=p;panAt={panX,panY};for(const auto& hit:hits)if(hit.box.has(p)){dragging=false;break;}return true;}
    bool move(Point p){if(!dragging)return false;panX=panAt.x+p.x-downAt.x;panY=panAt.y+p.y-downAt.y;moved|=std::hypot(p.x-downAt.x,p.y-downAt.y)>4*scale;autoFit=false;return true;}
    void cancel(){dragging=false;moved=false;}
    bool up(Point p,const Document& d){bool changed=dragging;dragging=false;if(moved){moved=false;return true;}
        for(auto it=hits.rbegin();it!=hits.rend();++it)if(it->box.has(p)){
            const auto hit=*it;changed=true;
            switch(hit.action){case 1:selectedNode=hit.index;selectedEdge=-1;break;case 2:selectedEdge=hit.index;selectedNode=-1;break;
            case 3:if(selectedEdge>=0&&selectedEdge<int(d.flow.edges.size())){const auto& e=d.flow.edges[selectedEdge];readRoute=e.route;readStep=e.start;}break;
            case 4:selectedNode=selectedEdge=-1;break;case 5:currentOnly=!currentOnly;selectedNode=selectedEdge=-1;autoFit=true;break;
            case 6:zoomAt(hit.index>0?1.25f:.8f,{canvas.x+canvas.w/2,canvas.y+canvas.h/2});break;
            case 7:fit(hit.index!=0);autoFit=hit.index==0;break;case 8:hideCollections=!hideCollections;selectedNode=selectedEdge=-1;autoFit=true;break;}break;
        }return changed;
    }
};
inline std::string caption(const FlowEdge& e){return e.unlock?"通关后解锁":e.choices?std::to_string(e.choices)+" 个选项":e.count?std::to_string(e.count)+" 个步骤":"继续";}
inline int endingWeight(const FlowNode& n){return n.kind=="collect"?0:n.kind!="ending"&&n.kind!="reference"?2:n.endingType=="heroine"||n.endingType=="true"||n.endingType=="good"?3:n.endingType=="normal"||n.endingType=="bad"?1:n.endingType=="after"||n.endingType=="cg"||n.endingType=="collect"?0:2;}
inline bool secondary(const FlowNode& n){return n.kind=="collect"||(n.kind=="ending"||n.kind=="reference")&&(n.endingType=="after"||n.endingType=="cg"||n.endingType=="collect");}
inline std::string caption(const FlowNode& n){return n.label;}

// Painter provides rect/line/text/clip primitives only; no window/input ownership.
template<class Painter> void render(Painter& p,State& state,const Document& d,Rect area,float scale,bool light,int route,LONG revision){
    const auto& f=d.flow;state.reset(revision,f);state.scale=scale;state.outer=area;state.route=route;state.hits.clear();
    if(!state.initialized){state.currentOnly=area.w/scale<820;state.initialized=true;}
    Color flowInk=light?Color{32,33,36}:Color{242,242,242},flowMuted=light?Color{89,97,108}:Color{182,186,195};
    Color background=light?Color{243,245,248,244}:Color{34,37,44,244},border=light?Color{193,199,208}:Color{70,74,83},control=light?Color{235,238,242}:Color{47,50,57};
    auto text=[&](const std::string& value,Rect box,float size,Color color,bool center=false){p.text(value,box,size*scale,color,center);};
    auto button=[&](const std::string& value,Rect box,int action,int index=0,bool primary=false){p.box(box,primary?routeColor(0,light):control,border,5*scale);text(value,box,12,primary?(light?Color{255,255,255}:Color{20,26,32}):flowInk,true);state.hits.push_back({box,action,index});};
    const float s=scale;const bool twoRows=area.w/s<560;const float bar=(twoRows?60.f:30.f)*s;
    button(state.currentOnly?"当前路线":"全部路线",{area.x,area.y,92*s,24*s},5);
    const bool shortBar=area.w/s<540;float x=area.x+area.w-(shortBar?168.f:250.f)*s;
    button("−",{x,area.y,30*s,24*s},6,-1);button("＋",{x+36*s,area.y,30*s,24*s},6,1);
    button("适应宽度",{x+72*s,area.y,88*s,24*s},7,0);
    if(!shortBar)button("总览",{x+168*s,area.y,82*s,24*s},7,1);
    const float checkX=twoRows?area.x:area.x+106*s,checkY=area.y+(twoRows?30*s:0);
    p.box({checkX+2*s,checkY+6*s,12*s,12*s},state.hideCollections?routeColor(0,light):background,border,2*s);
    if(state.hideCollections){const auto checkInk=light?Color{255,255,255}:Color{20,26,32};p.line({checkX+4*s,checkY+14*s},{checkX+7*s,checkY+17*s},checkInk,1.5f*s);p.line({checkX+7*s,checkY+17*s},{checkX+12*s,checkY+11*s},checkInk,1.5f*s);}
    text("隐藏回收路线",{checkX+22*s,checkY,130*s,24*s},12,flowInk);state.hits.push_back({{checkX,checkY,152*s,24*s},8,0});
    int section=-1;for(const auto& e:f.edges)if(e.route==route&&!e.unlock){section=e.section;break;}
    std::vector<bool> eligible(f.edges.size(),true);
    if(state.hideCollections){std::vector<bool> hasOut(f.nodes.size()),keep(f.nodes.size());for(const auto& e:f.edges)hasOut[e.from]=true;
        for(size_t i=0;i<f.nodes.size();++i)keep[i]=!secondary(f.nodes[i])&&(!hasOut[i]||f.nodes[i].kind=="ending");
        for(size_t pass=0;pass<f.nodes.size();++pass){bool changed=false;for(const auto& e:f.edges)if(keep[e.to]&&!keep[e.from]){keep[e.from]=true;changed=true;}if(!changed)break;}
        for(size_t i=0;i<f.edges.size();++i)eligible[i]=keep[f.edges[i].to];}
    state.activeEdges.clear();std::vector<bool> included(f.nodes.size(),!state.currentOnly&&!state.hideCollections);
    for(int i=0;i<int(f.edges.size());++i)if(eligible[i]&&(!state.currentOnly||f.edges[i].section==section)){state.activeEdges.push_back(i);included[f.edges[i].from]=included[f.edges[i].to]=true;}
    if(state.currentOnly){std::vector<bool> edgeSeen(f.edges.size());for(int i:state.activeEdges)edgeSeen[i]=true;
        for(size_t pass=0;pass<f.nodes.size();++pass){bool added=false;for(int i=0;i<int(f.edges.size());++i)if(eligible[i]&&!edgeSeen[i]&&included[f.edges[i].to]){edgeSeen[i]=true;included[f.edges[i].from]=true;state.activeEdges.push_back(i);added=true;}if(!added)break;}}
    state.activeNodes.clear();for(int i=0;i<int(f.nodes.size());++i)if(included[i])state.activeNodes.push_back(i);
    state.reflow(f,state.currentOnly||state.hideCollections);state.minX=state.minY=32768;float maxX=0,maxY=0;
    for(int i:state.activeNodes){const auto& n=state.positions[i];state.minX=std::min(state.minX,n.x-n.width/2-20);state.minY=std::min(state.minY,n.y-n.height/2-20);maxX=std::max(maxX,n.x+n.width/2+20);maxY=std::max(maxY,n.y+n.height/2+20);}
    state.graphWidth=std::max(1.f,maxX-state.minX);state.graphHeight=std::max(1.f,maxY-state.minY);
    if(state.activeNodes.empty()){state.minX=state.minY=0;state.graphWidth=state.graphHeight=1;}
    bool inspect=state.selectedEdge>=0||state.selectedNode>=0,side=area.w/s>=720;
    Rect chart{area.x,area.y+bar,area.w,area.h-bar},inspector{};
    if(inspect){if(side){inspector={chart.x+chart.w-246*s,chart.y,246*s,chart.h};chart.w-=260*s;}else inspector={chart.x+8*s,chart.y+chart.h-std::min(chart.h,164*s)-8*s,chart.w-16*s,std::min(chart.h,164*s)};}
    if(chart.w!=state.canvas.w||chart.h!=state.canvas.h)state.autoFit=true;state.canvas=chart;
    if(state.autoFit)state.fit();
    if(area.w/s>=800)text(std::to_string(state.activeNodes.size())+" 个节点",{area.x+270*s,area.y,area.w-534*s,28*s},11,flowMuted);
    p.box(chart,background,border,7*s);p.clip(chart);
    auto chartHit=[&](Rect box,int action,int index){const float right=std::min(box.x+box.w,chart.x+chart.w),bottom=std::min(box.y+box.h,chart.y+chart.h);box.x=std::max(box.x,chart.x);box.y=std::max(box.y,chart.y);box.w=right-box.x;box.h=bottom-box.y;if(box.w>0&&box.h>0)state.hits.push_back({box,action,index});};
    const Color grid=light?Color{230,234,240}:Color{42,46,54};
    for(float at=chart.x;at<chart.x+chart.w;at+=20*s)p.line({at,chart.y},{at,chart.y+chart.h},grid,s);
    for(float at=chart.y;at<chart.y+chart.h;at+=20*s)p.line({chart.x,at},{chart.x+chart.w,at},grid,s);
    const float z=state.zoom*s;
    for(int index:state.activeEdges){const auto& e=f.edges[index];const auto& points=state.lines[index];Color color=e.unlock?flowMuted:routeColor(e.section,light);
        std::vector<Point> screenPoints;screenPoints.reserve(points.size());for(auto point:points)screenPoints.push_back(state.screen(point));
        const float thickness=(index==state.selectedEdge?2.6f:1.7f)*z;
        if(e.unlock){float remaining=2*z;const float spacing=7*z,radius=.95f*z;
            // Round dots at fixed arc-length intervals, independent of how the
            // curve was tessellated. Shortened spans retain an even rhythm.
            for(size_t j=1;j<screenPoints.size();++j){const auto a=screenPoints[j-1],b=screenPoints[j];const float length=std::hypot(b.x-a.x,b.y-a.y);if(length<.001f)continue;
                float at=remaining;for(;at<=length;at+=spacing){const float dotX=a.x+(b.x-a.x)*at/length,dotY=a.y+(b.y-a.y)*at/length;p.box({dotX-radius,dotY-radius,2*radius,2*radius},color,color,radius,.4f*z);}remaining=at-length;}}
        else p.polyline(screenPoints,color,thickness);
        if(points.size()>1){size_t previous=points.size()-2;while(previous>0&&std::hypot(points[previous].x-points.back().x,points[previous].y-points.back().y)<.01f)--previous;auto a=state.screen(points[previous]),b=state.screen(points.back());float angle=std::atan2(b.y-a.y,b.x-a.x);p.polygon({b,{b.x-7*z*std::cos(angle-.45f),b.y-7*z*std::sin(angle-.45f)},{b.x-7*z*std::cos(angle+.45f),b.y-7*z*std::sin(angle+.45f)}},color,color);}
        const auto center=state.screen(state.centers[index]);const float labelWidth=e.choices?90.f:72.f,labelHeight=e.choices?28.f:24.f;
        Rect label{center.x-labelWidth*z/2,center.y-labelHeight*z/2,labelWidth*z,labelHeight*z};
        if(!e.unlock){const float tipY=label.y-z;p.polygon({{center.x,tipY},{center.x-3.2f*z,tipY-7*z},{center.x+3.2f*z,tipY-7*z}},color,color);}
        if(e.choices)shape(p,label,background,color,1);else p.box(label,background,color,6*z);
        p.text(caption(e),label,11*z,flowInk,true);chartHit(label,2,index);
    }
    for(int index:state.activeNodes){const auto& n=state.positions[index];auto center=state.screen({n.x,n.y});Rect box{center.x-n.width*z/2,center.y-n.height*z/2,n.width*z,n.height*z};
        int sectionColor=0;for(int edge:state.activeEdges)if(f.edges[edge].to==index&&!f.edges[edge].unlock){sectionColor=f.edges[edge].section;break;}
        Color color=n.kind=="save"?flowMuted:n.kind=="entry"||n.kind=="start"?routeColor(2,light):routeColor(sectionColor,light);
        const int weight=endingWeight(n);const float fraction=weight==3?.20f:weight==2?.12f:weight==1?.08f:.04f;
        auto fill=background;if(n.kind!="save"){fill.r=int(fill.r*(1-fraction)+color.r*fraction);fill.g=int(fill.g*(1-fraction)+color.g*fraction);fill.b=int(fill.b*(1-fraction)+color.b*fraction);}
        if(weight==0){color.a=110;fill=background;}
        shape(p,box,fill,color,n.kind=="save"?2:3,std::max(.7f,(weight==3?1.8f:weight==0?.85f:1.1f)*z));
        Rect labelBox=box;if(n.kind=="save"){labelBox.x+=box.w*.17f;labelBox.w=box.w*.66f;}
        p.text(caption(n),labelBox,(weight==3?14.f:weight==2?13.f:weight==1?12.f:10.5f)*z,weight==0?flowMuted:flowInk,true,weight==3);chartHit(box,1,index);
    }
    p.unclip();
    if(state.activeNodes.empty())text("暂无主路线，取消隐藏可查看回收内容",{chart.x+12*s,chart.y+20*s,chart.w-24*s,40*s},12,flowMuted,true);
    if(inspect){
        p.box(inspector,light?Color{250,250,250}:Color{41,41,41},border,7*s);p.clip(inspector);
        const float px=inspector.x+12*s,w=inspector.w-24*s;float y=inspector.y+10*s;
        button("×",{inspector.x+inspector.w-36*s,y,26*s,26*s},4);
        if(state.selectedEdge>=0){const auto& e=f.edges[state.selectedEdge];const auto& r=d.routes[e.route];
            text(reading::routeName(r),{px,y,w-34*s,26*s},14,flowInk);y+=30*s;
            text(f.nodes[e.from].label+" → "+f.nodes[e.to].label,{px,y,w,24*s},12,flowMuted);y+=28*s;
            button("连续阅读 →",{px,y,w,30*s},3,0,true);y+=40*s;
            for(int i=e.start;i<e.start+std::min(e.count,3);++i){const auto& step=r.steps[i];text(std::to_string(reading::stepNumber(r,i))+" · "+kind(step.kind),{px,y,w,18*s},10,flowMuted);y+=20*s;text(reading::primary(step),{px,y,w,26*s},13,flowInk);y+=34*s;}
            if(e.count>3)text("打开正文阅读完整步骤与其他选项",{px,y,w,24*s},11,flowMuted);
        }else{const auto& n=f.nodes[state.selectedNode];text(caption(n),{px,y,w-34*s,26*s},14,flowInk);y+=34*s;
            for(int edge:state.activeEdges){const auto& e=f.edges[edge];if(e.from!=state.selectedNode&&e.to!=state.selectedNode)continue;
                std::string title=(e.from==state.selectedNode?"前往 ":"来自 ")+f.nodes[e.from==state.selectedNode?e.to:e.from].label+" · "+caption(e);
                button(title,{px,y,w,32*s},2,edge);y+=38*s;if(y+32*s>inspector.y+inspector.h)break;}}
        p.unclip();
    }
    if(f.truncated)text("流程较大，部分节点未绘制；正文保留全部步骤。",{area.x,area.y+area.h-22*s,area.w,22*s},11,flowMuted);
}
}
