#include "rules.hpp"
struct Offset {std::string name,key,field,value,state;};
struct Result {
    std::vector<Offset> offsets;std::string architecture,rva,summary,text;
    unsigned found=0,support=0;bool ambiguous=false;
};
inline Result analyze(const fs::path& dump,const fs::path& library){
    Result out;RvaDiscovery::Elf elf(library);auto configured=rules();std::ifstream file(dump,std::ios::binary);
    if(!file)throw std::runtime_error("Nao foi possivel abrir dump.cs");
    auto fields=parse_dump(file,configured,utf8(dump));out.architecture=elf.arm32?"ARMv7 (32 bits)":"ARM64 (64 bits)";
    if((elf.arm32&&fields.architecture.find("64-bit")!=std::string::npos)||(!elf.arm32&&fields.architecture.find("32-bit")!=std::string::npos))throw std::runtime_error("O dump e a biblioteca tem arquiteturas diferentes");
    auto aliases=names();
    for(const auto& match:resolve(fields,configured)){
        std::string name=aliases.contains(match.rule.key)?aliases.at(match.rule.key):match.rule.key;
        if(!aliases.contains(match.rule.key)){
            if(name.rfind("Skeleton.Node",0)==0)name="bone_unidentified_"+name.substr(13);
            else{std::string formatted;for(size_t i=0;i<name.size();++i){unsigned char c=name[i];if(c=='.')formatted+='_';else{if(i&&std::isupper(c)&&std::islower(static_cast<unsigned char>(name[i-1])))formatted+='_';formatted+=static_cast<char>(std::tolower(c));}}name=formatted;}
        }
        std::string state=match.state=="captured"?"Encontrado":match.state=="ambiguous"?"Ambiguo":match.state=="missing"?"Ausente":"Offset indisponivel";
        out.offsets.push_back({name,match.rule.key,match.rule.name,match.state=="captured"?hex(*match.rows[0]->offset):"",state});
        if(match.state=="captured")++out.found;
    }
    try{
        auto methods=RvaDiscovery::read_methods(dump);auto candidates=RvaDiscovery::analyze(elf,methods,elf.arm32?0x5C:0xB8);
        if(candidates.empty())out.summary="Nenhum RVA reconhecido. Os offsets encontrados continuam disponiveis.";
        else {
            out.ambiguous=candidates.size()>1&&candidates[0].methods.size()==candidates[1].methods.size();
            out.support=static_cast<unsigned>(candidates[0].methods.size());
            if(!out.ambiguous)out.rva=hex(candidates[0].rva);
            out.summary=out.ambiguous?"Mais de um RVA com o mesmo suporte. Resultado ambiguo.":"RVA candidato estatico: nao confirmado em memoria.";
        }
    }catch(const std::exception& e){out.summary="RVA indisponivel: "+std::string(e.what());}
    std::ostringstream text;text<<"// "<<out.architecture<<" | Offsets "<<out.found<<'/'<<out.offsets.size()<<"\n// "<<out.summary<<"\n";
    if(!out.rva.empty())text<<"// game_class_rva = "<<out.rva<<"; suporte: "<<out.support<<" metodos\n";
    text<<"// Nomes de bones seguem o mapeamento VZX-COMPACT fornecido.\n// Perfil parcial: nao inclui layouts nativos de camera/Unity/emulador.\n#pragma once\n#include <cstdint>\nnamespace DumpOffsets {\n";
    for(const auto& row:out.offsets){if(!row.value.empty())text<<"inline constexpr std::uint64_t "<<row.name<<" = "<<row.value<<"; // "<<row.field<<"\n";else text<<"// "<<row.name<<": "<<row.state<<"\n";}
    text<<"}\n";out.text=text.str();return out;
}
}
