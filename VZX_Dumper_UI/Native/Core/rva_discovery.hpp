#pragma once
#include <cstring>

// Lightweight ARM64 data-flow recognizer, not a general disassembler.
// Finds ADRP/ADR -> LDR slot -> LDR class -> LDR static_fields chains.
namespace RvaDiscovery {
struct Segment {U64 offset,address,file_size,memory_size;unsigned flags;};
class Elf {
    std::vector<unsigned char> bytes_;
    std::vector<Segment> segments_;
    template<class T>T at(U64 offset)const{if(offset>bytes_.size()||sizeof(T)>bytes_.size()-offset)throw std::runtime_error("ELF read outside file");T value;memcpy(&value,bytes_.data()+offset,sizeof(value));return value;}
public:
    bool arm32=false;
    explicit Elf(const fs::path& path){
        std::ifstream file(path,std::ios::binary|std::ios::ate);if(!file)throw std::runtime_error("Cannot open ARM64 library");auto size=file.tellg();if(size<64||size>1024LL*1024*1024)throw std::runtime_error("ELF size rejected");bytes_.resize(static_cast<size_t>(size));file.seekg(0);file.read(reinterpret_cast<char*>(bytes_.data()),bytes_.size());if(!file)throw std::runtime_error("Cannot read ELF");
        if(at<unsigned>(0)!=0x464c457f||at<unsigned char>(5)!=1)throw std::runtime_error("Not a little-endian ELF");
        unsigned cls=at<unsigned char>(4),machine=at<unsigned short>(18);
        arm32=cls==1&&machine==40;
        if(!arm32&&!(cls==2&&machine==183))throw std::runtime_error("Expected ARMv7 ELF32 or ARM64 ELF64");
        U64 table=arm32?at<unsigned>(28):at<U64>(32);unsigned count=at<unsigned short>(arm32?44:56),stride=at<unsigned short>(arm32?42:54);
        if(stride!=(arm32?32:56)||count>256||table+U64(count)*stride>bytes_.size())throw std::runtime_error("ELF program headers rejected");
        U64 first=~U64(0);
        for(unsigned i=0;i<count;++i){U64 p=table+i*stride;if(at<unsigned>(p)!=1)continue;
            Segment segment=arm32?Segment{at<unsigned>(p+4),at<unsigned>(p+8),at<unsigned>(p+16),at<unsigned>(p+20),at<unsigned>(p+24)}:
                Segment{at<U64>(p+8),at<U64>(p+16),at<U64>(p+32),at<U64>(p+40),at<unsigned>(p+4)};
            if(segment.offset>bytes_.size()||segment.file_size>bytes_.size()-segment.offset||segment.file_size>segment.memory_size)throw std::runtime_error("ELF segment rejected");
            segments_.push_back(segment);first=std::min(first,segment.address);
        }
        if(first!=0)throw std::runtime_error("ELF image base is not zero; this layout is not supported by the recognizer");
    }
    std::optional<unsigned> instruction(U64 address)const{for(const auto& s:segments_)if((s.flags&1)&&address>=s.address&&address-s.address<s.file_size&&s.file_size-(address-s.address)>=4)return at<unsigned>(s.offset+address-s.address);return {};}
    std::optional<unsigned> word(U64 address)const{for(const auto& s:segments_)if(address>=s.address&&address-s.address<s.file_size&&s.file_size-(address-s.address)>=4)return at<unsigned>(s.offset+address-s.address);return {};}
    bool data_slot(U64 address)const{if(address&(arm32?3:7))return false;for(const auto& s:segments_)if(!(s.flags&1)&&address>=s.address&&address-s.address<s.memory_size&&s.memory_size-(address-s.address)>=(arm32?4:8))return true;return false;}
};
struct Method {U64 rva;std::string name;};
struct Methods {std::vector<Method> target;std::vector<U64> all;};
inline Methods read_methods(const fs::path& path){
    std::ifstream file(path,std::ios::binary);if(!file)throw std::runtime_error("Cannot open method dump");
    Methods result;std::string line,space,owner;std::optional<U64> pending;
    while(std::getline(file,line)){
        auto s=trim(line);if(s.rfind("// Namespace:",0)==0){space=trim(s.substr(13));continue;}
        auto r=s.find("RVA:");if(r!=std::string::npos){auto start=s.find("0x",r);pending.reset();if(start!=std::string::npos){try{size_t used=0;auto value=std::stoull(s.substr(start+2),&used,16);if(used&&value&&!(value&3))pending=value;}catch(...){} }}
        if(s.empty()||s.rfind("//",0)==0)continue;
        size_t type_at=std::string::npos;for(const char* tag:{"class ","struct ","interface ","enum "}){auto p=s.find(tag);if(p!=std::string::npos&&(p==0||s[p-1]==' ')){type_at=p+strlen(tag);break;}}
        if(type_at!=std::string::npos){auto end=s.find_first_of(" \t:{",type_at);auto name=s.substr(type_at,end-type_at);owner=name.find('.')!=std::string::npos||space.empty()?name:space+'.'+name;pending.reset();continue;}
        auto paren=s.find('(');if(paren==std::string::npos||!pending)continue;
        result.all.push_back(*pending);
        if(owner=="COW.GameFacade"){auto before=trim(s.substr(0,paren));auto last=before.find_last_of(" \t");result.target.push_back({*pending,before.substr(last==std::string::npos?0:last+1)});}
        pending.reset();
    }
    std::sort(result.all.begin(),result.all.end());result.all.erase(std::unique(result.all.begin(),result.all.end()),result.all.end());
    std::sort(result.target.begin(),result.target.end(),[](const Method&a,const Method&b){return a.rva<b.rva;});result.target.erase(std::unique(result.target.begin(),result.target.end(),[](const Method&a,const Method&b){return a.rva==b.rva;}),result.target.end());
    if(result.target.empty())throw std::runtime_error("No GameFacade methods with ARM64 RVAs were found in the dump");return result;
}
struct Evidence {U64 method,adrp,slot_load,class_load,static_load;std::string name;};
struct Candidate {U64 rva;std::vector<Evidence> evidence;std::set<U64> methods;};
struct Register {enum Kind{Unknown,Address,Slot,Class,Statics}kind=Unknown;U64 value=0,adrp=0,load=0,class_load=0;};
inline std::vector<Candidate> analyze_arm32(const Elf& elf,const Methods& methods,U64 static_fields){
    std::map<U64,Candidate> candidates;
    for(const auto& method:methods.target){
        std::array<Register,16> regs{};auto next=std::upper_bound(methods.all.begin(),methods.all.end(),method.rva);
        U64 end=method.rva+4096;if(next!=methods.all.end())end=std::min(end,*next);
        for(U64 pc=method.rva;pc<end;pc+=4){
            auto instruction=elf.instruction(pc);if(!instruction)break;unsigned w=*instruction,rd=(w>>12)&15,rn=(w>>16)&15,rm=w&15;
            if((w&0x0ffffff0)==0x012fff10)break;
            if((w&0x0e108000)==0x08108000)break; // LDM with PC, return
            if((w&0x0e000000)==0x0a000000){
                if(w&0x01000000){for(int r=0;r<4;++r)regs[r]={};regs[12]={};regs[14]={};}
                else if((w>>28)==14)regs.fill({});
                continue;
            }
            // LDR immediate/register, preindexed positive or negative without writeback.
            if((w&0x0d700000)==0x05100000){
                U64 base=rn==15?pc+8:regs[rn].value;
                bool regoffset=(w&0x02000000)!=0;bool up=(w&0x00800000)!=0;
                U64 offset=regoffset?regs[rm].value:(w&4095);Register source=regs[rn],out;
                if(regoffset&&((w&0xff0)!=0||regs[rm].kind!=Register::Address)){regs[rd]={};continue;}
                if(rn==15){
                    auto address=up?base+offset:base-offset;
                    if(!regoffset){auto value=elf.word(address);if(value)out={Register::Address,*value,pc,0,0};}
                    else if(elf.data_slot(address))out={Register::Slot,address,regs[rm].adrp,pc,0};
                }else if(source.kind==Register::Address){U64 address=up?base+offset:base-offset;if(elf.data_slot(address))out={Register::Slot,address,source.adrp,pc,0};}
                else if(source.kind==Register::Slot&&!offset){out=source;out.kind=Register::Class;out.class_load=pc;}
                else if(source.kind==Register::Class&&offset==static_fields){out=source;out.kind=Register::Statics;auto& c=candidates[source.value];c.rva=source.value;c.methods.insert(method.rva);c.evidence.push_back({method.rva,source.adrp,source.load,source.class_load,pc,method.name});}
                regs[rd]=out;continue;
            }
            // ADD register, unshifted: compiler's PC-relative literal calculation.
            if((w&0x0fe00ff0)==0x00800000){
                auto a=rn==15?Register{Register::Address,pc+8,pc,0,0}:regs[rn];
                auto b=rm==15?Register{Register::Address,pc+8,pc,0,0}:regs[rm];
                if(a.kind==Register::Address&&b.kind==Register::Address)regs[rd]={Register::Address,static_cast<unsigned>(a.value+b.value),a.adrp?a.adrp:b.adrp,0,0};else regs[rd]={};continue;
            }
            if((w&0x0fe00ff0)==0x01a00000){regs[rd]=regs[rm];continue;}
            if((w&0x0c000000)==0x04000000){if(w&0x00100000)regs[rd]={};continue;}
            if((w&0x0c000000)==0){unsigned opcode=(w>>21)&15;if(opcode<8||opcode>11)regs[rd]={};}
        }
    }
    std::vector<Candidate> result;for(auto& [key,c]:candidates)result.push_back(std::move(c));
    std::sort(result.begin(),result.end(),[](const Candidate&a,const Candidate&b){if(a.methods.size()!=b.methods.size())return a.methods.size()>b.methods.size();return a.rva<b.rva;});return result;
}
inline std::vector<Candidate> analyze(const Elf& elf,const Methods& methods,U64 static_fields){
    if(elf.arm32)return analyze_arm32(elf,methods,0x5C);
    std::map<U64,Candidate> candidates;
    for(const auto& method:methods.target){
        std::array<Register,32> regs{};auto next=std::upper_bound(methods.all.begin(),methods.all.end(),method.rva);U64 end=method.rva+4096;if(next!=methods.all.end())end=std::min(end,*next);
        for(U64 pc=method.rva;pc<end;pc+=4){
            auto instruction=elf.instruction(pc);if(!instruction)break;unsigned w=*instruction,rd=w&31,rn=(w>>5)&31;Register source=regs[rn];
            if((w&0xfffffc1f)==0xd65f0000)break; // RET
            if((w&0x9f000000)==0x90000000||(w&0x9f000000)==0x10000000){
                long long imm=(((w>>5)&0x7ffff)<<2)|((w>>29)&3);if(imm&(1<<20))imm-=1<<21;
                bool page=(w&0x9f000000)==0x90000000;long long address=page?static_cast<long long>(pc&~U64(4095))+imm*4096:static_cast<long long>(pc)+imm;
                regs[rd]=address>=0?Register{Register::Address,static_cast<U64>(address),pc,0,0}:Register{};continue;
            }
            if((w&0xff000000)==0x91000000){U64 imm=((w>>10)&0xfff)<<(((w>>22)&1)?12:0);if(source.kind==Register::Address){regs[rd]=source;regs[rd].value+=imm;}else regs[rd]={};continue;}
            if((w&0xffc00000)==0xf9400000){
                U64 offset=((w>>10)&0xfff)*8;Register target;
                if(source.kind==Register::Address&&elf.data_slot(source.value+offset))target={Register::Slot,source.value+offset,source.adrp,pc,0};
                else if(source.kind==Register::Slot&&!offset){target=source;target.kind=Register::Class;target.class_load=pc;}
                else if(source.kind==Register::Class&&offset==static_fields){target=source;target.kind=Register::Statics;auto& c=candidates[source.value];c.rva=source.value;c.methods.insert(method.rva);c.evidence.push_back({method.rva,source.adrp,source.load,source.class_load,pc,method.name});}
                regs[rd]=target;continue;
            }
            if((w&0xffe0ffe0)==0xaa0003e0){regs[rd]=regs[(w>>16)&31];continue;} // MOV Xd, Xm
            if((w&0xfc000000)==0x94000000||(w&0xfffffc1f)==0xd63f0000){for(unsigned i=0;i<19;++i)regs[i]={};regs[30]={};continue;}
            if((w&0xfc000000)==0x14000000){regs.fill({});continue;}
            if((w&0x7e000000)==0x34000000||(w&0x7e000000)==0x36000000||(w&0xff000010)==0x54000000)continue; // Conditional branches do not write registers.
            if((w&0x3b000000)==0x39000000){if(w&(1u<<22))regs[rd]={};continue;} // Other unsigned loads / stores.
            if((w&0x3a000000)==0x28000000){if(w&(1u<<22)){regs[rd]={};regs[(w>>10)&31]={};}continue;}
            if(rd!=31)regs[rd]={}; // Conservative invalidation for unrecognized writes.
        }
    }
    std::vector<Candidate> result;for(auto& [_,candidate]:candidates)result.push_back(std::move(candidate));
    std::sort(result.begin(),result.end(),[](const Candidate&a,const Candidate&b){if(a.methods.size()!=b.methods.size())return a.methods.size()>b.methods.size();if(a.evidence.size()!=b.evidence.size())return a.evidence.size()>b.evidence.size();return a.rva<b.rva;});return result;
}
}
