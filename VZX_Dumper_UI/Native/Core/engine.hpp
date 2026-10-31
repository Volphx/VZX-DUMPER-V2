#pragma once
#define NOMINMAX
#include <windows.h>
#include <algorithm>
#include <array>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <map>
#include <optional>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
using U64=std::uint64_t;
namespace fs=std::filesystem;
namespace Search {
struct Rule {std::string key,owner,name;unsigned type_index=0;};
struct Field {std::string owner,name,type,origin;std::optional<unsigned> offset;U64 address=0,name_pointer=0;unsigned token=0,type_index=0;bool is_static=false;};
struct Capture {std::string mode,source,architecture="unknown";unsigned long long parsed=0;std::vector<Field> fields;std::vector<std::string> notes;U64 base=0;DWORD pid=0;};
struct Match {Rule rule;std::vector<const Field*> rows;std::string state;};
static std::string trim(std::string s){auto a=s.find_first_not_of(" \t\r\n");if(a==std::string::npos)return {};auto b=s.find_last_not_of(" \t\r\n");return s.substr(a,b-a+1);}
static std::string utf8(const fs::path& p){auto s=p.u8string();return {reinterpret_cast<const char*>(s.data()),s.size()};}
static std::string hex(U64 n){std::ostringstream s;s<<"0x"<<std::hex<<std::uppercase<<n;return s.str();}
static std::string json(const std::string& s){std::ostringstream out;out<<'"';for(unsigned char c:s){switch(c){case '"':out<<"\\\"";break;case '\\':out<<"\\\\";break;case '\n':out<<"\\n";break;case '\r':out<<"\\r";break;case '\t':out<<"\\t";break;default:if(c<32)out<<"\\u"<<std::hex<<std::setw(4)<<std::setfill('0')<<unsigned(c)<<std::dec;else out<<char(c);}}out<<'"';return out.str();}
static std::string csv(const std::string& s){std::string out="\"";for(char c:s){if(c=='"')out+='"';out+=c;}return out+'"';}
static std::string leaf(const std::string& name){auto at=name.rfind('.');return at==std::string::npos?name:name.substr(at+1);}
static bool valid_key(const std::string& key){
    bool first=true;unsigned dots=0;
    for(unsigned char c:key){if(c=='.'){if(first)return false;first=true;++dots;continue;}if(first){if(!(std::isalpha(c)||c=='_'))return false;first=false;}else if(!(std::isalnum(c)||c=='_'))return false;}
    return !first&&dots==1;
}
static Capture parse_dump(std::istream& input,const std::vector<Rule>& rules,const std::string& source){
    Capture out;out.mode="dump";out.source=source;
    std::set<std::string> wanted;for(const auto& r:rules)wanted.insert(r.owner);
    std::string line,space,owner;std::vector<unsigned> nodes;unsigned type_index=0;
    while(std::getline(input,line)){
        std::string s=trim(line);
        auto type_marker=s.find("TypeDefIndex:");if(type_marker!=std::string::npos){try{type_index=std::stoul(s.substr(type_marker+13));}catch(...){type_index=0;}}
        if(s.rfind("// Namespace:",0)==0){space=trim(s.substr(13));continue;}
        if(s.empty()||s.rfind("//",0)==0)continue;
        size_t type_at=std::string::npos;
        for(const char* tag:{"class ","struct ","interface ","enum "}){
            auto pos=s.find(tag);if(pos!=std::string::npos&&(pos==0||s[pos-1]==' ')){type_at=pos+strlen(tag);break;}
        }
        if(type_at!=std::string::npos){
            auto end=s.find_first_of(" \t:{",type_at);std::string name=s.substr(type_at,end-type_at);
            owner=name.find('.')!=std::string::npos||space.empty()?name:space+'.'+name;continue;
        }
        auto comment=s.find("//");if(comment==std::string::npos)continue;
        auto semi=s.rfind(';',comment);if(semi==std::string::npos)continue;
        std::string decl=trim(s.substr(0,semi));if(decl.find('(')!=std::string::npos)continue;
        auto split=decl.find_last_of(" \t");if(split==std::string::npos)continue;
        Field row;row.owner=owner;row.name=decl.substr(split+1);row.type=trim(decl.substr(0,split));row.origin="dump_field";
        if(row.name.empty())continue;
        auto start=s.find("0x",comment);
        if(start!=std::string::npos){try{size_t used=0;auto value=std::stoull(s.substr(start+2),&used,16);bool negative=start>comment&&s[start-1]=='-';if(!negative&&used&&value<0x1000000)row.offset=static_cast<unsigned>(value);}catch(...){} }
        else if(s.find("offset unavailable",comment)==std::string::npos)continue;
        row.type_index=type_index;
        row.is_static=decl.find("static ")!=std::string::npos||decl.find("const ")!=std::string::npos;
        ++out.parsed;
        if(owner=="COW.GamePlay.Player"&&row.type.find("ITransformNode")!=std::string::npos&&row.offset)nodes.push_back(*row.offset);
        if(wanted.contains(owner)||wanted.contains(leaf(owner)))out.fields.push_back(std::move(row));
    }
    std::sort(nodes.begin(),nodes.end());nodes.erase(std::unique(nodes.begin(),nodes.end()),nodes.end());unsigned four=0,eight=0;
    for(size_t i=1;i<nodes.size();++i){auto delta=nodes[i]-nodes[i-1];if(delta==4)++four;else if(delta==8)++eight;}
    if(four>=3)out.architecture="V7A/32-bit (inferred from node field spacing)";
    else if(eight>=3)out.architecture="ARM64/64-bit (inferred from node field spacing)";
    else out.notes.push_back("Architecture could not be inferred; offsets are not a validated ARM64 profile.");
    if(!out.parsed)throw std::runtime_error("No dump fields parsed. Expected UTF-8 dump.cs with field offsets.");
    out.notes.push_back("dump.cs supplies managed field offsets, not native RVAs, camera matrices or emulator layouts.");
    return out;
}
static std::vector<Match> resolve(const Capture& capture,const std::vector<Rule>& rules){
    std::vector<Match> result;
    for(const auto& rule:rules){
        Match m;m.rule=rule;
        for(const auto& f:capture.fields)if((!rule.type_index||!f.type_index||rule.type_index==f.type_index)&&f.name==rule.name&&(f.owner==rule.owner||
            (rule.owner.find('.')==std::string::npos&&leaf(f.owner)==rule.owner)||
            (capture.mode=="live"&&f.owner.find('.')==std::string::npos&&f.owner==leaf(rule.owner))))m.rows.push_back(&f);
        m.state=m.rows.empty()?"missing":m.rows.size()>1?"ambiguous":!m.rows[0]->offset?"offset_unavailable":"captured";
        result.push_back(std::move(m));
    }return result;
}

#include "rva_discovery.hpp"

#include "search_tail.hpp"
