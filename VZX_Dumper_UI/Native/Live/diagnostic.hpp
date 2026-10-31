#pragma once
#include "readonly_memory.hpp"
#include <memory>
#include <mutex>
#include <cmath>
#include <map>
#include "field_recovery.hpp"
namespace LiveTest {
struct Profile {
    std::map<std::string,U64> fields;bool arm32=false;
    U64 required(const char* key)const{auto i=fields.find(key);if(i==fields.end())throw std::runtime_error(std::string("Offset essencial ausente: ")+key);return i->second;}
    bool has(const char* key)const{return fields.contains(key);}
    U64 pointer=8,native=0x10,staticFields=0xB8,arrayData=0x20,arrayLength=0x18;
    U64 hierarchy=0x38,index=0x40,matrices=0x18,parents=0x20,view=0x80,projection=0xC0;
    void load(const wchar_t* library,U64 rva,const char* rows){
        if(!rva)throw std::runtime_error("RVA nao identificado");
        std::istringstream input(rows?rows:"");std::string line;
        while(std::getline(input,line)){
            auto separator=line.find('=');if(separator==std::string::npos)throw std::runtime_error("Perfil invalido");
            const auto key=line.substr(0,separator);size_t parsed=0;const auto value=std::stoull(line.substr(separator+1),&parsed,16);
            if(parsed!=line.size()-separator-1||value>0x1000000)throw std::runtime_error("Offset fora dos limites");
            fields.emplace(key,value);
        }
        std::ifstream elf(library,std::ios::binary);std::array<unsigned,16> header{};
        if(!elf.read(reinterpret_cast<char*>(header.data()),sizeof(header))||header[0]!=0x464C457F)throw std::runtime_error("ELF selecionado invalido");
        auto* bytes=reinterpret_cast<unsigned char*>(header.data());unsigned short machine=0;std::memcpy(&machine,bytes+18,2);
        arm32=bytes[4]==1&&machine==40;if(!arm32&&!(bytes[4]==2&&machine==183))throw std::runtime_error("ELF ARMv7/ARM64 esperado");
        if(arm32){pointer=4;native=8;staticFields=0x5C;arrayData=0x10;arrayLength=0xC;hierarchy=0x20;index=0x24;parents=0x1C;view=0x68;projection=0xA8;}
        for(auto key:{"Game.CurrentGame","Game.Match","Match.LocalPlayer","Match.Entities","Player.FollowCamera","Camera.ManagedCamera","Player.AvatarManager","Avatar.Instance","Skeleton.Node01","Skeleton.Node06"})required(key);
        if(has("Managed.NativePointer")&&required("Managed.NativePointer")!=native)throw std::runtime_error("Layout nativo Unity incompativel");
        Offsets::Build::GameClassRva=rva;Offsets::Build::ElfArchitecture=header[4];Offsets::Build::ElfEntryMarker=header[6];
        Offsets::Build::DiscoveryHint=0;Offsets::Managed::PointerSize=pointer;Offsets::Managed::ClassStaticFields=staticFields;
    }
};
struct Vec {float x=0,y=0,z=0;};
inline bool finite(Vec v){return std::isfinite(v.x)&&std::isfinite(v.y)&&std::isfinite(v.z);}
inline float distance(Vec a,Vec b){const auto x=a.x-b.x,y=a.y-b.y,z=a.z-b.z;return std::sqrt(x*x+y*y+z*z);}
inline Vec rotate(const std::array<float,12>& t,Vec v){
    Vec s{v.x*t[8],v.y*t[9],v.z*t[10]};Vec q{2*(t[5]*s.z-t[6]*s.y),2*(t[6]*s.x-t[4]*s.z),2*(t[4]*s.y-t[5]*s.x)};
    return {t[0]+s.x+t[7]*q.x+t[5]*q.z-t[6]*q.y,t[1]+s.y+t[7]*q.y+t[6]*q.x-t[4]*q.z,t[2]+s.z+t[7]*q.z+t[4]*q.y-t[5]*q.x};
}
inline U64 ptr(Memory& m,U64 address){auto p=m.ptr(address);if(p<0x10000||p>=0x0000800000000000ULL||(p&(Offsets::Managed::PointerSize-1)))throw std::runtime_error("Ponteiro invalido no perfil atual");return p;}
inline Vec position(Memory& m,const Profile& p,U64 managed){
    auto native=ptr(m,managed+p.native),data=ptr(m,native+p.hierarchy),matrices=ptr(m,data+p.matrices),parents=ptr(m,data+p.parents);
    int index=m.get<int>(native+p.index);if(index<0||index>100000)throw std::runtime_error("Indice de Transform invalido");
    auto t=m.get<std::array<float,12>>(matrices+U64(index)*48);Vec value{t[0],t[1],t[2]};
    std::set<int> seen{index};int parent=m.get<int>(parents+U64(index)*4);
    for(int depth=0;parent>=0;++depth){if(depth>=96||parent>100000||!seen.insert(parent).second)throw std::runtime_error("Hierarquia de ossos invalida");
        value=rotate(m.get<std::array<float,12>>(matrices+U64(parent)*48),value);parent=m.get<int>(parents+U64(parent)*4);}
    if(!finite(value))throw std::runtime_error("Posicao invalida");return value;
}
inline Vec bone(Memory& m,const Profile& p,U64 entity,const char* key){
    auto node=ptr(m,entity+p.required(key));auto transform=ptr(m,node+(p.has("Transform.NodeTransform")?p.required("Transform.NodeTransform"):p.native));return position(m,p,transform);
}
using Matrix=std::array<float,16>;
struct Point {float x=0,y=0;};
inline std::array<float,4> multiply(const Matrix& a,std::array<float,4> v){return {a[0]*v[0]+a[4]*v[1]+a[8]*v[2]+a[12]*v[3],a[1]*v[0]+a[5]*v[1]+a[9]*v[2]+a[13]*v[3],a[2]*v[0]+a[6]*v[1]+a[10]*v[2]+a[14]*v[3],a[3]*v[0]+a[7]*v[1]+a[11]*v[2]+a[15]*v[3]};}
inline bool project(Vec v,const Matrix& view,const Matrix& projection,Point& point){auto c=multiply(projection,multiply(view,{v.x,v.y,v.z,1}));if(!std::isfinite(c[3])||c[3]<.05f)return false;point={(1+c[0]/c[3])*.5f,(1-c[1]/c[3])*.5f};return std::isfinite(point.x)&&std::isfinite(point.y);}
inline unsigned attribute(Memory& m,const Profile& p,U64 entity,unsigned index){
    auto pool=ptr(m,entity+p.required("Player.DataPool")),array=ptr(m,pool+p.required("Attributes.Array"));
    const int count=m.get<int>(pool+p.required("Attributes.Count"));const auto length=m.ptr(array+p.arrayLength);
    if(count<=int(index)||count>100000||length<=index||length>100000)throw std::runtime_error("Array de atributos invalido");
    return m.get<unsigned short>(ptr(m,array+p.arrayData+index*p.pointer)+p.required("Attributes.Value"));
}
inline std::string name(Memory& m,const Profile& p,U64 entity){
    if(!p.has("Player.NickName")||!p.has("Managed.StringLength")||!p.has("Managed.StringData"))return {};
    auto string=m.ptr(entity+p.required("Player.NickName"));if(!string)return {};
    int count=m.get<int>(string+p.required("Managed.StringLength"));if(count<1||count>128)return {};
    auto raw=m.read(string+p.required("Managed.StringData"),count*2);std::wstring text(count,L' ');std::memcpy(text.data(),raw.data(),raw.size());
    int size=WideCharToMultiByte(CP_UTF8,0,text.data(),count,nullptr,0,nullptr,nullptr);std::string result(size,' ');if(size)WideCharToMultiByte(CP_UTF8,0,text.data(),count,result.data(),size,nullptr,nullptr);return result;
}
struct Session {Profile profile;std::unique_ptr<Memory> memory;DWORD pid=0;};
inline std::unique_ptr<Session> session;inline std::mutex mutex;
inline std::string start(const wchar_t* library,U64 rva,const char* rows,DWORD pid){
    std::lock_guard guard(mutex);session.reset();auto next=std::make_unique<Session>();next->profile.load(library,rva,rows);next->pid=pid;next->memory=std::make_unique<Memory>(pid);
    auto& p=next->profile;auto& mem=*next->memory;unsigned recovered=0;
    if(p.arm32&&(!p.has("Player.CachedTransform")||!p.has("Player.DataPool")||!p.has("Attributes.Array")||!p.has("Attributes.Count")||!p.has("Attributes.Value")||!p.has("Avatar.IsVisible"))) {
        auto metadata=fs::path(library).parent_path()/L"global-metadata.dat";
        if(!fs::is_regular_file(metadata))throw std::runtime_error("Recuperacao V7A requer global-metadata.dat ao lado da biblioteca selecionada");
        mem.begin_frame();auto klass=ptr(mem,ptr(mem,mem.base+Offsets::Build::GameClassRva));
        auto game=ptr(mem,ptr(mem,klass+p.staticFields)+p.required("Game.CurrentGame"));
        auto local=ptr(mem,ptr(mem,game+p.required("Game.Match"))+p.required("Match.LocalPlayer"));
        recovered=FieldRecovery::recover(mem,p.fields,local,metadata);
    }
    for(auto key:{"Player.CachedTransform","Player.DataPool","Attributes.Array","Attributes.Count","Attributes.Value","Avatar.IsVisible"})p.required(key);
    session=std::move(next);
    return "{\"valid\":true,\"pid\":"+std::to_string(pid)+",\"status\":\"Conectado. ESP somente leitura; campos ausentes recuperados de FieldInfo quando necessarios. Layouts Unity sujeitos a validacao.\"}";
}
inline std::string frame(){
    std::lock_guard guard(mutex);if(!session)throw std::runtime_error("ESP nao iniciado");auto& m=*session->memory;const auto& p=session->profile;
    m.begin_frame();auto klass=ptr(m,ptr(m,m.base+Offsets::Build::GameClassRva)),statics=ptr(m,klass+p.staticFields),game=ptr(m,statics+p.required("Game.CurrentGame"));
    auto match=ptr(m,game+p.required("Game.Match")),local=ptr(m,match+p.required("Match.LocalPlayer"));
    auto camera=ptr(m,ptr(m,ptr(m,local+p.required("Player.FollowCamera"))+p.required("Camera.ManagedCamera"))+p.native);
    auto view=m.get<Matrix>(camera+p.view),projection=m.get<Matrix>(camera+p.projection);
    if(std::abs(view[15]-1)>.01f||std::abs(projection[11]+1)>.01f)throw std::runtime_error("Camera nao validada; nenhum desenho aplicado");
    for(auto n:view)if(!std::isfinite(n))throw std::runtime_error("Camera invalida");for(auto n:projection)if(!std::isfinite(n))throw std::runtime_error("Projecao invalida");
    auto origin=position(m,p,ptr(m,local+p.required("Player.CachedTransform")));
    auto dictionary=ptr(m,match+p.required("Match.Entities")),entries=ptr(m,dictionary+(p.arm32?0xC:0x18));int count=m.get<int>(dictionary+(p.arm32?0x10:0x20));
    auto length=m.ptr(entries+p.arrayLength);if(count<0||count>1000||length<U64(count)||length>100000)throw std::runtime_error("Dicionario de entidades invalido");
    int localTeam=p.has("Player.Team")?m.get<int>(local+p.required("Player.Team")):-1;
    std::ostringstream boxes;bool comma=false;unsigned visible=0,accepted=0,named=0,resolved=0,avatars=0,hidden=0;std::set<U64> seen;
    for(int i=0;i<count;++i)try{
        // The game's ID-key dictionary stores 40-byte entries on both validated architectures.
        auto entry=entries+p.arrayData+U64(i)*40;if(m.get<int>(entry)<0)continue;auto entity=ptr(m,entry+32);
        if(entity==local||!seen.insert(entity).second)continue;++resolved;
        auto avatar=ptr(m,ptr(m,entity+p.required("Player.AvatarManager"))+p.required("Avatar.Instance"));
        ++avatars;if(m.get<unsigned char>(avatar+p.required("Avatar.IsVisible"))!=1){++hidden;continue;}++visible;
        if(p.has("Player.Team")){auto team=m.get<int>(entity+p.required("Player.Team"));if(team>0&&localTeam>0&&team==localTeam)continue;}
        unsigned hp=attribute(m,p,entity,0),maximum=attribute(m,p,entity,1);if(!hp||hp>maximum||maximum>32767)continue;
        Vec root=bone(m,p,entity,"Skeleton.Node06"),head=bone(m,p,entity,"Skeleton.Node01");float dist=distance(origin,head),height=distance(root,head);
        if(!std::isfinite(dist)||dist<.5f||dist>500||height<.3f||height>3.5f)continue;
        Point top{},bottom{};head.y+=.16f;if(!project(head,view,projection,top)||!project(root,view,projection,bottom))continue;
        float h=bottom.y-top.y;if(h<=0||h>2||top.x<-.2f||top.x>1.2f||bottom.y<0||top.y>1)continue;
        std::string label;try{label=name(m,p,entity);}catch(...){}if(!label.empty())++named;
        if(comma)boxes<<',';comma=true;++accepted;
        boxes<<"{\"x\":"<<top.x<<",\"top\":"<<top.y<<",\"bottom\":"<<bottom.y<<",\"hp\":"<<hp<<",\"maxHp\":"<<maximum<<",\"distance\":"<<dist<<",\"name\":"<<Search::json(label)<<",\"bones\":[";
        bool lineComma=false;
        for(auto [a,b]:{std::pair{"Skeleton.Node01","Skeleton.Node03"},{"Skeleton.Node03","Skeleton.Node02"},{"Skeleton.Node03","Skeleton.Node13"},{"Skeleton.Node13","Skeleton.Node18"},{"Skeleton.Node18","Skeleton.Node16"},{"Skeleton.Node03","Skeleton.Node14"},{"Skeleton.Node14","Skeleton.Node17"},{"Skeleton.Node17","Skeleton.Node15"},{"Skeleton.Node02","Skeleton.Node08"},{"Skeleton.Node02","Skeleton.Node09"}})try{
            if(!p.has(a)||!p.has(b))continue;auto start=bone(m,p,entity,a),end=bone(m,p,entity,b);if(distance(start,end)>2.2f)continue;Point s{},e{};
            if(!project(start,view,projection,s)||!project(end,view,projection,e))continue;if(lineComma)boxes<<',';lineComma=true;boxes<<'['<<s.x<<','<<s.y<<','<<e.x<<','<<e.y<<']';
        }catch(...){}
        boxes<<"]}";
    }catch(...){}
    if(m.fresh_pointer(match+p.required("Match.LocalPlayer"))!=local)throw std::runtime_error("Partida alterada durante a leitura");
    std::ostringstream json;json<<"{\"valid\":true,\"pid\":"<<session->pid<<",\"entities\":"<<count<<",\"visible\":"<<visible<<",\"accepted\":"<<accepted<<",\"named\":"<<named
        <<",\"status\":\"ESP teste: "<<accepted<<" alvos / "<<count<<" entidades; "<<resolved<<" referencias, "<<avatars<<" avatares, "<<hidden<<" ocultos. Somente leitura.\",\"boxes\":["<<boxes.str()<<"]}";return json.str();
}
inline void stop(){std::lock_guard guard(mutex);session.reset();}
}
