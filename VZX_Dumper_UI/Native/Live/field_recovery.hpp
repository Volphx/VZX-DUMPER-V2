#pragma once
// Read-only FieldInfo recovery. Values are accepted only after v31 metadata
// pointer/token correspondence validation; never substitutes stored offsets.
namespace FieldRecovery {
using Search::Capture; using Search::Field; using Search::hex; using Search::utf8;
struct Seed {
 U64 class_name=8,class_namespace=12,class_parent=0x2C,class_fields=0x40;
 U64 stride=20,field_name=0,field_owner=8,field_offset=12,field_token=16;
};
static std::string cstring(Memory& mem,U64 pointer){
    if(pointer<0x10000||pointer>=0x800000000000ULL)throw std::runtime_error("Invalid metadata string pointer");
    std::string text;for(unsigned i=0;i<256;++i){char c=mem.get<char>(pointer+i);if(!c)return text;text+=c;}throw std::runtime_error("Metadata string too long");
}
class LiveCollector {
    Memory& mem_;const Seed& seed_;Capture& result_;std::set<U64> seen_;
    std::string safe_text(U64 klass,U64 offset){try{return cstring(mem_,mem_.ptr(klass+offset));}catch(...){return {};}}
public:
    LiveCollector(Memory& mem,const Seed& seed,Capture& result):mem_(mem),seed_(seed),result_(result){}
    void klass(U64 current,const std::string& context){
        for(unsigned depth=0;current>=0x10000&&depth<24;++depth){
            if(!seen_.insert(current).second)return;
            try{
                auto name=safe_text(current,seed_.class_name),space=safe_text(current,seed_.class_namespace);
                std::string owner=name.empty()?"Class@"+hex(current):(space.empty()?name:space+'.'+name);
                std::vector<Field> rows;U64 fields=mem_.ptr(current+seed_.class_fields);
                for(unsigned i=0;fields>=0x10000&&i<4096;++i){
                    U64 entry=fields+i*seed_.stride;
                    try{
                        if(mem_.ptr(entry+seed_.field_owner)!=current)break;
                        Field f;f.owner=owner;f.address=entry;f.origin="live:"+context;f.token=mem_.get<unsigned>(entry+seed_.field_token);
                        int offset=mem_.get<int>(entry+seed_.field_offset);if(offset>=0&&offset<0x1000000)f.offset=static_cast<unsigned>(offset);
                        U64 name_pointer=0;
                        try{name_pointer=mem_.ptr(entry+seed_.field_name);f.name=cstring(mem_,name_pointer);}catch(const std::exception& ex){if(result_.notes.size()<8)result_.notes.push_back("Field name "+hex(name_pointer)+": "+ex.what());f.name="<name-unavailable:"+hex(f.token)+">";}f.name_pointer=name_pointer;
                        rows.push_back(std::move(f));++result_.parsed;
                    }catch(...){break;}
                }
                auto has=[&](const char* value){return std::any_of(rows.begin(),rows.end(),[&](const Field& f){return f.name==value;});};
                // Identify two stable base schemas when their class-name string is nonresident.
                std::string inferred;
                if(name.empty()&&has("CurrentGame")&&has("CurrentMatchGame"))inferred="COW.GameFacade";
                if(name.empty()&&has("m_Match")&&has("m_GameModeSetting"))inferred="COW.MatchGame";
                if(!inferred.empty())for(auto& f:rows){f.owner=inferred;f.origin+=";owner_from_field_signature";}
                result_.fields.insert(result_.fields.end(),rows.begin(),rows.end());
                current=mem_.ptr(current+seed_.class_parent);
            }catch(const std::exception& ex){result_.notes.push_back(context+": "+ex.what());break;}
        }
    }
    void object(U64 pointer,const std::string& label){if(pointer<0x10000)return;try{klass(mem_.ptr(pointer),label);}catch(const std::exception& e){result_.notes.push_back(label+": "+e.what());}}
};
struct MetadataField {std::string owner,name;U64 file_name_offset;};
static void recover_metadata_names(Capture& capture,const fs::path& path){
    std::ifstream file(path,std::ios::binary|std::ios::ate);if(!file){capture.notes.push_back("Matching metadata file unavailable: "+utf8(path));return;}
    auto length=file.tellg();if(length<256||length>512*1024*1024)throw std::runtime_error("Metadata file size rejected");
    std::vector<unsigned char> bytes(static_cast<size_t>(length));file.seekg(0);file.read(reinterpret_cast<char*>(bytes.data()),bytes.size());if(!file)throw std::runtime_error("Cannot read metadata file");
    auto u32=[&](U64 at){if(at+4>bytes.size())throw std::runtime_error("Metadata table out of bounds");unsigned v=0;memcpy(&v,bytes.data()+at,4);return v;};
    if(u32(0)!=0xfab11baf||u32(4)!=31)throw std::runtime_error("Metadata-name recovery currently supports the tested v31 layout only");
    auto section=[&](unsigned index,unsigned stride){U64 offset=u32(8+index*8),size=u32(12+index*8);if(offset+size>bytes.size()||size%stride)throw std::runtime_error("Metadata table layout rejected");return std::pair<U64,U64>{offset,size/stride};};
    auto [strings,string_size]=section(2,1);auto [fields,field_count]=section(11,12);auto [types,type_count]=section(19,88);
    auto string=[&](unsigned index){if(index>=string_size)throw std::runtime_error("Metadata string index rejected");std::string out;for(U64 i=strings+index;i<strings+string_size&&out.size()<512;++i){if(!bytes[i])return out;out+=char(bytes[i]);}throw std::runtime_error("Metadata string is not terminated");};
    std::set<unsigned> wanted;for(const auto& f:capture.fields)if(f.token&&f.name_pointer)wanted.insert(f.token);
    std::map<unsigned,std::vector<MetadataField>> index;
    for(U64 t=0;t<type_count;++t){U64 at=types+t*88;unsigned start=u32(at+32);unsigned count=0;unsigned short small_count=0;memcpy(&small_count,bytes.data()+at+68,2);count=small_count;if(!count)continue;if(U64(start)+count>field_count)throw std::runtime_error("Metadata type field range rejected");
        std::string owner;
        for(unsigned i=0;i<count;++i){U64 f=fields+(U64(start)+i)*12;unsigned token=u32(f+8);if(!wanted.contains(token))continue;
            if(owner.empty()){owner=string(u32(at));auto space=string(u32(at+4));if(!space.empty())owner=space+'.'+owner;}
            unsigned name=u32(f);index[token].push_back({owner,string(name),strings+name});
        }
    }
    std::map<U64,unsigned> votes;unsigned eligible=0;
    for(const auto& f:capture.fields){auto found=index.find(f.token);if(found==index.end()||!f.name_pointer)continue;++eligible;std::set<U64> bases;
        for(const auto& item:found->second)if(f.name_pointer>item.file_name_offset){U64 base=f.name_pointer-item.file_name_offset;if(!(base&15))bases.insert(base);}
        for(U64 base:bases)++votes[base];
    }
    U64 base=0;unsigned support=0,second=0;
    for(const auto& [address,n]:votes){if(n>support){second=support;support=n;base=address;}else second=std::max(second,n);}
    if(support<32||support*100ULL<eligible*80ULL||second*2ULL>=support){capture.notes.push_back("Metadata correspondence REJECTED: insufficient unique pointer/token agreement; no names were substituted.");return;}
    unsigned recovered=0;
    for(auto& f:capture.fields){auto found=index.find(f.token);if(found==index.end())continue;std::vector<const MetadataField*> candidates;
        for(const auto& item:found->second)if(base+item.file_name_offset==f.name_pointer)candidates.push_back(&item);
        if(candidates.size()!=1)continue;auto* item=candidates[0];
        if(f.name.rfind("<name-unavailable:",0)!=0&&f.name!=item->name)continue;
        f.name=item->name;f.owner=item->owner;f.origin+=";name_from_validated_v31_metadata";++recovered;
    }
    capture.notes.push_back("Metadata name correspondence validated by "+std::to_string(support)+" live FieldInfo pointer/token pairs (base "+hex(base)+"). Restored "+std::to_string(recovered)+" field identities using "+utf8(path)+". Offset values still come exclusively from live FieldInfo.");
}
inline unsigned recover(Memory& mem,std::map<std::string,U64>& values,U64 local,const fs::path& metadata) {
 Capture captured;captured.mode="live";Seed seed;LiveCollector collector(mem,seed,captured);
 auto merge=[&](){
  recover_metadata_names(captured,metadata);
  for(const auto& match:Search::resolve(captured,Search::rules())) {
   if(match.state!="captured"||match.rows[0]->origin.find("name_from_validated_v31_metadata")==std::string::npos)continue;
   auto value=U64(*match.rows[0]->offset);auto existing=values.find(match.rule.key);
   if(existing!=values.end()&&existing->second!=value)throw std::runtime_error("FieldInfo diverge do dump: "+match.rule.key);
   values.emplace(match.rule.key,value);
  }
 };
 const auto before=values.size();collector.object(local,"PlayerHierarchy");merge();
 if(values.contains("Player.DataPool")) {
  auto pool=mem.ptr(local+values.at("Player.DataPool"));collector.object(pool,"DataPoolHierarchy");merge();
  if(values.contains("Attributes.Array")) {
   auto array=mem.ptr(pool+values.at("Attributes.Array"));
   if(array)collector.object(mem.ptr(array+0x10),"AttributeHierarchy");merge();
  }
 }
 if(values.contains("Player.AvatarManager")&&values.contains("Avatar.Instance")) {
auto manager=mem.ptr(local+values.at("Player.AvatarManager"));collector.object(manager,"AvatarManager");
collector.object(mem.ptr(manager+values.at("Avatar.Instance")),"UmaAvatar");merge();
}
return unsigned(values.size()-before);
}
}
