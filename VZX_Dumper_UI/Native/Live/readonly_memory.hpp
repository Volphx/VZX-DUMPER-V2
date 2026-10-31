// C++20 | External guest memory through HD-Player.exe | BstkVMM 6.1.36.156792
#pragma once
#include "profile.hpp"
#include "emulator_offsets.hpp"
#include "cache_path.hpp"
#include <windows.h>
#include <tlhelp32.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <set>
#include <stdexcept>
#include <unordered_map>
#include <vector>

using U64=std::uint64_t;
class Memory {
    struct Range {U64 node,start,size,host;};
    HANDLE process_=nullptr;
    DWORD pid_=0;
    U64 created_=0,uvm_=0,vm_=0,cr3_=0;
    std::vector<Range> ranges_;
    std::unordered_map<unsigned,U64> chunks_;
    std::unordered_map<U64,U64> virtual_pages_;
    std::unordered_map<U64,std::array<char,4096>> table_pages_,data_pages_;
    std::filesystem::path cache_path_;
    template<class T> bool try_host(U64 a,T& value) const {
        SIZE_T n=0;
        return ReadProcessMemory(process_,reinterpret_cast<LPCVOID>(a),&value,sizeof(value),&n)&&n==sizeof(value);
    }
    template<class T> T host(U64 a) const {
        T value{};if(!try_host(a,value))throw std::runtime_error("Windows memory read failed");return value;
    }
    void host_bytes(U64 a,void* destination,size_t size) const {
        SIZE_T n=0;
        if(!ReadProcessMemory(process_,reinterpret_cast<LPCVOID>(a),destination,size,&n)||n!=size)
            throw std::runtime_error("Windows memory range unavailable");
    }
    bool accept_uvm(U64 candidate) {
        U64 vm=0,cpu=0;unsigned magic=0,count=0;
        if((candidate&4095)||!try_host(candidate,magic)||magic!=EmulatorOffsets::UvmMagic||!try_host(candidate+EmulatorOffsets::UvmVm,vm)||
            !try_host(vm+EmulatorOffsets::CpuCount,count)||!count||count>64||!try_host(vm+EmulatorOffsets::CpuTable,cpu)||cpu<4096)return false;
        unsigned char mode=0;if(!try_host(cpu+EmulatorOffsets::CpuPagingMode,mode)||mode!=EmulatorOffsets::PagingMode)return false;
        uvm_=candidate;vm_=vm;return true;
    }
    void find_vm() {
        std::vector<MEMORY_BASIC_INFORMATION> regions;
        MEMORY_BASIC_INFORMATION mbi{};
        for(U64 address=0;address<0x7fffffffffffULL;) {
            if(!VirtualQueryEx(process_,reinterpret_cast<LPCVOID>(address),&mbi,sizeof(mbi)))break;
            U64 begin=reinterpret_cast<U64>(mbi.BaseAddress);
            if(mbi.State==MEM_COMMIT&&mbi.Type==MEM_PRIVATE&&!(mbi.Protect&(PAGE_NOACCESS|PAGE_GUARD)))regions.push_back(mbi);
            if(begin+mbi.RegionSize<=address)break;address=begin+mbi.RegionSize;
        }
        std::sort(regions.begin(),regions.end(),[](const auto& a,const auto& b){return a.RegionSize<b.RegionSize;});
        std::vector<char> buffer(4*1024*1024);
        for(const auto& region:regions)for(size_t offset=0;offset<region.RegionSize;offset+=buffer.size()) {
            U64 start=reinterpret_cast<U64>(region.BaseAddress)+offset;
            SIZE_T received=0;size_t wanted=std::min(buffer.size(),region.RegionSize-offset);
            ReadProcessMemory(process_,reinterpret_cast<LPCVOID>(start),buffer.data(),wanted,&received);
            size_t first=static_cast<size_t>((4096-(start&4095))&4095);
            for(size_t at=first;at+4<=received;at+=4096) {
                unsigned magic=0;std::memcpy(&magic,buffer.data()+at,4);
                if(magic==EmulatorOffsets::UvmMagic&&accept_uvm(start+at))return;
            }
        }
        throw std::runtime_error("Supported BlueStacks virtual machine not found");
    }
    void collect_ranges(U64 node,std::set<U64>& seen) {
        if(!node||seen.contains(node))return;
        if(seen.size()>256)throw std::runtime_error("Unexpected RAM range tree");
        seen.insert(node);auto values=host<std::array<U64,14>>(node);
        if(values[EmulatorOffsets::Range::SizeIndex]&&values[EmulatorOffsets::Range::SizeIndex]<0x10000000000ULL)ranges_.push_back({node,values[EmulatorOffsets::Range::StartIndex],values[EmulatorOffsets::Range::SizeIndex],values[EmulatorOffsets::Range::HostIndex]});
        collect_ranges(values[EmulatorOffsets::Range::LeftIndex],seen);collect_ranges(values[EmulatorOffsets::Range::RightIndex],seen);
    }
    U64 chunk_host(unsigned id) {
        if(auto found=chunks_.find(id);found!=chunks_.end())return found->second;
        U64 node=host<U64>(vm_+EmulatorOffsets::ChunkTree);
        for(int depth=0;node&&depth<64;++depth) {
            auto bytes=host<std::array<char,48>>(node);unsigned key=0;
            std::memcpy(&key,bytes.data()+EmulatorOffsets::Chunk::Key,4);
            if(key==id) {
                U64 address=0;std::memcpy(&address,bytes.data()+EmulatorOffsets::Chunk::Host,8);
                if(!address)break;chunks_[id]=address;return address;
            }
            std::memcpy(&node,bytes.data()+(id<key?EmulatorOffsets::Chunk::Left:EmulatorOffsets::Chunk::Right),8);
        }
        throw std::runtime_error("Guest RAM chunk not mapped in Windows");
    }
    U64 physical_host(U64 address) {
        for(const auto& range:ranges_)if(address>=range.start&&address-range.start<range.size) {
            U64 offset=address-range.start;
            if(range.host)return range.host+offset;
            auto descriptor=host<std::array<unsigned,4>>(range.node+EmulatorOffsets::Range::PageTable+(offset>>12)*EmulatorOffsets::Range::DescriptorStride);
            unsigned page_id=descriptor[EmulatorOffsets::Range::PageIdIndex];
            if(!page_id)return vm_+EmulatorOffsets::ZeroPage+(address&4095);
            return chunk_host(page_id>>EmulatorOffsets::Range::ChunkShift)+U64(page_id&EmulatorOffsets::Range::PageMask)*4096+(address&4095);
        }
        throw std::runtime_error("Guest physical address outside RAM");
    }
    const std::array<char,4096>& table_page(U64 physical) {
        physical&=~U64(4095);
        if(auto found=table_pages_.find(physical);found!=table_pages_.end())return found->second;
        std::array<char,4096> bytes{};host_bytes(physical_host(physical),bytes.data(),bytes.size());
        return table_pages_.emplace(physical,bytes).first->second;
    }
    U64 physical_qword(U64 physical) {
        const auto& bytes=table_page(physical);U64 value=0;
        std::memcpy(&value,bytes.data()+(physical&4095),8);return value;
    }
    U64 translate(U64 address) {
        U64 page=address&~U64(4095);
        if(auto found=virtual_pages_.find(page);found!=virtual_pages_.end())return found->second+(address&4095);
        U64 table=cr3_&0xffffffffff000ULL;
        for(int shift:{39,30,21,12}) {
            U64 entry=physical_qword(table+((address>>shift)&511)*8);
            if(!(entry&1)){std::ostringstream info;info<<"Guest page not present: va="<<std::hex<<address<<" level="<<std::dec<<shift<<" table="<<std::hex<<table<<" entry="<<entry;throw std::runtime_error(info.str());}
            if((shift==30||shift==21)&&(entry&128)) {
                U64 physical=(entry&0xffffffffff000ULL&~((U64(1)<<shift)-1))+(address&((U64(1)<<shift)-1));
                U64 mapped=physical_host(physical&~U64(4095));virtual_pages_[page]=mapped;return mapped+(address&4095);
            }
            table=entry&0xffffffffff000ULL;
        }
        U64 mapped=physical_host(table);virtual_pages_[page]=mapped;return mapped+(address&4095);
    }
    bool validate_base(U64 candidate) {
        try {
            auto header=get<std::array<unsigned,16>>(candidate);
            if(header[0]!=Offsets::Build::ElfMagic||header[4]!=Offsets::Build::ElfArchitecture||(Offsets::Build::ElfEntryMarker&&header[6]!=Offsets::Build::ElfEntryMarker))return false;
            U64 klass=ptr(ptr(candidate+Offsets::Build::GameClassRva));
            U64 fields=ptr(klass+Offsets::Managed::ClassStaticFields);
            if(klass<0x10000||fields<0x10000||(klass&(Offsets::Managed::PointerSize-1))||(fields&(Offsets::Managed::PointerSize-1)))return false;
            // Metadata name pages may not be resident. Validate live class storage instead.
            (void)ptr(fields);return true;
        }catch(const std::exception&){return false;}
    }
    U64 scan_tables(U64 table,U64 virtual_prefix,int level,size_t& budget,ULONGLONG deadline) {
        if(!budget||GetTickCount64()>deadline)return 0;
        std::array<U64,512> entries{};
        try {const auto& bytes=table_page(table);std::memcpy(entries.data(),bytes.data(),4096);}catch(const std::exception&){return 0;}
        int shift=39-9*level;
        unsigned limit=level==0?256u:512u;
        for(unsigned i=0;i<limit;++i) {
            U64 entry=entries[i];if(!(entry&1))continue;
            U64 address=virtual_prefix|(U64(i)<<shift);
            if(level==3||((level==1||level==2)&&(entry&128))) {
                // Check mapping starts. ELF images in the tested loader have non-executable headers.
                if(!(entry&(U64(1)<<63)))continue;
                if(!budget--)return 0;
                try {
                    U64 physical=entry&0xffffffffff000ULL;
                    unsigned magic=host<unsigned>(physical_host(physical));
                    if(magic==Offsets::Build::ElfMagic&&validate_base(address))return address;
                }catch(const std::exception&){}
            } else {
                U64 found=scan_tables(entry&0xffffffffff000ULL,address,level+1,budget,deadline);
                if(found)return found;
            }
        }return 0;
    }
    bool resolve_context(U64 base_hint,bool discover) {
        unsigned count=host<unsigned>(vm_+EmulatorOffsets::CpuCount);std::set<U64> attempted;
        for(int attempt=0;attempt<50;++attempt) {
            for(unsigned i=0;i<count;++i) {
                U64 cpu=host<U64>(vm_+EmulatorOffsets::CpuTable+i*EmulatorOffsets::CpuStride),candidate=host<U64>(cpu+EmulatorOffsets::CpuCr3);
                if(!candidate||attempted.contains(candidate))continue;
                attempted.insert(candidate);cr3_=candidate;begin_frame();
                if(base_hint&&validate_base(base_hint)){base=base_hint;return true;}
                if(discover) {
                    size_t budget=1000000;U64 found=scan_tables(cr3_&0xffffffffff000ULL,0,0,budget,GetTickCount64()+8000);
                    if(found){base=found;return true;}
                }
            }
            Sleep(20);
        }return false;
    }
public:
    U64 base=0;
    explicit Memory(DWORD pid,bool force_discovery=false):pid_(pid) { try {
        process_=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ,FALSE,pid);
        if(!process_)throw std::runtime_error("Cannot read HD-Player.exe from Windows");
        FILETIME creation{},exit{},kernel{},user{};
        if(GetProcessTimes(process_,&creation,&exit,&kernel,&user))created_=(U64(creation.dwHighDateTime)<<32)|creation.dwLowDateTime;
        HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid);
        MODULEENTRY32W module{sizeof(module)};U64 vmm=0;
        if(Module32FirstW(snapshot,&module))do{if(!_wcsicmp(module.szModule,L"BstkVMM.dll")){vmm=reinterpret_cast<U64>(module.modBaseAddr);break;}}while(Module32NextW(snapshot,&module));
        CloseHandle(snapshot);
        if(!vmm)throw std::runtime_error("BstkVMM backend not present; this build is unsupported");
        auto signature=host<std::array<unsigned char,8>>(vmm+EmulatorOffsets::SignatureRva);
        const auto& expected=EmulatorOffsets::Signature;
        if(signature!=expected)throw std::runtime_error("BstkVMM version differs from the supported build");
        cache_path_=Paths::cache();
        U64 cached_pid=0,cached_created=0,cached_uvm=0,cached_cr3=0,cached_base=0;
        std::ifstream cache(cache_path_);cache>>std::hex>>cached_pid>>cached_created>>cached_uvm>>cached_cr3>>cached_base;
        if(force_discovery||cached_pid!=pid_||cached_created!=created_||!accept_uvm(cached_uvm)) {
            std::cout<<"Discovering emulator memory (one-time scan)...\n";find_vm();
        }
        std::set<U64> seen;collect_ranges(host<U64>(vm_+EmulatorOffsets::RamTree),seen);
        if(ranges_.empty())throw std::runtime_error("Guest RAM ranges missing");
        bool found=false;
        if(!force_discovery&&cached_pid==pid_&&cached_created==created_&&cached_cr3&&cached_base) {
            cr3_=cached_cr3;found=validate_base(cached_base);if(found)base=cached_base;
        }
        if(!found)found=resolve_context(force_discovery?0:(cached_base?cached_base:Offsets::Build::DiscoveryHint),false);
        if(!found) {
            std::cout<<"Finding game address space and ELF image...\n";
            found=resolve_context(0,true);
        }
        if(!found)throw std::runtime_error("Game address space not found; keep the game active and retry");
        std::ofstream save(cache_path_);save<<std::hex<<pid_<<' '<<created_<<' '<<uvm_<<' '<<cr3_<<' '<<base<<'\n';
        std::cout<<"Windows-only reader | VM 0x"<<std::hex<<vm_<<" | CR3 0x"<<cr3_<<" | base 0x"<<base<<std::dec<<"\n";
    }
    catch(...) { if(process_){CloseHandle(process_);process_=nullptr;}throw; }
    }
    ~Memory(){if(process_)CloseHandle(process_);}
    Memory(const Memory&)=delete;Memory& operator=(const Memory&)=delete;
    void begin_frame(){virtual_pages_.clear();table_pages_.clear();data_pages_.clear();chunks_.clear();}
    U64 context()const{return cr3_;}
    U64 fresh_pointer(U64 address) {
        virtual_pages_.clear();table_pages_.clear();chunks_.clear();
        if((address&(Offsets::Managed::PointerSize-1))||(address&4095)>4096-Offsets::Managed::PointerSize)throw std::runtime_error("Unaligned pointer access");
        return Offsets::Managed::PointerSize==4?static_cast<U64>(host<unsigned>(translate(address))):host<U64>(translate(address));
    }
    bool refresh_context() {
        U64 previous=cr3_;
        try {
            std::vector<Range> previous_ranges=std::move(ranges_);std::set<U64> seen;
            try{collect_ranges(host<U64>(vm_+EmulatorOffsets::RamTree),seen);}catch(...){ranges_=std::move(previous_ranges);throw;}
            if(ranges_.empty())ranges_=std::move(previous_ranges);
            begin_frame();
            if(validate_base(base)){begin_frame();return true;}
            unsigned count=host<unsigned>(vm_+EmulatorOffsets::CpuCount);
            for(unsigned i=0;i<count&&i<64;++i) {
                U64 cpu=host<U64>(vm_+EmulatorOffsets::CpuTable+i*EmulatorOffsets::CpuStride),candidate=host<U64>(cpu+EmulatorOffsets::CpuCr3);
                if(!candidate)continue;cr3_=candidate;begin_frame();
                if(validate_base(base)){begin_frame();return true;}
            }
        }catch(const std::exception&){}
        cr3_=previous;begin_frame();return false;
    }

    std::vector<char> read(U64 address,size_t size) {
        if(address<4096||size>1024*1024)throw std::runtime_error("Invalid guest memory range");
        std::vector<char> result(size);size_t done=0;
        while(done<size) {
            U64 current=address+done,page=current&~U64(4095);
            size_t offset=static_cast<size_t>(current&4095),count=std::min(size-done,4096-offset);
            auto found=data_pages_.find(page);
            if(found==data_pages_.end()) {
                std::array<char,4096> bytes{};host_bytes(translate(page),bytes.data(),bytes.size());
                found=data_pages_.emplace(page,bytes).first;
            }
            std::memcpy(result.data()+done,found->second.data()+offset,count);done+=count;
        }return result;
    }
    template<class T> T get(U64 address){auto bytes=read(address,sizeof(T));T result{};std::memcpy(&result,bytes.data(),sizeof(T));return result;}
    U64 ptr(U64 address){return Offsets::Managed::PointerSize==4?static_cast<U64>(get<unsigned>(address)):get<U64>(address); }
};
