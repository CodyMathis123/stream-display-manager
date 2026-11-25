#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <string>
#include <cstdint>
#include <algorithm>

namespace sdm {

enum class TailStatus { Unavailable, Attached, NoChange, Lines, Gap };
struct TailResult {
    TailStatus status=TailStatus::Unavailable;
    const char* reason="unavailable";
};

// Opens only for each poll. Tracks file identity, not just length, since
// Steam can replace streaming_log.txt with another larger file.
// New/replaced/truncated logs start at EOF: old lines cannot authorize a stream.
class SteamTail {
public:
    template<class OnLine>
    TailResult Poll(const std::filesystem::path& path, OnLine&& onLine) {
        if(path.empty()) {
            Reset();
            return {TailStatus::Unavailable,"path-unavailable"};
        }
        HANDLE h=CreateFileW(path.c_str(),GENERIC_READ,
            FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,
            nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
        if(h==INVALID_HANDLE_VALUE) {
            Reset();
            return {TailStatus::Unavailable,"log-unavailable"};
        }
        BY_HANDLE_FILE_INFORMATION info{};
        LARGE_INTEGER size{};
        if(!GetFileInformationByHandle(h,&info) || !GetFileSizeEx(h,&size) || size.QuadPart<0) {
            CloseHandle(h);
            Reset();
            return {TailStatus::Gap,"log-stat-failed"};
        }
        const std::uint64_t fileSize=static_cast<std::uint64_t>(size.QuadPart);
        if(!attached_) {
            volume_=info.dwVolumeSerialNumber;
            indexHi_=info.nFileIndexHigh;indexLo_=info.nFileIndexLow;
            position_=fileSize;
            attached_=true;pending_.clear();
            CloseHandle(h);
            return {TailStatus::Attached,"attached-at-eof"};
        }
        if(volume_!=info.dwVolumeSerialNumber ||
           indexHi_!=info.nFileIndexHigh || indexLo_!=info.nFileIndexLow ||
           fileSize<position_) {
            volume_=info.dwVolumeSerialNumber;
            indexHi_=info.nFileIndexHigh;indexLo_=info.nFileIndexLow;
            position_=fileSize;pending_.clear();
            CloseHandle(h);
            return {TailStatus::Gap,"replaced-or-truncated"};
        }
        std::uint64_t unread=fileSize-position_;
        if(unread>256*1024) {
            position_=fileSize;pending_.clear();
            CloseHandle(h);
            return {TailStatus::Gap,"read-limit"};
        }
        if(unread==0) {
            CloseHandle(h);
            return {TailStatus::NoChange,"unchanged"};
        }
        LARGE_INTEGER offset{};offset.QuadPart=static_cast<LONGLONG>(position_);
        if(!SetFilePointerEx(h,offset,nullptr,FILE_BEGIN)) {
            CloseHandle(h);Reset();
            return {TailStatus::Gap,"seek-failed"};
        }
        std::string data(static_cast<size_t>(unread),'\0');
        DWORD read=0;
        if(!ReadFile(h,data.data(),static_cast<DWORD>(unread),&read,nullptr) ||
           read!=static_cast<DWORD>(unread)) {
            CloseHandle(h);Reset();
            return {TailStatus::Gap,"read-failed"};
        }
        CloseHandle(h);
        position_+=read;
        pending_+=data;
        if(pending_.size()>256*1024+16384) {
            pending_.clear();
            return {TailStatus::Gap,"oversized-buffer"};
        }
        std::size_t start=0, end=0;
        while((end=pending_.find('\n',start))!=std::string::npos) {
            if(end-start>16384) {
                pending_.clear();
                return {TailStatus::Gap,"oversized-line"};
            }
            onLine(pending_.substr(start,end-start));
            start=end+1;
        }
        pending_.erase(0,start);
        if(pending_.size()>16384) {
            pending_.clear();
            return {TailStatus::Gap,"oversized-partial-line"};
        }
        return {TailStatus::Lines,"lines-read"};
    }
    bool Attached() const { return attached_; }
    void Reset() {
        attached_=false;position_=0;pending_.clear();
        volume_=indexHi_=indexLo_=0;
    }
private:
    bool attached_=false;
    DWORD volume_=0,indexHi_=0,indexLo_=0;
    std::uint64_t position_=0;
    std::string pending_;
};
} // namespace sdm
