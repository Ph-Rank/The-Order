#pragma once
#include <string_view>

namespace guide::chat {
enum Style { bold = 1, italic = 2, code = 4, link = 8, strike = 16, heading = 32 };
struct Span { size_t offset, length; int style; };
struct Message { std::string role, text, meta; std::vector<Span> styles; };

inline bool utf8Boundary(std::string_view text, size_t index) {
    return index == text.size() || (static_cast<unsigned char>(text[index]) & 0xc0) != 0x80;
}

struct TextRun {
    size_t offset, length;
    float x, y, width;
    int style;
};
struct TextLayout { float height = 0; std::vector<TextRun> runs; };

inline float runSize(float size, int style) {
    if (style & heading) return size * 1.18f;
    return style & code ? size * .95f : size;
}

template<class P>
TextLayout layoutText(P& painter, const Message& message, float width, float size) {
    TextLayout layout;
    float x = 0, y = 0;
    const float lineHeight = size * 1.60f;
    std::string_view text(message.text);
    auto newLine = [&] { x = 0; y += lineHeight; };

    auto appendRange = [&](size_t start, size_t length, int style) {
        const float font = runSize(size, style);
        size_t end = start + length;
        while (start < end) {
            if (text[start] == '\r') { ++start; continue; }
            if (text[start] == '\n') {
                if (x > 0) newLine();
                else y += lineHeight * .65f;
                ++start;
                continue;
            }
            size_t stop = std::min(end, text.find_first_of("\r\n", start));
            while (start < stop) {
                // GDI and ImGui both measure this run in its drawing font.
                // Bound the measuring work for long URLs, code and CJK prose.
                std::vector<size_t> cuts{start};
                while (cuts.back() < stop && cuts.size() < 513) {
                    size_t next = cuts.back() + 1;
                    while (next < stop && !utf8Boundary(text, next)) ++next;
                    cuts.push_back(next);
                }
                auto measure = [&](size_t index) {
                    return painter.measureChatRun(text.substr(start, cuts[index] - start), font, style);
                };
                size_t low = 0, high = cuts.size() - 1;
                if (measure(high) <= width - x) low = high;
                else while (low < high) {
                    size_t middle = (low + high + 1) / 2;
                    if (measure(middle) <= width - x) low = middle;
                    else high = middle - 1;
                }

                if (low == 0 && x > 0) { newLine(); continue; }
                size_t fit = cuts[std::max<size_t>(1, low)];
                bool wraps = low < cuts.size() - 1 && fit < stop;
                if (wraps && !(style & code)) {
                    size_t soft = text.find_last_of(" \t", fit - 1);
                    if (soft != std::string::npos && soft > start && fit - soft <= 28) fit = soft + 1;
                    else if (x > 0 && text[start] > 0 && text[start] < 0x7f) {
                        size_t nextSpace = text.find_first_of(" \t", start);
                        size_t wordEnd = std::min(nextSpace, stop);
                        if (wordEnd - start <= 64 && painter.measureChatRun(text.substr(start, wordEnd - start), font, style) <= width) {
                            newLine();
                            continue;
                        }
                    }
                }
                float used = painter.measureChatRun(text.substr(start, fit - start), font, style);
                layout.runs.push_back({start, fit - start, x, y, used, style});
                start = fit; x += used;
                if (wraps) {
                    while (start < stop && text[start] == ' ' && !(style & code)) ++start;
                    newLine();
                }
            }
        }
    };

    size_t start = 0;
    for (const auto& span : message.styles) {
        appendRange(start, span.offset - start, 0);
        appendRange(span.offset, span.length, span.style);
        start = span.offset + span.length;
    }
    appendRange(start, text.size() - start, 0);
    layout.height = y + lineHeight;
    return layout;
}

template<class P>
void drawTextLayout(P& painter, const Message& message, const TextLayout& layout, float x, float y, float size,
    flow::Color ink, flow::Color reference, flow::Color codeBackground, flow::Rect viewport) {
    for (const auto& run : layout.runs) {
        float rowY = y + run.y;
        if (rowY + size * 1.7f < viewport.y) continue;
        if (rowY > viewport.y + viewport.h) break;
        const float font = runSize(size, run.style);
        float top = rowY + (size * 1.6f - font * 1.28f) * .5f;
        if (run.style & code && run.width >= 3)
            painter.box({x + run.x, top, run.width, font * 1.28f}, codeBackground, flow::Color{0,0,0,0}, std::min(3.f, run.width / 2));
        painter.drawChatRun(std::string_view(message.text).substr(run.offset, run.length), x + run.x, top, font,
            run.style & link ? reference : ink, run.style);
        if (run.style & strike)
            painter.line({x + run.x, top + font * .68f}, {x + run.x + run.width, top + font * .68f}, ink, std::max(1.f, font * .055f));
    }
}
}
