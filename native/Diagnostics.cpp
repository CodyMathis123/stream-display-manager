// Bounded user-local JSONL storage and on-demand, readable export.
// No Win32 Event Log source, background worker, third-party compression or DB.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <commdlg.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <deque>
#include <algorithm>
#include <map>
#include <chrono>
#include <cstdio>
#include <stdexcept>
#include "Diagnostics.h"
#pragma comment(lib,"comdlg32.lib")

namespace sdm {
namespace {
constexpr std::uintmax_t SegmentLimit=2*1024*1024;
constexpr std::uintmax_t RetentionLimit=16*1024*1024;
constexpr auto Retention=std::chrono::hours(24*14);
constexpr auto ExportWindow=std::chrono::hours(24*7);
constexpr std::uintmax_t ExportLimit=8*1024*1024;

bool IsCurrentLog(const std::filesystem::path& p) {
    const auto n=p.filename().wstring();
    return n.starts_with(L"native-events-") && n.ends_with(L".jsonl") &&
           n.size()>=28;
}
std::wstring DateUtc() {
    SYSTEMTIME now{};GetSystemTime(&now);
    wchar_t day[20]{};
    swprintf_s(day,L"%04u-%02u-%02u",now.wYear,now.wMonth,now.wDay);
    return day;
}
std::string TimeUtc() {
    SYSTEMTIME now{};GetSystemTime(&now);
    char stamp[40]{};
    sprintf_s(stamp,"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ",
              now.wYear,now.wMonth,now.wDay,now.wHour,now.wMinute,
              now.wSecond,now.wMilliseconds);
    return stamp;
}
std::filesystem::path CurrentSegment(const std::filesystem::path& dir) {
    const std::wstring base=L"native-events-"+DateUtc();
    std::error_code ec;
    for(unsigned n=0;n<1000;++n){
        wchar_t suffix[24]{};
        if(n)swprintf_s(suffix,L"-%03u",n);
        auto p=dir/(base+suffix+L".jsonl");
        auto size=std::filesystem::file_size(p,ec);
        if(ec || size<SegmentLimit)return p;
    }
    return dir/(base+L"-overflow.jsonl"); // safety: never drop recovery events
}
std::string Escape(const std::string& s){
    std::string out;
    for(unsigned char ch:s){
        if(ch=='"' || ch=='\\'){out+='\\';out+=static_cast<char>(ch);}
        else if(ch=='\n')out+="\\n";
        else if(ch=='\r')out+="\\r";
        else if(ch=='\t')out+="\\t";
        else if(ch<32)out+='?';
        else out+=static_cast<char>(ch);
    }
    return out;
}
std::string Severity(const std::string& type,const std::string& reason) {
    if(reason=="failed" || reason=="write-failed" ||
       reason=="rename-failed" || reason=="registry-open-failed" ||
       reason=="registry-write-failed" || reason=="pending-retry" ||
       reason=="startup-recovery-pending")return "error";
    if(type=="steam_log_gap" || reason=="unavailable")return "warning";
    return "info";
}
std::string Field(const std::string& line,const std::string& key) {
    // Intentionally only interpret our own flat JSONL string metadata.
    // Ignore untrusted/legacy/unrecognized events rather than deserialize raw logs.
    const auto match="\""+key+"\":\"";
    const auto start=line.find(match);
    if(start==std::string::npos)return {};
    std::string value;
    for(auto pos=start+match.size();pos<line.size();++pos){
        auto ch=line[pos];
        if(ch=='"')return value;
        if(ch=='\\' && pos+1<line.size()){
            ch=line[++pos];
            if(ch=='n')ch='\n';
            else if(ch=='r')ch='\r';
            else if(ch=='t')ch='\t';
        }
        value+=ch;
    }
    return {};
}
struct Entry {std::filesystem::path path;std::filesystem::file_time_type time;std::uintmax_t size;};
std::vector<Entry> Files(const std::filesystem::path& dir) {
    std::vector<Entry> found;
    std::error_code ec;
    for(std::filesystem::directory_iterator it(dir,ec),end;!ec && it!=end;it.increment(ec)){
        auto p=it->path();
        if(!IsCurrentLog(p) || !it->is_regular_file(ec))continue;
        auto size=it->file_size(ec);if(ec)break;
        auto last=it->last_write_time(ec);if(ec)break;
        found.push_back({p,last,size});
    }
    std::sort(found.begin(),found.end(),[](const Entry& a,const Entry& b){
        if(a.time==b.time)return a.path<b.path;
        return a.time<b.time;
    });
    return found;
}
} // namespace

void PruneDiagnostics(const std::filesystem::path& dir) {
    std::error_code ec;
    const auto current=CurrentSegment(dir);
    const auto cutoff=std::filesystem::file_time_type::clock::now()-Retention;
    auto files=Files(dir);
    std::uintmax_t total=0;
    for(const auto& file:files) {
        if(file.path!=current && file.time<cutoff)
            std::filesystem::remove(file.path,ec);
        else total+=file.size;
    }
    // Delete the oldest completed segments until under budget. Never delete
    // the active segment even during an unusually verbose session.
    for(const auto& file:files) {
        if(total<=RetentionLimit)break;
        if(file.path==current || !std::filesystem::exists(file.path,ec))continue;
        std::filesystem::remove(file.path,ec);
        if(!ec)total-=total<file.size?total:file.size;
    }
}
void AppendDiagnostic(const std::filesystem::path& dir,const std::string& type,
                      const std::string& reason,const std::string& details) {
    try {
        std::error_code ec;
        std::filesystem::create_directories(dir,ec);
        if(ec)return; // Logging cannot prevent display recovery.
        auto filePath=CurrentSegment(dir);
        std::ofstream file(filePath,std::ios::binary|std::ios::app);
        if(!file)return;
        file<<"{\"atUtc\":\""<<TimeUtc()<<"\",\"type\":\""<<Escape(type)
            <<"\",\"reason\":\""<<Escape(reason)
            <<"\",\"severity\":\""<<Severity(type,reason)<<"\"";
        if(!details.empty())file<<","<<details;
        file<<"}\n";
        file.close();
        static unsigned calls=0;
        static std::wstring lastDay;
        auto today=DateUtc();
        if(lastDay!=today || ++calls>=128){
            lastDay=today;calls=0;
            PruneDiagnostics(dir);
        }
    }catch(const std::exception&){ /* Logging must not affect CCD recovery. */ }
}

bool ExportDiagnostics(HWND owner,const std::filesystem::path& dir,
                       const std::wstring& version) {
    const int choice=MessageBoxW(owner,
        L"Export up to seven days of Stream Display Manager diagnostics?\n\n"
        L"The export includes session timestamps, Steam App IDs, display modes, "
        L"device descriptions and event details. Review it before sharing.\n\n"
        L"Raw Steam streaming logs and Windows account configuration are NOT included.",
        L"Export diagnostics",MB_OKCANCEL|MB_ICONINFORMATION);
    if(choice!=IDOK)return false;
    wchar_t filename[MAX_PATH]=L"StreamDisplayManager-diagnostics.txt";
    const wchar_t filter[]=L"Text diagnostics (*.txt)\0*.txt\0All files (*.*)\0*.*\0";
    OPENFILENAMEW picker{};
    picker.lStructSize=sizeof(picker);
    picker.hwndOwner=owner;
    picker.lpstrFile=filename;
    picker.nMaxFile=MAX_PATH;
    picker.lpstrFilter=filter;
    picker.nFilterIndex=1;
    picker.lpstrDefExt=L"txt";
    picker.Flags=OFN_OVERWRITEPROMPT|OFN_PATHMUSTEXIST;
    if(!GetSaveFileNameW(&picker))return false;

    // Read only our application's bounded local JSONL. The original legacy
    // native-events.jsonl is preserved untouched for manual inspection.
    auto files=Files(dir);
    const auto cutoff=std::filesystem::file_time_type::clock::now()-ExportWindow;
    struct Session {std::string first,last,game;unsigned events=0,errors=0;};
    std::map<std::string,Session> sessions;
    std::deque<std::string> lines;
    std::uintmax_t total=0;
    bool truncated=false;
    try {
        for(const auto& f:files){
            if(f.time<cutoff)continue;
            std::ifstream stream(f.path,std::ios::binary);
            std::string line;
            while(std::getline(stream,line)){
                if(line.size()>128*1024){truncated=true;continue;}
                total+=line.size()+1;
                lines.push_back(std::move(line));
                // Keep the most recent events, not the oldest 8 MiB.
                while(total>ExportLimit && !lines.empty()){
                    total-=lines.front().size()+1;
                    lines.pop_front();
                    truncated=true;
                }
            }
        }
        for(const auto& event:lines){
            auto id=Field(event,"sessionId");
            if(id.empty())continue;
            auto& session=sessions[id];
            auto time=Field(event,"atUtc");
            if(session.first.empty())session.first=time;
            session.last=time;
            ++session.events;
            if(Field(event,"severity")=="error")++session.errors;
            const auto marker=std::string("\"appId\":");
            auto pos=event.find(marker);
            if(pos!=std::string::npos){
                pos+=marker.size();
                std::string app;
                while(pos<event.size() && event[pos]>='0' && event[pos]<='9')
                    app+=event[pos++];
                if(!app.empty())session.game=app;
            }
        }
        std::ofstream out(filename,std::ios::binary|std::ios::trunc);
        if(!out)throw std::runtime_error("Unable to create export");
        out<<"Stream Display Manager - diagnostics export\n"
           <<"Version: ";
        // Encode the version as ASCII digits; no private machine paths.
        for(auto ch:version)if((ch>='0'&&ch<='9')||ch=='.')out<<static_cast<char>(ch);
        out<<"\nExported UTC: "<<TimeUtc()<<"\n"
           <<"Scope: last seven days, up to 8 MiB\n"
           <<"Source: app-generated sanitized JSONL (not raw Steam logs)\n"
           <<"Review device metadata and game App IDs before sharing.\n\n"
           <<"SESSIONS ("<<sessions.size()<<")\n";
        if(sessions.empty())out<<"No session-tagged events in retained logs.\n";
        for(const auto& [id,item]:sessions){
            out<<id<<" | "<<item.first<<" to "<<item.last
               <<" | Steam App ID: "<<(item.game.empty()?"unknown":item.game)
               <<" | events: "<<item.events<<" | errors: "<<item.errors<<"\n";
        }
        out<<"\nEVENTS ("<<lines.size()<<")\n";
        for(const auto& line:lines)out<<line<<"\n";
        if(truncated)out<<"\n[Export truncated at 8 MiB; oldest events omitted.]\n";
        out.close();
        if(!out)throw std::runtime_error("Failed to write export");
        MessageBoxW(owner,L"Diagnostic export saved.",L"Stream Display Manager",
                    MB_OK|MB_ICONINFORMATION);
        return true;
    }catch(const std::exception&){
        MessageBoxW(owner,L"Could not export diagnostics to the selected file.",
                    L"Stream Display Manager",MB_OK|MB_ICONERROR);
        return false;
    }
}
} // namespace sdm
