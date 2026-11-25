#pragma once
#include <windows.h>
#include <filesystem>
#include <string>
#include <vector>
#include "DisplayTopology.h"

namespace sdm {
// The tray passes its local data paths explicitly: helpers do not own state.
std::string Utf8(const std::wstring& value);
std::string Json(const std::string& value);
void Log(const std::filesystem::path& logdir,const std::string& type,
         const std::string& reason,const std::string& details="");
std::filesystem::path LocalFolder();
bool ReadSettings(const std::filesystem::path& settings);
void SaveSettings(const std::filesystem::path& settings,bool enabled,
                  const std::filesystem::path& logdir);
bool StartupEnabled();
bool SetStartup(bool enable,const std::filesystem::path& logdir);
std::wstring Signature(const std::vector<Screen>& screens);
void CaptureModes(const std::filesystem::path& logdir,
                  const std::vector<Screen>& screens,const char* reason);
std::wstring InstalledFileVersion();
void ShowAbout(HWND owner);
} // namespace sdm
