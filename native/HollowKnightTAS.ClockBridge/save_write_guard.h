#ifndef HKTAS_SAVE_WRITE_GUARD_H
#define HKTAS_SAVE_WRITE_GUARD_H

#include <winternl.h>
#include <stddef.h>
#include <stdint.h>
#include <wchar.h>
#include <string.h>

#ifndef STATUS_ACCESS_DENIED
#define STATUS_ACCESS_DENIED ((NTSTATUS)0xC0000022L)
#endif

#define HKTAS_GUARD_PATH_CHARS 2048u
#define HKTAS_GUARD_WRITE_ACCESS (FILE_WRITE_DATA | FILE_APPEND_DATA | FILE_WRITE_EA | FILE_WRITE_ATTRIBUTES | DELETE | WRITE_DAC | WRITE_OWNER | GENERIC_WRITE | GENERIC_ALL)

typedef NTSTATUS (NTAPI *hktas_nt_create_file_fn)(PHANDLE, ACCESS_MASK,
    POBJECT_ATTRIBUTES, PIO_STATUS_BLOCK, PLARGE_INTEGER, ULONG, ULONG,
    ULONG, ULONG, PVOID, ULONG);
typedef NTSTATUS (NTAPI *hktas_nt_open_file_fn)(PHANDLE, ACCESS_MASK,
    POBJECT_ATTRIBUTES, PIO_STATUS_BLOCK, ULONG, ULONG);
typedef NTSTATUS (NTAPI *hktas_nt_set_information_file_fn)(HANDLE,
    PIO_STATUS_BLOCK, PVOID, ULONG, FILE_INFORMATION_CLASS);
typedef NTSTATUS (NTAPI *hktas_nt_write_file_fn)(HANDLE, HANDLE,
    PIO_APC_ROUTINE, PVOID, PIO_STATUS_BLOCK, PVOID, ULONG, PLARGE_INTEGER,
    PULONG);

static hktas_nt_create_file_fn g_guard_real_create;
static hktas_nt_open_file_fn g_guard_real_open;
static hktas_nt_set_information_file_fn g_guard_real_set_information;
static hktas_nt_write_file_fn g_guard_real_write;
static wchar_t g_guard_original_root[HKTAS_GUARD_PATH_CHARS];
static wchar_t g_guard_original_device[HKTAS_GUARD_PATH_CHARS];
static wchar_t g_guard_session_token[65];
static size_t g_guard_original_root_length;
static volatile LONG g_guard_armed;
static volatile LONG g_guard_install_status;
static volatile LONG g_guard_rejected_count;

static BOOL guard_is_slot_name(const wchar_t *name)
{
    size_t length;
    if (name == NULL) return FALSE;
    length = wcslen(name);
    return length >= 7u
        && (name[0] == L'u' || name[0] == L'U')
        && (name[1] == L's' || name[1] == L'S')
        && (name[2] == L'e' || name[2] == L'E')
        && (name[3] == L'r' || name[3] == L'R')
        && name[4] >= L'1' && name[4] <= L'4' && name[5] == L'.';
}

static BOOL guard_is_slot_basename(const wchar_t *path)
{
    const wchar_t *last = path;
    if (path == NULL) return FALSE;
    for (const wchar_t *cursor = path; *cursor != L'\0'; ++cursor)
        if (*cursor == L'\\' || *cursor == L'/') last = cursor + 1;
    return guard_is_slot_name(last);
}

static BOOL guard_is_original_slot(const wchar_t *path)
{
    size_t length = g_guard_original_root_length;
    if (length == 0u || path == NULL || wcslen(path) <= length + 1u) return FALSE;
    if (CompareStringOrdinal(path, (int)length, g_guard_original_root,
            (int)length, TRUE) != CSTR_EQUAL || path[length] != L'\\')
        return FALSE;
    const wchar_t *name = path + length + 1u;
    if (wcschr(name, L'\\') != NULL || wcschr(name, L'/') != NULL) return FALSE;
    return guard_is_slot_name(name);
}

