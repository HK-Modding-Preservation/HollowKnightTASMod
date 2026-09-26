#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include "../../native/HollowKnightTAS.ClockBridge/restore_window.h"

static int failed;
#define CHECK(condition) do { if (!(condition)) { fprintf(stderr, "FAIL: %s\n", #condition); ++failed; } } while (0)

int main(void)
{
    WNDCLASSW cls = {0};
    cls.lpfnWndProc = DefWindowProcW;
    cls.hInstance = GetModuleHandleW(NULL);
    cls.lpszClassName = L"UnityWndClass";
    CHECK(RegisterClassW(&cls) != 0);
    SetEnvironmentVariableW(L"HKTAS_RESTORE_HIDDEN_WINDOW", NULL);
    CHECK(install_restore_window_hooks());
    SetEnvironmentVariableW(L"HKTAS_RESTORE_HIDDEN_WINDOW", L"1");
    CHECK(!install_restore_window_hooks()); /* Missing Unity module fails closed. */
    HWND window = restore_create_window(0, cls.lpszClassName, L"Restore harness",
        WS_OVERLAPPEDWINDOW | WS_VISIBLE, 0, 0, 100, 100, NULL, NULL, cls.hInstance, NULL);
    CHECK(window != NULL && !IsWindowVisible(window));
    CHECK(restore_window_held(window));
    restore_show_window(window, SW_SHOW);
    CHECK(!IsWindowVisible(window));
    restore_position_window(window, HWND_TOP, 0, 0, 100, 100, SWP_SHOWWINDOW);
    CHECK(!IsWindowVisible(window));
    CHECK(!restore_foreground_window(window));
    CHECK((restore_style(window, GWL_STYLE, WS_VISIBLE) & WS_VISIBLE) == 0);
    SetWindowLongPtrW(window, GWL_EXSTYLE, WS_EX_NOACTIVATE);
    CHECK((restore_style(window, GWL_EXSTYLE, 0) & WS_EX_NOACTIVATE) != 0);
    SetPropW(window, HKTAS_RESTORE_VISIBLE_PROPERTY, (HANDLE)1);
    CHECK(!restore_window_held(window));
    CHECK(restore_style(window, GWL_STYLE, WS_VISIBLE) == WS_VISIBLE);
    CHECK(restore_style(window, GWL_EXSTYLE, 0) == 0);
    restore_window_long_w(window, GWLP_USERDATA, 123);
    CHECK(GetWindowLongPtrW(window, GWLP_USERDATA) == 123);
    DestroyWindow(window);
    UnregisterClassW(cls.lpszClassName, cls.hInstance);
    printf("Restore window: 14 checks, %d failures\n", failed);
    return failed ? 1 : 0;
}
