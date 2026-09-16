#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <assert.h>
#include "../../native/HollowKnightTAS.ClockBridge/startup_gate.h"

static volatile LONG completed;
static DWORD WINAPI exercise_gate(LPVOID unused)
{
    (void)unused;
    enter_boot_gate();
    InterlockedExchange(&completed, 1);
    return 0;
}

int main(void)
{
    wchar_t pid[24];
    const wchar_t *token = L"0123456789abcdef0123456789abcdef";
    SetEnvironmentVariableW(L"HKTAS_BOOT_GATE_TOKEN", NULL);
    assert(configure_boot_gate());
    enter_boot_gate();
    assert(g_boot_entered == 0);
    SetEnvironmentVariableW(L"HKTAS_BOOT_GATE_TOKEN", L"invalid");
    assert(!configure_boot_gate());
    SetEnvironmentVariableW(L"HKTAS_BOOT_GATE_TOKEN", token);
    wsprintfW(pid, L"%lu", GetCurrentProcessId());
    SetEnvironmentVariableW(L"HKTAS_BOOT_GATE_OWNER", pid);
    HANDLE ready = CreateEventW(NULL, TRUE, FALSE,
        L"Local\\HKTAS.Boot.0123456789abcdef0123456789abcdef.Ready");
    HANDLE proceed = CreateEventW(NULL, TRUE, FALSE,
        L"Local\\HKTAS.Boot.0123456789abcdef0123456789abcdef.Continue");
    assert(ready && proceed && configure_boot_gate());
    HANDLE worker = CreateThread(NULL, 0, exercise_gate, NULL, 0, NULL);
    assert(worker && WaitForSingleObject(ready, 3000) == WAIT_OBJECT_0);
    assert(completed == 0);
    assert(WaitForSingleObject(worker, 50) == WAIT_TIMEOUT);
    assert(SetEvent(proceed));
    assert(WaitForSingleObject(worker, 3000) == WAIT_OBJECT_0 && completed == 1);
    enter_boot_gate(); /* One shot: subsequent reads cannot re-arm the gate. */
    close_boot_gate();
    CloseHandle(worker); CloseHandle(ready); CloseHandle(proceed);
    return 0;
}
