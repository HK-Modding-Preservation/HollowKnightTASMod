#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dbghelp.h>
#include <stdio.h>

static BOOL CALLBACK print_symbol(PSYMBOL_INFO symbol, ULONG size, PVOID context)
{
    (void)size;
    DWORD64 base = *(DWORD64 *)context;
    printf("0x%llx\t%lu\t%s\n", (unsigned long long)(symbol->Address - base),
        (unsigned long)symbol->Size, symbol->Name);
    return TRUE;
}

int main(int argc, char **argv)
{
    if (argc != 4) {
        fprintf(stderr, "Usage: inspect_native_symbols image.dll symbol-directory symbol-mask\n");
        return 2;
    }
    HANDLE process = GetCurrentProcess();
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_FAIL_CRITICAL_ERRORS);
    if (!SymInitialize(process, argv[2], FALSE)) return 3;
    DWORD64 base = SymLoadModuleEx(process, NULL, argv[1], NULL, 0, 0, NULL, 0);
    if (!base) {
        fprintf(stderr, "SymLoadModuleEx failed: %lu\n", (unsigned long)GetLastError());
        SymCleanup(process);
        return 4;
    }
    BOOL result = SymEnumSymbols(process, base, argv[3], print_symbol, &base);
    if (!result) fprintf(stderr, "SymEnumSymbols failed: %lu\n", (unsigned long)GetLastError());
    IMAGEHLP_MODULE64 module = {0};
    module.SizeOfStruct = sizeof(module);
    if (SymGetModuleInfo64(process, base, &module)) {
        fprintf(stderr, "PDB=%s age=%lu unmatched=%d symbolType=%d\n",
            module.LoadedPdbName, (unsigned long)module.PdbAge,
            module.PdbUnmatched, (int)module.SymType);
        if (module.PdbUnmatched || module.SymType != SymPdb) result = FALSE;
    } else result = FALSE;
    SymCleanup(process);
    return result ? 0 : 5;
}
