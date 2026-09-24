#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <wchar.h>
#include "../../native/HollowKnightTAS.ClockBridge/save_write_guard.h"

static int failed;

static void check(BOOL condition, const char *label)
{
    if (!condition) { printf("FAIL %s\n", label); failed++; }
    else printf("PASS %s\n", label);
}

static void path_join(wchar_t *output, size_t capacity,
    const wchar_t *root, const wchar_t *name)
{
    swprintf(output, capacity, L"%ls\\%ls", root, name);
}

int main(void)
{
    wchar_t temporary[MAX_PATH], root[MAX_PATH], original[MAX_PATH], shadow[MAX_PATH];
    wchar_t existing[MAX_PATH], new_file[MAX_PATH], side[MAX_PATH], backup[MAX_PATH];
    wchar_t outside[MAX_PATH], shadow_file[MAX_PATH];
    DWORD length = GetTempPathW(MAX_PATH, temporary);
    if (length == 0u || length >= MAX_PATH) return 2;
    swprintf(root, MAX_PATH, L"%lsHKTAS-Guard-Test-%lu", temporary, GetCurrentProcessId());
    path_join(original, MAX_PATH, root, L"original");
    path_join(shadow, MAX_PATH, root, L"shadow");
    CreateDirectoryW(root, NULL);
    CreateDirectoryW(original, NULL);
    CreateDirectoryW(shadow, NULL);
    path_join(existing, MAX_PATH, original, L"user1.dat");
    path_join(new_file, MAX_PATH, original, L"user1.new");
    path_join(side, MAX_PATH, original, L"user4.modded.json");
    path_join(backup, MAX_PATH, original, L"user1.dat.bak");
    path_join(outside, MAX_PATH, root, L"outside.tmp");
    path_join(shadow_file, MAX_PATH, shadow, L"user1.dat");

    HANDLE existing_handle = CreateFileW(existing, GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
        CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    HANDLE outside_handle = CreateFileW(outside, GENERIC_WRITE,
        FILE_SHARE_READ, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (existing_handle == INVALID_HANDLE_VALUE || outside_handle == INVALID_HANDLE_VALUE) return 3;
    DWORD written;
    WriteFile(existing_handle, "original", 8, &written, NULL);
    CloseHandle(outside_handle);

    const wchar_t token[] = L"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    if (!install_save_write_guard(original, token))
    {
        printf("INSTALL_FAIL %lu\n", GetLastError());
        return 4;
    }
    check(save_write_guard_is_armed(), "armed");
    check(install_save_write_guard(original, token), "same_identity_rearm_allowed");
    check(!install_save_write_guard(shadow, token), "different_root_rearm_denied");
    check(!install_save_write_guard(original,
        L"1123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
        "different_token_rearm_denied");
    check(save_write_guard_matches(original, token), "identity_matches");
    check(!save_write_guard_matches(shadow, token), "identity_rejects_shadow_root");

    HANDLE read_handle = CreateFileW(existing, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    check(read_handle != INVALID_HANDLE_VALUE, "original_read_allowed");
    if (read_handle != INVALID_HANDLE_VALUE) CloseHandle(read_handle);
    HANDLE write_handle = CreateFileW(existing, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    check(write_handle == INVALID_HANDLE_VALUE, "original_open_write_denied");
    if (write_handle != INVALID_HANDLE_VALUE) CloseHandle(write_handle);
    check(!WriteFile(existing_handle, "bad", 3, &written, NULL), "preopened_handle_write_denied");
    check(!SetEndOfFile(existing_handle), "preopened_handle_truncate_denied");
    CloseHandle(existing_handle);

    HANDLE create_handle = CreateFileW(new_file, GENERIC_WRITE, FILE_SHARE_READ,
        NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    check(create_handle == INVALID_HANDLE_VALUE, "new_file_create_denied");
    if (create_handle != INVALID_HANDLE_VALUE) CloseHandle(create_handle);
    create_handle = CreateFileW(side, GENERIC_WRITE, FILE_SHARE_READ,
        NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    check(create_handle == INVALID_HANDLE_VALUE, "mod_side_create_denied");
    if (create_handle != INVALID_HANDLE_VALUE) CloseHandle(create_handle);
    create_handle = CreateFileW(backup, GENERIC_WRITE, FILE_SHARE_READ,
        NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    check(create_handle == INVALID_HANDLE_VALUE, "backup_create_denied");
    if (create_handle != INVALID_HANDLE_VALUE) CloseHandle(create_handle);
    wchar_t previous_directory[MAX_PATH];
    GetCurrentDirectoryW(MAX_PATH, previous_directory);
    SetCurrentDirectoryW(original);
    create_handle = CreateFileW(L"user2.dat", GENERIC_WRITE, FILE_SHARE_READ,
        NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    check(create_handle == INVALID_HANDLE_VALUE, "relative_slot_create_denied");
    if (create_handle != INVALID_HANDLE_VALUE) CloseHandle(create_handle);
    SetCurrentDirectoryW(previous_directory);
    check(!DeleteFileW(existing), "original_delete_denied");
    check(!MoveFileW(outside, new_file), "rename_into_original_denied");
    check(!MoveFileW(existing, outside), "rename_out_of_original_denied");

    HANDLE shadow_handle = CreateFileW(shadow_file, GENERIC_WRITE, FILE_SHARE_READ,
        NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    check(shadow_handle != INVALID_HANDLE_VALUE, "shadow_create_allowed");
    if (shadow_handle != INVALID_HANDLE_VALUE)
    {
        check(WriteFile(shadow_handle, "shadow", 6, &written, NULL), "shadow_write_allowed");
        CloseHandle(shadow_handle);
    }
    check(g_guard_rejected_count >= 7, "rejection_count");
    wprintf(L"TEST_ROOT %ls\n", root);
    return failed == 0 ? 0 : 5;
}
