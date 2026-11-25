#pragma once
#include <windows.h>
#include <filesystem>
#include <string>

namespace sdm {
// Local per-user JSONL; no Windows Event Log registration or new service.
// Retain at most 14 days / 16 MiB, with daily 2 MiB segments.
void AppendDiagnostic(const std::filesystem::path& logdir,const std::string& type,
                      const std::string& reason,const std::string& details);
void PruneDiagnostics(const std::filesystem::path& logdir);
// User-initiated native Save dialog; one readable text file with session summary
// and sanitized JSONL. Returns false on cancellation or write failure.
bool ExportDiagnostics(HWND owner,const std::filesystem::path& logdir,
                       const std::wstring& version);
} // namespace sdm
