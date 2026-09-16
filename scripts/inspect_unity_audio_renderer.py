"""Read-only audit of this game's Unity AudioRenderer implementation (pefile, capstone)."""
import argparse
import hashlib
import struct

import capstone
import pefile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("unity_player")
    args = parser.parse_args()
    pe = pefile.PE(args.unity_player)
    data = pe.__data__
    base = pe.OPTIONAL_HEADER.ImageBase
    digest = hashlib.sha256(data).hexdigest()
    print("sha256", digest)
    if digest != "d97e92a7640b10580b4e60139eacf01828f74baaef53f75e08b9fdd6193fbe5e":
        raise ValueError("Unsupported UnityPlayer build; fixed body ranges must be re-audited")
    qword = lambda offset: struct.unpack_from("<Q", data, offset)[0]
    name = b"UnityEngine.AudioRenderer::Internal_AudioRenderer_Start\0"
    name_offset = data.find(name)
    if name_offset < 0:
        raise ValueError("AudioRenderer registration name is missing")
    name_va = base + pe.get_rva_from_offset(name_offset)
    entry = data.find(struct.pack("<Q", name_va))
    if entry < 0:
        raise ValueError("AudioRenderer registration table is missing")
    read_only = next(section for section in pe.sections if section.Name.rstrip(b"\0") == b".rdata")
    lo, hi = base + read_only.VirtualAddress, base + read_only.VirtualAddress + read_only.Misc_VirtualSize
    start = entry
    while start >= 8 and lo <= qword(start - 8) < hi:
        start -= 8
    end = entry
    while lo <= qword(end) < hi:
        end += 8
    if qword(end) != 0:
        raise ValueError("Unexpected registration table terminator")
    functions = end + 8
    disassembler = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    for index in range((entry - start) // 8, (entry - start) // 8 + 5):
        offset = pe.get_offset_from_rva(qword(start + index * 8) - base)
        label = data[offset:data.find(b"\0", offset)].decode()
        target = qword(functions + index * 8)
        print(label, "RVA", hex(target - base))
        offset = pe.get_offset_from_rva(target - base)
        for instruction in disassembler.disasm(data[offset:offset + 16], target):
            print(hex(instruction.address - base), instruction.mnemonic, instruction.op_str)
            if instruction.mnemonic in ("jmp", "ret"):
                break
    # Audited body ranges for the supported installed build; no patching or execution.
    # These explain the complete-DSP-block loop and accumulator arithmetic.
    for rva, size in [(0xA9848E, 100), (0xA985CF, 240), (0xA98B27, 24)]:
        offset = pe.get_offset_from_rva(rva)
        print("BODY", hex(rva))
        for instruction in disassembler.disasm(data[offset:offset + size], base + rva):
            print(hex(instruction.address - base), instruction.mnemonic, instruction.op_str)


if __name__ == "__main__":
    main()
