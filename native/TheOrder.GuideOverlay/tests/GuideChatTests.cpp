#include "../GuideChat.hpp"
#include <cstdio>
#pragma comment(lib,"user32.lib")
static int checks=0;
static void require(bool ok,const char* name){++checks;if(!ok)throw std::runtime_error(name);}
static std::vector<BYTE> data;
static void number(int n){auto* p=reinterpret_cast<BYTE*>(&n);data.insert(data.end(),p,p+4);}
static void text(const char* value){number(int(strlen(value)));data.insert(data.end(),value,value+strlen(value));}
int main(){
    try{
        using namespace guide::chat;
        require(sizeof(Header)==256,"header size");require(offsetof(Header,focus)==40&&offsetof(Header,captureRequest)==48,"input offsets");require(offsetof(Header,captureWriter)==128&&offsetof(Header,caret)==132&&offsetof(Header,editorSequence)==144,"capture/editor offsets");require(stateOffset==33024&&pixelOffset==stateOffset+stateBytes,"region offsets");
        text("");text("model");text("high");number(0);number(1);number(1);number(1);number(1);text("Model");number(1);text("assistant");text("引用 · 人間は考える葦である");text("");
        auto decoded=decode(data);require(decoded.image&&decoded.search&&decoded.spoiler&&!decoded.busy,"boolean state");require(decoded.messages.size()==1&&decoded.messages[0].text.find("人間")!=std::string::npos,"utf8 reply");
        data.push_back(0);bool rejected=false;try{decode(data);}catch(...){rejected=true;}require(rejected,"reject trailing bytes");data.pop_back();data[0]=255;rejected=false;try{decode(data);}catch(...){rejected=true;}require(rejected,"reject malformed length");
        std::wstring base=L"Local\\TheOrder.Guide.0123456789abcdef0123456789abcdef";
        HANDLE map=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,capacity,(base+L".chat").c_str());require(map&&GetLastError()!=ERROR_ALREADY_EXISTS,"unique mapping");auto* memory=static_cast<BYTE*>(MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,capacity));require(memory!=nullptr,"map view");
        auto* header=reinterpret_cast<Header*>(memory);header->magic=magic;header->version=version;header->pid=12;header->created=34;header->owner=56;
        {Connection c;require(c.open(base),"connect");guide::Header main{};main.pid=12;main.created=34;main.ownerPid=56;require(c.valid(&main),"identity match");main.created++;require(!c.valid(&main),"pid generation mismatch");
        c.draft("中文 J / current scene");require(c.draft()=="中文 J / current scene","draft roundtrip");header->draftSequence|=1;require(c.draft().empty(),"reject torn draft");header->draftSequence++;
        require(c.request(1,0,"这句话有什么引用吗"),"send");require(header->request==1&&header->command==1&&header->inputLength>0,"request publish");require(!c.request(1),"no overwrite before ack");header->ack=header->request;require(c.request(2),"stop request");require(header->command==2,"stop kind");
        header->ack=header->request;require(!c.request(1,0,std::string(inputBytes,'a')),"bounded request");
        BYTE pixels[]={3,2,1,255,9,8,7,255};header->captureRequest=1;c.frame(1,pixels,2,1,8,true);require(header->captureAck==1&&header->captureStatus==1,"frame ack");require(header->pixelLength==8&&header->width==2&&header->height==1,"frame dimensions");require(memory[pixelOffset]==1&&memory[pixelOffset+2]==3&&memory[pixelOffset+4]==7,"bgra conversion");require(header->captureWriter==0,"frame lock released");
        header->captureRequest=2;c.frame(1,pixels,2,1,8,true);require(header->captureAck==1,"reject stale capture");c.failFrame(2);require(header->captureAck==2&&header->captureStatus==-1,"failure distinguished");
        Canvas canvas;canvas.body={0,0,100,100};canvas.maximum=200;canvas.scale=1;require(canvas.wheel({30,30},-1)&&canvas.scroll==75,"conversation scroll");require(!canvas.wheel({120,30},-1),"scroll stays in pane");canvas.modelsOpen=true;canvas.wheel({30,30},-2);require(canvas.modelFirst==4,"model menu scroll");
        canvas.hits.push_back({{0,0,30,30},12,0});require(canvas.click({5,5},c)&&header->focus==1,"composer focus");}
        UnmapViewOfFile(memory);CloseHandle(map);std::printf("{\"passed\":%d,\"failed\":0,\"version\":%ld}\n",checks,version);return 0;
    }catch(const std::exception& e){std::fprintf(stderr,"FAILED: %s (%d checks)\n",e.what(),checks);return 1;}
}
