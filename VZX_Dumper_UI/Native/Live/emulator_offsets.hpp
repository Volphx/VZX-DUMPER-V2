#pragma once
#include <array>
#include <cstdint>

// Windows x64 BstkVMM profile. Separate from Android/game offsets.
namespace EmulatorOffsets {
inline constexpr char Version[]="BstkVMM 6.1.36.156792";
inline constexpr std::uint64_t SignatureRva=0x200C20;
inline constexpr std::array<unsigned char,8> Signature{0x48,0x8B,0x81,0x00,0xFE,0x11,0x00,0xC3};
inline constexpr unsigned UvmMagic=0x19700823;
inline constexpr std::uint64_t UvmVm=0x8,CpuCount=0x3C,CpuTable=0x11FE00,CpuStride=8;
inline constexpr std::uint64_t CpuPagingMode=0x1101C,CpuCr3=0x11020;
inline constexpr unsigned char PagingMode=5;
inline constexpr std::uint64_t RamTree=0x106098,ChunkTree=0x106C60,ZeroPage=0x104000;
namespace Range {
inline constexpr unsigned StartIndex=0,SizeIndex=1,HostIndex=6,LeftIndex=10,RightIndex=11;
inline constexpr std::uint64_t PageTable=0x70,DescriptorStride=16;
inline constexpr unsigned PageIdIndex=2,ChunkShift=9,PageMask=511;
}
namespace Chunk {inline constexpr std::uint64_t Left=0,Right=8,Key=0x10,Host=0x28;}
}
