#pragma once
#include <windows.h>
#include <filesystem>
#include <string>
namespace Paths {inline std::filesystem::path cache(){
    wchar_t directory[32768]{};
    if(!GetEnvironmentVariableW(L"LOCALAPPDATA",directory,32768))throw std::runtime_error("LOCALAPPDATA unavailable");
    auto folder=std::filesystem::path(directory)/L"VZX-Dumper"/L"LiveTest";std::filesystem::create_directories(folder);
    return folder/(std::to_string(Offsets::Build::ElfArchitecture)+"-"+std::to_string(Offsets::Build::ElfEntryMarker)+"-"+std::to_string(Offsets::Build::GameClassRva)+".cache");
}}
