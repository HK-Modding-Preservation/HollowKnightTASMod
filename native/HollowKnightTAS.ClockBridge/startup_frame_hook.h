#ifndef HKTAS_STARTUP_FRAME_HOOK_H
#define HKTAS_STARTUP_FRAME_HOOK_H
#include <string.h>

/* This prototype is explicitly opt-in. Addresses were resolved from the
 * matching Unity PDB, not inferred from repeated clock calls. The injector
 * also verifies the full UnityPlayer SHA-256 against its build whitelist. */
static BOOL g_boot_frame_hook_enabled;
static void (__cdecl *g_boot_original_player_loop)(void);
static HANDLE g_boot_step;
static HANDLE g_boot_state_mapping;
static volatile LONG *g_boot_frame_state; /* completed, waiting, thread, hooked */
static BOOL g_boot_loop_active;

static void wait_boot_frame_command(void)
{
    HANDLE handles[3] = {g_boot_continue, g_boot_owner, g_boot_step};
    for (;;) {
        DWORD result = MsgWaitForMultipleObjectsEx(3, handles, INFINITE,
            QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        if (result != WAIT_OBJECT_0 + 3) return;
        MSG message;
        /* Keep the native window responsive without executing PlayerLoop.
         * Reentrant loop requests during DispatchMessage are suppressed by
         * g_boot_loop_active below. Bound the batch to avoid input starvation. */
        for (int i = 0; i < 128 && PeekMessageW(&message, NULL, 0, 0, PM_REMOVE); ++i) {
            if (message.message == WM_QUIT) {
                PostQuitMessage((int)message.wParam);
                return;
            }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
}

static void __cdecl boot_player_loop(void)
{
    if (g_boot_loop_active) return;
    g_boot_loop_active = TRUE;
    if (g_boot_frame_state && WaitForSingleObject(g_boot_continue, 0) != WAIT_OBJECT_0)
    {
        InterlockedExchange(&g_boot_frame_state[2], (LONG)GetCurrentThreadId());
        InterlockedExchange(&g_boot_frame_state[1], 1);
        SetEvent(g_boot_ready);
        wait_boot_frame_command();
        ResetEvent(g_boot_ready);
        InterlockedExchange(&g_boot_frame_state[1], 0);
    }
    advance_boot_frame_clock();
    g_boot_original_player_loop();
    if (g_boot_frame_state) InterlockedIncrement(&g_boot_frame_state[0]);
    g_boot_loop_active = FALSE;
}

static BOOL install_boot_frame_hook(void)
{
    wchar_t enabled[4], token[40], name[100];
    if (GetEnvironmentVariableW(L"HKTAS_BOOT_FRAME_GATE", enabled, 4) == 0) return TRUE;
    if (wcscmp(enabled, L"1") != 0 || !g_boot_ready) return FALSE;
    BYTE *base = (BYTE *)GetModuleHandleW(L"UnityPlayer.dll");
    if (!base) return FALSE;
    IMAGE_DOS_HEADER *dos = (IMAGE_DOS_HEADER *)base;
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + dos->e_lfanew);
    IMAGE_DATA_DIRECTORY debug = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    const BYTE expected_guid[16] = {0x90,0xbe,0x34,0x03,0xdd,0x63,0x44,0x46,0xbe,0xe1,0xfc,0x8d,0x56,0xc8,0x40,0xc2};
    BOOL matched = FALSE;
    for (DWORD i = 0; i < debug.Size / sizeof(IMAGE_DEBUG_DIRECTORY); ++i) {
        IMAGE_DEBUG_DIRECTORY *entry = (IMAGE_DEBUG_DIRECTORY *)(base + debug.VirtualAddress) + i;
        if (entry->Type != IMAGE_DEBUG_TYPE_CODEVIEW || entry->SizeOfData < 24) continue;
        BYTE *cv = base + entry->AddressOfRawData;
        if (memcmp(cv, "RSDS", 4) == 0 && memcmp(cv + 4, expected_guid, 16) == 0
            && *(DWORD *)(cv + 20) == 1) matched = TRUE;
    }
    if (!matched) return FALSE;
    BYTE *site = base + 0x5231e5;
    const BYTE expected_call[5] = {0xe8,0xd6,0x92,0x23,0x00};
    if (memcmp(site, expected_call, sizeof(expected_call)) != 0) return FALSE;
    GetEnvironmentVariableW(L"HKTAS_BOOT_GATE_TOKEN", token, 40);
    wsprintfW(name, L"Local\\HKTAS.Boot.%s.Step", token);
    g_boot_step = OpenEventW(SYNCHRONIZE, FALSE, name);
    wsprintfW(name, L"Local\\HKTAS.Boot.%s.State", token);
    g_boot_state_mapping = OpenFileMappingW(FILE_MAP_WRITE, FALSE, name);
    if (!g_boot_step || !g_boot_state_mapping) return FALSE;
    g_boot_frame_state = (volatile LONG *)MapViewOfFile(g_boot_state_mapping, FILE_MAP_WRITE, 0, 0, 16);
    if (!g_boot_frame_state) return FALSE;

    /* Replace one five-byte CALL with a nearby absolute-jump relay. No
     * function prologue relocation, no guessed instruction lengths. This
     * executes before the primary thread enters Unity's main loop. */
    SYSTEM_INFO info;
    GetSystemInfo(&info);
    BYTE *relay = NULL;
    uintptr_t aligned = (uintptr_t)base & ~((uintptr_t)info.dwAllocationGranularity - 1);
    for (uintptr_t delta = info.dwAllocationGranularity; delta < 0x70000000; delta += info.dwAllocationGranularity) {
        relay = VirtualAlloc((void *)(aligned + delta), 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (relay) break;
    }
    if (!relay) return FALSE;
    relay[0] = 0xff; relay[1] = 0x25;
    memset(relay + 2, 0, 4);
    void (*target)(void) = boot_player_loop;
    memcpy(relay + 6, &target, sizeof(target));
    DWORD old;
    if (!VirtualProtect(relay, 4096, PAGE_EXECUTE_READ, &old)) return FALSE;
    FlushInstructionCache(GetCurrentProcess(), relay, 14);
    g_boot_original_player_loop = (void (__cdecl *)(void))(base + 0x75c4c0);
    if (!VirtualProtect(site, 5, PAGE_EXECUTE_READWRITE, &old)) return FALSE;
    int32_t relative = (int32_t)((intptr_t)relay - (intptr_t)(site + 5));
    memcpy(site + 1, &relative, sizeof(relative));
    DWORD ignored;
    if (!VirtualProtect(site, 5, old, &ignored)) return FALSE;
    FlushInstructionCache(GetCurrentProcess(), site, 5);
    g_boot_frame_hook_enabled = TRUE;
    InterlockedExchange(&g_boot_frame_state[3], 1);
    return TRUE;
}
#endif