static BOOL guard_normalize_path(const wchar_t *input, wchar_t *output,
    DWORD capacity)
{
    wchar_t translated[HKTAS_GUARD_PATH_CHARS];
    const wchar_t *source = input;
    DWORD length;
    if (input == NULL || output == NULL || capacity == 0u) return FALSE;
    if (wcsncmp(source, L"\\??\\", 4) == 0
        || wcsncmp(source, L"\\\\?\\", 4) == 0)
        source += 4;
    else if (g_guard_original_device[0] != L'\0')
    {
        size_t device_length = wcslen(g_guard_original_device);
        if (wcslen(source) > device_length
            && CompareStringOrdinal(source, (int)device_length,
                g_guard_original_device, (int)device_length, TRUE) == CSTR_EQUAL
            && source[device_length] == L'\\')
        {
            int written = swprintf(translated, HKTAS_GUARD_PATH_CHARS,
                L"%lc:%ls", g_guard_original_root[0], source + device_length);
            if (written <= 0 || (unsigned int)written >= HKTAS_GUARD_PATH_CHARS) return FALSE;
            source = translated;
        }
    }
    if (!((source[0] >= L'A' && source[0] <= L'Z')
          || (source[0] >= L'a' && source[0] <= L'z')) || source[1] != L':')
        return FALSE;
    length = GetFullPathNameW(source, capacity, output, NULL);
    if (length == 0u || length >= capacity) return FALSE;
    while (length > 3u && (output[length - 1u] == L'\\'
                           || output[length - 1u] == L'/')) output[--length] = L'\0';
    return TRUE;
}

static BOOL guard_handle_path(HANDLE handle, wchar_t *output, DWORD capacity)
{
    wchar_t raw[HKTAS_GUARD_PATH_CHARS];
    DWORD length = GetFinalPathNameByHandleW(handle, raw,
        HKTAS_GUARD_PATH_CHARS, FILE_NAME_NORMALIZED);
    if (length == 0u || length >= HKTAS_GUARD_PATH_CHARS) return FALSE;
    return guard_normalize_path(raw, output, capacity);
}

static BOOL guard_object_path(POBJECT_ATTRIBUTES attributes,
    wchar_t *output, DWORD capacity, BOOL *slot_basename)
{
    wchar_t raw[HKTAS_GUARD_PATH_CHARS];
    wchar_t root[HKTAS_GUARD_PATH_CHARS];
    UNICODE_STRING *name;
    size_t count;
    if (slot_basename != NULL) *slot_basename = FALSE;
    if (attributes == NULL || attributes->ObjectName == NULL) return FALSE;
    name = attributes->ObjectName;
    if (name->Buffer == NULL || name->Length == 0u
        || (name->Length % sizeof(wchar_t)) != 0u) return FALSE;
    count = name->Length / sizeof(wchar_t);
    if (count >= HKTAS_GUARD_PATH_CHARS) return FALSE;
    memcpy(raw, name->Buffer, name->Length);
    raw[count] = L'\0';
    if (slot_basename != NULL) *slot_basename = guard_is_slot_basename(raw);
    if (attributes->RootDirectory != NULL && raw[0] != L'\\'
        && !(count > 1u && raw[1] == L':'))
    {
        if (!guard_handle_path(attributes->RootDirectory, root,
                HKTAS_GUARD_PATH_CHARS)) return FALSE;
        size_t root_length = wcslen(root);
        if (root_length + 1u + count >= HKTAS_GUARD_PATH_CHARS) return FALSE;
        memmove(raw + root_length + 1u, raw, (count + 1u) * sizeof(wchar_t));
        memcpy(raw, root, root_length * sizeof(wchar_t));
        raw[root_length] = L'\\';
    }
    return guard_normalize_path(raw, output, capacity);
}

static NTSTATUS guard_deny(PIO_STATUS_BLOCK io, PHANDLE output_handle)
{
    InterlockedIncrement(&g_guard_rejected_count);
    if (output_handle != NULL) *output_handle = NULL;
    if (io != NULL) { io->Status = STATUS_ACCESS_DENIED; io->Information = 0u; }
    return STATUS_ACCESS_DENIED;
}

static NTSTATUS NTAPI guard_nt_create_file(PHANDLE handle, ACCESS_MASK access,
    POBJECT_ATTRIBUTES attributes, PIO_STATUS_BLOCK io, PLARGE_INTEGER allocation,
    ULONG attributes_bits, ULONG share, ULONG disposition, ULONG options,
    PVOID ea, ULONG ea_length)
{
    wchar_t path[HKTAS_GUARD_PATH_CHARS];
    BOOL slot_basename = FALSE;
    BOOL resolved = guard_object_path(attributes, path,
        HKTAS_GUARD_PATH_CHARS, &slot_basename);
    BOOL can_mutate = (access & HKTAS_GUARD_WRITE_ACCESS) != 0u
        || disposition != FILE_OPEN || (options & FILE_DELETE_ON_CLOSE) != 0u;
    if (can_mutate && ((resolved && guard_is_original_slot(path))
                       || (!resolved && slot_basename)))
        return guard_deny(io, handle);
    return g_guard_real_create(handle, access, attributes, io, allocation,
        attributes_bits, share, disposition, options, ea, ea_length);
}

