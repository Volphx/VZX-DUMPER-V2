#include "Core/engine.hpp"
#include <cstdlib>
#include "Live/diagnostic.hpp"
static char* copy_result(const std::string& text) {
    auto* result=static_cast<char*>(std::malloc(text.size()+1));
    if(result)std::memcpy(result,text.c_str(),text.size()+1);
    return result;
}
extern "C" __declspec(dllexport) char* __cdecl vzx_analyze(const wchar_t* dump,const wchar_t* library) noexcept {
    try {
        const auto result=Search::analyze(fs::path(dump),fs::path(library));
        std::ostringstream out;
        out<<"{\"architecture\":"<<Search::json(result.architecture)<<",\"rva\":"<<Search::json(result.rva)
           <<",\"summary\":"<<Search::json(result.summary)<<",\"text\":"<<Search::json(result.text)
           <<",\"found\":"<<result.found<<",\"support\":"<<result.support<<",\"ambiguous\":"<<(result.ambiguous?"true":"false")<<",\"offsets\":[";
        bool comma=false;
        for(const auto& row:result.offsets) {
            if(comma)out<<',';comma=true;
            out<<"{\"name\":"<<Search::json(row.name)<<",\"key\":"<<Search::json(row.key)<<",\"field\":"<<Search::json(row.field)
               <<",\"value\":"<<Search::json(row.value)<<",\"state\":"<<Search::json(row.state)<<'}';
        }
        out<<"]}";return copy_result(out.str());
    }catch(const std::exception& e){return copy_result("{\"error\":"+Search::json(e.what())+"}");}
    catch(...){return copy_result("{\"error\":\"Falha inesperada ao analisar os arquivos.\"}");}
}
extern "C" __declspec(dllexport) void __cdecl vzx_free(void* pointer) noexcept {std::free(pointer);}
extern "C" __declspec(dllexport) char* __cdecl vzx_esp_start(const wchar_t* library,U64 rva,const char* offsets,DWORD pid) noexcept {
    try{return copy_result(LiveTest::start(library,rva,offsets,pid));}
    catch(const std::exception& e){return copy_result("{\"error\":"+Search::json(e.what())+"}");}
    catch(...){return copy_result("{\"error\":\"Falha ao conectar o teste ESP.\"}");}
}
extern "C" __declspec(dllexport) char* __cdecl vzx_esp_frame() noexcept {
    try{return copy_result(LiveTest::frame());}
    catch(const std::exception& e){return copy_result("{\"valid\":false,\"status\":"+Search::json(e.what())+",\"boxes\":[]}");}
    catch(...){return copy_result("{\"valid\":false,\"status\":\"Falha na leitura do quadro.\",\"boxes\":[]}");}
}
extern "C" __declspec(dllexport) void __cdecl vzx_esp_stop() noexcept {try{LiveTest::stop();}catch(...){} }
