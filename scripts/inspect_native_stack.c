#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dbghelp.h>
#include <stdio.h>
#include <stdlib.h>

/* Diagnostic only: briefly suspend one explicitly selected thread to copy
 * its stack, then resume before resolving symbols or printing output. */
int main(int argc, char **argv)
{
    if (argc != 4) return 2;
    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, FALSE, strtoul(argv[1], NULL, 10));
    HANDLE thread = OpenThread(THREAD_GET_CONTEXT | THREAD_SUSPEND_RESUME | THREAD_QUERY_INFORMATION,
        FALSE, strtoul(argv[2], NULL, 10));
    if (!process || !thread) return 3;
    if (GetProcessIdOfThread(thread) != GetProcessId(process)) return 4;
    SymSetOptions(SYMOPT_UNDNAME | SYMOPT_DEFERRED_LOADS | SYMOPT_FAIL_CRITICAL_ERRORS);
    if (!SymInitialize(process, argv[3], TRUE)) return 5;
    CONTEXT context = {0};
    context.ContextFlags = CONTEXT_FULL;
    if (SuspendThread(thread) == (DWORD)-1) return 6;
    DWORD64 addresses[24];
    int count = 0;
    if (GetThreadContext(thread, &context)) {
        STACKFRAME64 frame = {0};
        frame.AddrPC.Offset = context.Rip;
        frame.AddrPC.Mode = AddrModeFlat;
        frame.AddrStack.Offset = context.Rsp;
        frame.AddrStack.Mode = AddrModeFlat;
        frame.AddrFrame.Offset = context.Rbp;
        frame.AddrFrame.Mode = AddrModeFlat;
        addresses[count++] = context.Rip;
        while (count < 24 && StackWalk64(IMAGE_FILE_MACHINE_AMD64, process, thread, &frame, &context,
            NULL, SymFunctionTableAccess64, SymGetModuleBase64, NULL)) {
            if (!frame.AddrPC.Offset) break;
            addresses[count++] = frame.AddrPC.Offset;
        }
    }
    ResumeThread(thread);
    for (int i = 0; i < count; ++i) {
        char storage[sizeof(SYMBOL_INFO) + MAX_SYM_NAME] = {0};
        PSYMBOL_INFO symbol = (PSYMBOL_INFO)storage;
        symbol->SizeOfStruct = sizeof(SYMBOL_INFO);
        symbol->MaxNameLen = MAX_SYM_NAME;
        DWORD64 displacement = 0;
        if (SymFromAddr(process, addresses[i], &displacement, symbol))
            printf("%s+0x%llx\n", symbol->Name, (unsigned long long)displacement);
        else printf("0x%llx\n", (unsigned long long)addresses[i]);
    }
    SymCleanup(process);
    CloseHandle(thread); CloseHandle(process);
    return count ? 0 : 7;
}
