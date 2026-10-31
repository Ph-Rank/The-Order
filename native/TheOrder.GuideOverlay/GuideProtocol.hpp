#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <stdexcept>
#include <string>
#include <vector>

namespace guide {
constexpr unsigned magic = 0x31474f54, version = 4, capacity = 16 * 1024 * 1024;
struct Header {
    unsigned magic, version; volatile LONG sequence, length, visible, interactive;
    volatile LONG64 rendererPulse, hostPulse; LONG pid; volatile LONG status; LONG64 created;
    volatile LONG command, argument, route, step, graph, hotkey; volatile LONG mouseSample; LONG selected;
    float opacity, fontSize; LONG flowNodes,flowEdges,flowSelected,flowZoom; LONG ownerPid, readerAck;
    volatile LONG64 lastToggle; volatile LONG renderedFrames, gameFrames, injectResult, renderApi; LONG guideChannel, sourceFlags; BYTE reserved152[24]; volatile LONG channel, everOpened;
    volatile LONG panelWidth, panelHeight, bodyFontPx, visibleSteps, scrollY, showOriginal, readerBackend, lightTheme, dpi;
    volatile LONG readingWidth, navigationWidth, indexWidth, overflowRows, rowPitch, visibleStage, readingFirst, layoutMode, fontRoles;
};
static_assert(sizeof(Header) == 256 && offsetof(Header, lastToggle) == 120);
static_assert(offsetof(Header,panelWidth)==184 && offsetof(Header,readerBackend)==208);
inline int shortcutKey(int shortcut){return shortcut&255;}
inline UINT shortcutModifiers(int shortcut){return UINT(shortcut>>16)&7U;}
inline UINT activeModifiers(){return ((GetAsyncKeyState(VK_MENU)&0x8000)?MOD_ALT:0U)
    |((GetAsyncKeyState(VK_CONTROL)&0x8000)?MOD_CONTROL:0U)|((GetAsyncKeyState(VK_SHIFT)&0x8000)?MOD_SHIFT:0U)
    |(((GetAsyncKeyState(VK_LWIN)|GetAsyncKeyState(VK_RWIN))&0x8000)?MOD_WIN:0U);}
inline bool shortcutMatches(int shortcut,UINT modifiers){return shortcutModifiers(shortcut)==modifiers;}
// The companion samples outside the game's input hooks. Tick and left-button
// state share the former reserved field; zero means no companion observation.
inline LONG mouseSample(bool down,ULONGLONG tick){return LONG(((DWORD(tick)&0x3fffffffU)<<1)|(down?1U:0U));}
inline bool mouseReleasedAfter(LONG sample,ULONGLONG started,ULONGLONG now){
    if(!sample||(sample&1))return false;const DWORD mask=0x3fffffffU,tick=DWORD(sample)>>1;
    const DWORD after=(tick-DWORD(started))&mask;
    return ((DWORD(now)-tick)&mask)<400&&after>0&&after<0x20000000U;
}
struct Alternative { std::string original, display; bool machine = false; };
struct Step { std::string kind, original, display; bool machine = false; std::string saveKey;
    bool achievement = false, heading = false; std::vector<Alternative> alternatives; };
struct Route { std::string key, title, from, to; std::vector<Step> steps; };
struct Point {float x=0,y=0;};
struct FlowNode {std::string kind,label,endingType;float x,y,width,height;};
struct FlowEdge {int from,to,route,start,count,choices,section;bool unlock;std::vector<Point> points;Point label;};
struct Flow {float width=0,height=0;bool truncated=false;std::vector<FlowNode> nodes;std::vector<FlowEdge> edges;};
struct ReadingBlock {std::string kind,text;std::vector<std::vector<std::string>> rows;};
struct Reading {bool present=false,network=false,guides=false,notes=false;int channel=0,file=0,page=0,pages=0,width=0,height=0;float progress=0,zoom=1;std::string notice,label;std::vector<std::string> files;std::vector<ReadingBlock> blocks;std::vector<BYTE> rgba;};
struct Document { std::string title, attribution, status; std::vector<std::string> sources; std::vector<Route> routes; Flow flow; Reading reading; };
struct Cursor {
    const BYTE* data; size_t size, offset = 0;
    int number() { if (offset + 4 > size) throw std::runtime_error("wire"); int n; memcpy(&n, data + offset, 4); offset += 4; return n; }
    int count(int maximum) { int n = number(); if (n < 0 || n > maximum) throw std::runtime_error("count"); return n; }
    float real() {int bits=number();float value;memcpy(&value,&bits,4);if(!std::isfinite(value)||value<0||value>32768)throw std::runtime_error("geometry");return value;}
    std::string text() {
        const int n = count(131072); if (offset + n > size) throw std::runtime_error("text");
        std::string value(reinterpret_cast<const char*>(data + offset), n); offset += n;
        if (value.find('\0') != std::string::npos || (n && MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), n, nullptr, 0) == 0)) throw std::runtime_error("utf8");
        return value;
    }
};
inline Document decode(const std::vector<BYTE>& data) {
    Cursor c{data.data(), data.size()}; Document d; d.title = c.text(); d.attribution = c.text(); d.status = c.text();
    for (int n = c.count(128); n--;) d.sources.push_back(c.text());
    int total = 0;
    for (int n = c.count(2048); n--;) {
        Route r; r.key = c.text(); r.title = c.text(); r.from = c.text(); r.to = c.text(); int steps = c.count(16384);
        if ((total += steps) > 16384) throw std::runtime_error("steps");
        while (steps--) { Step s; s.kind = c.text(); s.original = c.text(); s.display = c.text(); s.machine = c.count(1) != 0;
            s.saveKey = c.text(); s.achievement = c.count(1) != 0; s.heading = c.count(1) != 0;
            for (int remaining = c.count(128); remaining--;) { Alternative a; a.original = c.text(); a.display = c.text(); a.machine = c.count(1) != 0; s.alternatives.push_back(std::move(a)); }
            r.steps.push_back(std::move(s)); }
        d.routes.push_back(std::move(r));
    }
    if(c.count(1)) {
        auto& flow=d.flow;flow.width=c.real();flow.height=c.real();flow.truncated=c.count(1)!=0;
        for(int n=c.count(512);n--;) {FlowNode node;node.kind=c.text();node.label=c.text();node.endingType=c.text();node.x=c.real();node.y=c.real();node.width=c.real();node.height=c.real();flow.nodes.push_back(std::move(node));}
        int points=0;
        for(int n=c.count(768);n--;) {
            FlowEdge e;e.from=c.count(511);e.to=c.count(511);e.route=c.count(2047);e.start=c.count(16384);e.count=c.count(16384);e.choices=c.count(16384);e.section=c.count(511);e.unlock=c.count(1)!=0;
            if(e.from>=int(flow.nodes.size())||e.to>=int(flow.nodes.size())||e.route>=int(d.routes.size())||e.start>int(d.routes[e.route].steps.size())-e.count||e.choices>e.count)throw std::runtime_error("flow reference");
            int length=c.count(1024);if(length<4||(length-1)%3!=0||(points+=length)>32768)throw std::runtime_error("flow points");
            while(length--)e.points.push_back({c.real(),c.real()});e.label={c.real(),c.real()};flow.edges.push_back(std::move(e));
        }
    }
    if(c.count(1)) {
        auto& r=d.reading;r.present=true;r.channel=c.count(2);r.network=c.count(1)!=0;r.guides=c.count(1)!=0;r.notes=c.count(1)!=0;
        for(int n=c.count(128);n--;)r.files.push_back(c.text());r.file=c.count(127);r.page=c.count(2000);r.pages=c.count(2000);r.notice=c.text();r.label=c.text();
        for(int n=c.count(2048);n--;) {ReadingBlock b;b.kind=c.text();b.text=c.text();for(int rows=c.count(128);rows--;){std::vector<std::string> row;for(int columns=c.count(128);columns--;)row.push_back(c.text());b.rows.push_back(std::move(row));}r.blocks.push_back(std::move(b));}
        r.width=c.count(4096);r.height=c.count(4096);int size=c.count(12000000);
        if(int64_t(r.width)*r.height>3000000||size!=int64_t(r.width)*r.height*4||c.offset+size>c.size)throw std::runtime_error("pixels");
        r.rgba.assign(c.data+c.offset,c.data+c.offset+size);c.offset+=size;
        r.progress=c.real();r.zoom=c.real();if(r.progress>1||r.zoom<.6f||r.zoom>4)throw std::runtime_error("position");
    }
    if (c.offset != c.size) throw std::runtime_error("trailing"); return d;
}
inline bool validName(const std::wstring& name) {
    const std::wstring prefix = L"Local\\TheOrder.Guide.";
    return name.size() == prefix.size() + 32 && name.compare(0, prefix.size(), prefix) == 0
        && std::all_of(name.begin() + prefix.size(), name.end(), [](wchar_t c) { return (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f'); });
}
inline LONG64 creation(HANDLE process) { FILETIME c{}, e{}, k{}, u{}; return GetProcessTimes(process, &c, &e, &k, &u) ? (LONG64(c.dwHighDateTime) << 32) | c.dwLowDateTime : 0; }
class Connection {
    HANDLE mapping = nullptr; BYTE* memory = nullptr; LONG seen = -1;
public:
    Header* header = nullptr; Document document;
    Connection() = default; Connection(const Connection&) = delete;
    bool open(const std::wstring& name) {
        if (!validName(name)) return false;
        mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name.c_str()); if (!mapping) return false;
        memory = static_cast<BYTE*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, capacity));
        if (!memory) { CloseHandle(mapping); mapping = nullptr; return false; }
        header = reinterpret_cast<Header*>(memory); return header->magic == magic && header->version == version;
    }
    bool live() const { const auto now = static_cast<LONG64>(GetTickCount64()); return header && header->hostPulse > 0 && now >= header->hostPulse && now - header->hostPulse < 5000; }
    bool refresh() {
        if (!live()) return false;
        LONG seq = header->sequence; if (seq == seen || (seq & 1)) return true;
        MemoryBarrier(); LONG length = header->length; if (length < 0 || length > LONG(capacity - sizeof(Header))) return false;
        std::vector<BYTE> data(length); memcpy(data.data(), memory + sizeof(Header), length); MemoryBarrier();
        if (seq != header->sequence || (header->sequence & 1)) return true;
        try { document = decode(data); seen = seq; } catch (...) { return false; }
        return true;
    }
    void toggle() {
        if (!header) return;
        LONG64 now = static_cast<LONG64>(GetTickCount64()), previous = header->lastToggle;
        if (now - previous < 220 || InterlockedCompareExchange64(&header->lastToggle, now, previous) != previous) return;
        InterlockedXor(&header->visible, 1); InterlockedExchange(&header->interactive, header->visible ? 1 : 0);
        if(header->visible)InterlockedExchange(&header->everOpened,1);
    }
    bool request(int command,int argument){if(!header||header->command)return false;header->argument=argument;MemoryBarrier();InterlockedExchange(&header->command,command);return true;}
    bool action(int index) {
        if (!header || (index != 1 && index != 2 && index != 3)) return false;
        auto* stamp = reinterpret_cast<volatile LONG64*>(reinterpret_cast<BYTE*>(header) + 144 + index * 8);
        LONG64 now = static_cast<LONG64>(GetTickCount64()), previous = *stamp;
        return now - previous >= 220 && InterlockedCompareExchange64(stamp, now, previous) == previous;
    }
    void close() { if (memory) UnmapViewOfFile(memory); if (mapping) CloseHandle(mapping); memory = nullptr; mapping = nullptr; header = nullptr; }
    ~Connection() { close(); }
};
inline const char* kind(const std::string& k) { return k == "save" ? "存档" : k == "load" ? "读档" : k == "choice" ? "选择" : k == "action" ? "操作" : "提示"; }
}
