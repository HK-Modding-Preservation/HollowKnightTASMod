#ifndef HKTAS_STARTUP_GATE_H
#define HKTAS_STARTUP_GATE_H

/* Opt-in pre-frame handshake. No waiting is permitted from DllMain.
 * This gate is separate from the later completed-frame Runtime gate. */
static HANDLE g_boot_ready;
static HANDLE g_boot_continue;
static HANDLE g_boot_owner;
static volatile LONG g_boot_entered;

static void close_boot_gate(void)
{
    if (g_boot_ready) CloseHandle(g_boot_ready);
    if (g_boot_continue) CloseHandle(g_boot_continue);
    if (g_boot_owner) CloseHandle(g_boot_owner);
    g_boot_ready = g_boot_continue = g_boot_owner = NULL;
}

static BOOL configure_boot_gate(void)
{
    wchar_t token[40], owner[24], name[100];
    DWORD length = GetEnvironmentVariableW(L"HKTAS_BOOT_GATE_TOKEN", token, 40);
    if (length == 0) return TRUE; /* Existing launches are unchanged. */
    if (length != 32) return FALSE;
    for (DWORD i = 0; i < length; ++i)
        if (!((token[i] >= L'0' && token[i] <= L'9')
              || (token[i] >= L'a' && token[i] <= L'f'))) return FALSE;
    length = GetEnvironmentVariableW(L"HKTAS_BOOT_GATE_OWNER", owner, 24);
    if (length == 0 || length >= 24) return FALSE;
    DWORD pid = 0;
    for (DWORD i = 0; i < length; ++i)
    {
        if (owner[i] < L'0' || owner[i] > L'9'
            || pid > (MAXDWORD - (DWORD)(owner[i] - L'0')) / 10u) return FALSE;
        pid = pid * 10u + (DWORD)(owner[i] - L'0');
    }
    if (pid == 0) return FALSE;
    g_boot_owner = OpenProcess(SYNCHRONIZE, FALSE, pid);
    wsprintfW(name, L"Local\\HKTAS.Boot.%s.Ready", token);
    g_boot_ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, name);
    wsprintfW(name, L"Local\\HKTAS.Boot.%s.Continue", token);
    g_boot_continue = OpenEventW(SYNCHRONIZE, FALSE, name);
    if (!g_boot_owner || !g_boot_ready || !g_boot_continue)
    {
        close_boot_gate();
        return FALSE;
    }
    return TRUE;
}

static void wait_boot_gate_release(void)
{
    if (g_boot_continue)
    {
        HANDLE handles[2] = { g_boot_continue, g_boot_owner };
        /* Owner death releases the wait, so a closed Studio cannot strand
         * the game's primary thread before it has a usable window. */
        WaitForMultipleObjects(2, handles, FALSE, INFINITE);
    }
}

static void enter_boot_gate(void)
{
    if (!g_boot_ready || InterlockedCompareExchange(&g_boot_entered, 1, 0) != 0) return;
    SetEvent(g_boot_ready);
    wait_boot_gate_release();
}

#endif