static NTSTATUS NTAPI guard_nt_open_file(PHANDLE handle, ACCESS_MASK access,
    POBJECT_ATTRIBUTES attributes, PIO_STATUS_BLOCK io, ULONG share, ULONG options)
{
    wchar_t path[HKTAS_GUARD_PATH_CHARS];
    BOOL slot_basename = FALSE;
    BOOL resolved = guard_object_path(attributes, path,
        HKTAS_GUARD_PATH_CHARS, &slot_basename);
    if (((access & HKTAS_GUARD_WRITE_ACCESS) != 0u
            || (options & FILE_DELETE_ON_CLOSE) != 0u)
        && ((resolved && guard_is_original_slot(path))
            || (!resolved && slot_basename)))
        return guard_deny(io, handle);
    return g_guard_real_open(handle, access, attributes, io, share, options);
}

static BOOL guard_rename_target(PVOID information, ULONG length)
{
    FILE_RENAME_INFORMATION *rename = (FILE_RENAME_INFORMATION *)information;
    wchar_t raw[HKTAS_GUARD_PATH_CHARS];
    wchar_t root[HKTAS_GUARD_PATH_CHARS];
    wchar_t normalized[HKTAS_GUARD_PATH_CHARS];
    size_t count;
    if (information == NULL || length < offsetof(FILE_RENAME_INFORMATION, FileName))
        return FALSE;
    if (rename->FileNameLength > length - offsetof(FILE_RENAME_INFORMATION, FileName)
        || (rename->FileNameLength % sizeof(wchar_t)) != 0u) return FALSE;
    count = rename->FileNameLength / sizeof(wchar_t);
    if (count == 0u || count >= HKTAS_GUARD_PATH_CHARS) return FALSE;
    memcpy(raw, rename->FileName, rename->FileNameLength);
    raw[count] = L'\0';
    if (rename->RootDirectory != NULL && raw[0] != L'\\'
        && !(count > 1u && raw[1] == L':'))
    {
        if (!guard_handle_path(rename->RootDirectory, root,
                HKTAS_GUARD_PATH_CHARS)) return guard_is_slot_basename(raw);
        size_t root_length = wcslen(root);
        if (root_length + 1u + count >= HKTAS_GUARD_PATH_CHARS) return TRUE;
        memmove(raw + root_length + 1u, raw, (count + 1u) * sizeof(wchar_t));
        memcpy(raw, root, root_length * sizeof(wchar_t));
        raw[root_length] = L'\\';
    }
    if (!guard_normalize_path(raw, normalized, HKTAS_GUARD_PATH_CHARS))
        return guard_is_slot_basename(raw);
    return guard_is_original_slot(normalized);
}

static NTSTATUS NTAPI guard_nt_set_information_file(HANDLE handle,
    PIO_STATUS_BLOCK io, PVOID information, ULONG length,
    FILE_INFORMATION_CLASS info_class)
{
    wchar_t path[HKTAS_GUARD_PATH_CHARS];
    BOOL resolved = guard_handle_path(handle, path, HKTAS_GUARD_PATH_CHARS);
    if ((resolved && guard_is_original_slot(path))
        || (!resolved && GetFileType(handle) == FILE_TYPE_DISK)
        || ((info_class == FileRenameInformation || info_class == FileLinkInformation
             || (int)info_class == 65 || (int)info_class == 72)
            && guard_rename_target(information, length)))
        return guard_deny(io, NULL);
    return g_guard_real_set_information(handle, io, information, length, info_class);
}

static NTSTATUS NTAPI guard_nt_write_file(HANDLE handle, HANDLE event,
    PIO_APC_ROUTINE apc, PVOID apc_context, PIO_STATUS_BLOCK io, PVOID buffer,
    ULONG length, PLARGE_INTEGER offset, PULONG key)
{
    wchar_t path[HKTAS_GUARD_PATH_CHARS];
    BOOL resolved = guard_handle_path(handle, path, HKTAS_GUARD_PATH_CHARS);
    if ((resolved && guard_is_original_slot(path))
        || (!resolved && GetFileType(handle) == FILE_TYPE_DISK))
        return guard_deny(io, NULL);
    return g_guard_real_write(handle, event, apc, apc_context, io,
        buffer, length, offset, key);
}

