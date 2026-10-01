#ifndef HKTAS_LOADING_STARTUP_CLOCK_PROBE_H
#define HKTAS_LOADING_STARTUP_CLOCK_PROBE_H

/* Versioned full-run startup clock on the pinned Unity build. Startup I/O loops
 * must not set the gameplay clock's epoch or scheduling phase. No state
 * package is persisted; the three diagnostic rows contain observations. */
static BOOL g_loading_startup_clock_enabled;
static BOOL g_loading_startup_clock_initialized;
static BOOL g_loading_startup_clock_released;
static BOOL g_loading_startup_clock_frozen_frame;
static BYTE *g_loading_startup_time_manager;
static DWORD g_loading_startup_clock_wall_begin;
static unsigned g_loading_startup_clock_frozen_loops;

static BOOL loading_startup_writable(void *pointer, size_t length)
{
    MEMORY_BASIC_INFORMATION info;
    if (VirtualQuery(pointer, &info, sizeof(info)) != sizeof(info)
        || info.State != MEM_COMMIT || (info.Protect & PAGE_GUARD)) return FALSE;
    DWORD access = info.Protect & 0xffu;
    if (access != PAGE_READWRITE && access != PAGE_WRITECOPY
        && access != PAGE_EXECUTE_READWRITE && access != PAGE_EXECUTE_WRITECOPY) return FALSE;
    uintptr_t begin = (uintptr_t)pointer;
    uintptr_t end = (uintptr_t)info.BaseAddress + info.RegionSize;
    return begin <= end && length <= end - begin;
}

static void loading_startup_clock_diagnostic(const char *phase)
{
    wchar_t directory[MAX_PATH], path[MAX_PATH];
    DWORD length = GetEnvironmentVariableW(L"HKTAS_LOADING_PROBE_OUTPUT", directory, MAX_PATH);
    if (!length || length >= MAX_PATH - 64) return;
    wsprintfW(path, L"%s\\startup-clock-native-%lu.jsonl", directory, GetCurrentProcessId());
    HANDLE file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return;
    char row[384];
    uint64_t time_bits = 0, fixed_bits = 0;
    LONGLONG engine_frame = -1;
    if (g_loading_startup_time_manager) {
        memcpy(&time_bits, g_loading_startup_time_manager + 0x90, 8);
        memcpy(&fixed_bits, g_loading_startup_time_manager + 0x30, 8);
        memcpy(&engine_frame, g_loading_startup_time_manager + 0xc8, 8);
    }
    int size = snprintf(row, sizeof(row),
        "{\"phase\":\"%s\",\"process\":%lu,\"engineFrame\":%lld,\"timeBits\":\"%016llx\","
        "\"fixedBits\":\"%016llx\",\"frozenLoops\":%u,\"nativeCompleted\":%llu}\n",
        phase, GetCurrentProcessId(), (long long)engine_frame, (unsigned long long)time_bits,
        (unsigned long long)fixed_bits, g_loading_startup_clock_frozen_loops,
        (unsigned long long)hktas_get_completed_player_loops());
    if (size > 0 && (size_t)size < sizeof(row)) {
        DWORD written;
        WriteFile(file, row, (DWORD)size, &written, NULL);
    }
    CloseHandle(file);
}

static BOOL configure_loading_startup_clock_probe(void)
{
    if (!g_v2_gate_enabled) return TRUE;
    g_loading_startup_clock_enabled = TRUE;
    return TRUE;
}

static void zero_loading_startup_time_fields(void)
{
    BYTE *manager = g_loading_startup_time_manager;
    /* Three TimeHolders have current/last/unscaled times at offsets 0,8,16.
     * Preserve configured fixedDeltaTime (0x48); its unit is not a clock epoch. */
    memset(manager + 0x30, 0, 24);
    memset(manager + 0x60, 0, 48);
    memset(manager + 0x90, 0, 48);
    memset(manager + 0xc8, 0, 16); /* frame/render/cull counters */
    memset(manager + 0xd8, 0, 4);  /* captureDeltaTime while bootstrap is pending */
    memset(manager + 0xe0, 0, 24); /* game/real epochs and scene offset */
    manager[0xc0] = manager[0xc1] = 1;
    manager[0xc2] = 0; /* No first-fixed-frame work while startup is held. */
    memset(manager + 0x108, 0, 808); /* presentation sync time/ring */
}

static BOOL prepare_loading_startup_clock_probe(void)
{
    if (!g_loading_startup_clock_enabled || g_loading_startup_clock_released) return TRUE;
    if (!g_loading_startup_clock_initialized) {
        BYTE *engine = (BYTE *)GetModuleHandleW(L"UnityPlayer.dll");
        const BYTE getter[] = {0xb9,0x07,0x00,0x00,0x00,0xe9,0xc6,0x79,0x04,0x00};
        if (!engine || memcmp(engine + 0x52b330, getter, sizeof(getter)) != 0) return FALSE;
        BYTE *(__cdecl *get_manager)(void) = (BYTE *(__cdecl *)(void))(engine + 0x52b330);
        double (__cdecl *get_raw_time)(void) = (double (__cdecl *)(void))(engine + 0x7775c0);
        g_loading_startup_time_manager = get_manager();
        if (!loading_startup_writable(g_loading_startup_time_manager, 0x430)) return FALSE;
        /* Initialize native lazy timer storage before changing its integer
         * origin. Subtraction must happen before conversion to double. */
        (void)get_raw_time();
        LONGLONG *counter_origin = (LONGLONG *)(engine + 0x1a41260);
        double *scripting_origin = *(double **)(engine + 0x1967800);
        if (!loading_startup_writable(counter_origin, 8) || !loading_startup_writable(scripting_origin, 8)) return FALSE;
        AcquireSRWLockShared(&g_clock_lock);
        LONGLONG anchor = g_deterministic_clock_anchor.QuadPart;
        ReleaseSRWLockShared(&g_clock_lock);
        loading_startup_clock_diagnostic("before-initialize");
        InterlockedExchange64(counter_origin, anchor);
        *scripting_origin = 0.0;
        zero_loading_startup_time_fields();
        if (get_raw_time() != 0.0) return FALSE;
        g_loading_startup_clock_wall_begin = GetTickCount();
        g_loading_startup_clock_initialized = TRUE;
        loading_startup_clock_diagnostic("initialized");
    }
    if (InterlockedCompareExchange(&g_v2_state->runtime_input_ready, 0, 0) == 1) {
        g_loading_startup_clock_released = TRUE;
        g_loading_startup_clock_frozen_frame = FALSE;
        g_loading_startup_time_manager[0xc2] = 1;
        loading_startup_clock_diagnostic("released");
        return TRUE;
    }
    if (GetTickCount() - g_loading_startup_clock_wall_begin > 30000u) {
        loading_startup_clock_diagnostic("timeout");
        return FALSE;
    }
    g_loading_startup_clock_frozen_frame = TRUE;
    zero_loading_startup_time_fields();
    ++g_loading_startup_clock_frozen_loops;
    return TRUE;
}

static void finish_loading_startup_clock_probe(void)
{
    if (g_loading_startup_clock_frozen_frame) zero_loading_startup_time_fields();
}

#endif