static BOOL guard_install_nt_hook(HMODULE ntdll, const char *name,
    void *replacement, void **original)
{
    BYTE *target = (BYTE *)GetProcAddress(ntdll, name);
    SYSTEM_INFO system;
    BYTE *relay = NULL;
    DWORD old_protection, ignored;
    BYTE patch[8] = {0xe9, 0, 0, 0, 0, 0x90, 0x90, 0x90};
    if (target == NULL || ((uintptr_t)target & 7u) != 0u
        || target[0] != 0x4c || target[1] != 0x8b || target[2] != 0xd1
        || target[3] != 0xb8) return FALSE;
    GetSystemInfo(&system);
    uintptr_t aligned = (uintptr_t)target
        & ~((uintptr_t)system.dwAllocationGranularity - 1u);
    for (uintptr_t delta = system.dwAllocationGranularity;
         delta < 0x70000000u; delta += system.dwAllocationGranularity)
    {
        relay = VirtualAlloc((void *)(aligned + delta), 4096u,
            MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (relay != NULL) break;
        if (aligned > delta)
            relay = VirtualAlloc((void *)(aligned - delta), 4096u,
                MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (relay != NULL) break;
    }
    if (relay == NULL) return FALSE;
    int64_t distance = (int64_t)((intptr_t)relay - (intptr_t)(target + 5));
    if (distance < INT32_MIN || distance > INT32_MAX) return FALSE;
    relay[0] = 0xff; relay[1] = 0x25;
    memset(relay + 2, 0, 4);
    memcpy(relay + 6, &replacement, sizeof(replacement));
    BYTE *direct = relay + 32;
    memcpy(direct, target, 8);
    direct[8] = 0x0f; direct[9] = 0x05; direct[10] = 0xc3;
    if (!VirtualProtect(relay, 4096u, PAGE_EXECUTE_READ, &old_protection)) return FALSE;
    *original = direct;
    int32_t relative = (int32_t)distance;
    memcpy(patch + 1, &relative, sizeof(relative));
    uint64_t atomic_patch;
    memcpy(&atomic_patch, patch, sizeof(atomic_patch));
    if (!VirtualProtect(target, 8u, PAGE_EXECUTE_READWRITE, &old_protection)) return FALSE;
    InterlockedExchange64((volatile LONG64 *)target, (LONG64)atomic_patch);
    VirtualProtect(target, 8u, old_protection, &ignored);
    FlushInstructionCache(GetCurrentProcess(), target, 8u);
    return TRUE;
}

static BOOL install_save_write_guard(const wchar_t *original_root,
    const wchar_t *session_token)
{
    HMODULE ntdll;
    wchar_t drive[3];
    wchar_t normalized_root[HKTAS_GUARD_PATH_CHARS];
    if (session_token == NULL || wcslen(session_token) != 64u) return FALSE;
    for (const wchar_t *cursor = session_token; *cursor != L'\0'; ++cursor)
        if (!((*cursor >= L'0' && *cursor <= L'9')
              || (*cursor >= L'a' && *cursor <= L'f'))) return FALSE;
    if (!guard_normalize_path(original_root, normalized_root,
            HKTAS_GUARD_PATH_CHARS)) return FALSE;
    if (InterlockedCompareExchange(&g_guard_armed, 0, 0) != 0)
        return wcscmp(session_token, g_guard_session_token) == 0
            && CompareStringOrdinal(normalized_root, -1,
                g_guard_original_root, -1, TRUE) == CSTR_EQUAL;
    wcscpy(g_guard_original_root, normalized_root);
    wcscpy(g_guard_session_token, session_token);
    g_guard_original_root_length = wcslen(g_guard_original_root);
    if (g_guard_original_root_length < 4u || g_guard_original_root[1] != L':') return FALSE;
    drive[0] = g_guard_original_root[0]; drive[1] = L':'; drive[2] = L'\0';
    if (QueryDosDeviceW(drive, g_guard_original_device,
            HKTAS_GUARD_PATH_CHARS) == 0u) return FALSE;
    ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll == NULL) return FALSE;
    if (!guard_install_nt_hook(ntdll, "NtCreateFile", guard_nt_create_file,
            (void **)&g_guard_real_create)
        || !guard_install_nt_hook(ntdll, "NtOpenFile", guard_nt_open_file,
            (void **)&g_guard_real_open)
        || !guard_install_nt_hook(ntdll, "NtSetInformationFile", guard_nt_set_information_file,
            (void **)&g_guard_real_set_information)
        || !guard_install_nt_hook(ntdll, "NtWriteFile", guard_nt_write_file,
            (void **)&g_guard_real_write))
        return FALSE;
    InterlockedExchange(&g_guard_armed, 1);
    return TRUE;
}

static BOOL save_write_guard_is_armed(void)
{
    return InterlockedCompareExchange(&g_guard_armed, 0, 0) == 1;
}

static BOOL save_write_guard_matches(const wchar_t *original_root,
    const wchar_t *session_token)
{
    wchar_t normalized_root[HKTAS_GUARD_PATH_CHARS];
    if (!save_write_guard_is_armed() || session_token == NULL
        || wcscmp(session_token, g_guard_session_token) != 0
        || !guard_normalize_path(original_root, normalized_root,
            HKTAS_GUARD_PATH_CHARS)) return FALSE;
    return CompareStringOrdinal(normalized_root, -1,
        g_guard_original_root, -1, TRUE) == CSTR_EQUAL;
}

#endif
