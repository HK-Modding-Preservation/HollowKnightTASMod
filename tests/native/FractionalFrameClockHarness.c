/* Run the actual exported rate setter and PlayerLoop clock advance without
 * loading a DLL into the game. DllMain is never invoked by this executable. */
#include "../../native/HollowKnightTAS.ClockBridge/clock_bridge.c"
#include <stdio.h>

#define CHECK(value) do { if (!(value)) { fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #value); return 1; } } while (0)

static void reset_clock(void)
{
    g_deterministic_clock_enabled = TRUE;
    g_deterministic_clock_frequency = 10000000;
    g_deterministic_clock_anchor.QuadPart = 0;
    g_v2_gate_enabled = TRUE;
    memset(&g_fractional_clock, 0, sizeof(g_fractional_clock));
    g_fractional_clock_configured = FALSE;
}

int main(void)
{
    reset_clock();
    /* Reconfiguration happens every real frame: it must never reset phase. */
    for (int i = 1; i <= 1000000; ++i) {
        CHECK(HktasClockBridge_SetFullRunFrameRateRatio(99999, 1000) == 1);
        int64_t before = g_deterministic_clock_anchor.QuadPart;
        int64_t step = HktasClockBridge_GetDeterministicClockStepTicks();
        advance_boot_frame_clock();
        CHECK(g_deterministic_clock_anchor.QuadPart - before == step);
        CHECK(HktasClockBridge_GetDeterministicClockStepTicks() == step);
        __int128 error = (__int128)i * 10000000 * 1000
            - (__int128)g_deterministic_clock_anchor.QuadPart * 99999;
        CHECK(error >= 0 && error <= 99999); /* at most one QPC tick */
    }
    printf("PASS 1000000 frames at 99.999 FPS; cumulative error <= 1 QPC tick\n");
    reset_clock();
    for (int i = 0; i < 100000; ++i) {
        CHECK(HktasClockBridge_SetFullRunFrameRateRatio(99999, 1000) == 1);
        advance_boot_frame_clock();
        CHECK(HktasClockBridge_SetFullRunFrameRateRatio(60001, 1000) == 1);
        advance_boot_frame_clock();
    }
    __int128 expected = (__int128)100000 * 10000000 * 1000 * (99999 + 60001);
    __int128 error = expected - (__int128)g_deterministic_clock_anchor.QuadPart * 99999 * 60001;
    CHECK(error >= 0 && error <= (__int128)99999 * 60001);
    printf("PASS 200000 alternating fractional frames; carry survives rate changes\n");
    int64_t anchor = g_deterministic_clock_anchor.QuadPart;
    uint64_t phase = g_fractional_clock.phase;
    for (int i = 0; i < 1000; ++i) {
        CHECK(HktasClockBridge_SetFullRunFrameRateRatio(99999, 1000) == 1);
        (void)HktasClockBridge_GetDeterministicClockStepTicks();
    }
    CHECK(g_deterministic_clock_anchor.QuadPart == anchor && g_fractional_clock.phase == phase);
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(0, 1) == 0);
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(1, 0) == 0);
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(1000000001, 1000000) == 0);
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(999999, 1000000) == 0);
    CHECK(g_deterministic_clock_anchor.QuadPart == anchor && g_fractional_clock.phase == phase);
    printf("PASS paused queries/configuration and invalid ratios do not advance time\n");
    reset_clock();
    for (int fps = 1; fps <= 1000; ++fps) {
        CHECK(HktasClockBridge_SetFullRunFrameRate(fps) == 1);
        int64_t before = g_deterministic_clock_anchor.QuadPart;
        advance_boot_frame_clock();
        CHECK(g_deterministic_clock_anchor.QuadPart - before == (10000000 + fps / 2) / fps);
    }
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(999999999, 1000000) == 1);
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(1000001, 1000000) == 1);
    printf("PASS all legacy integer rates and six-decimal boundaries\n");
    reset_clock();
    g_loading_startup_clock_enabled = TRUE;
    g_loading_startup_clock_frozen_frame = TRUE;
    CHECK(HktasClockBridge_SetFullRunFrameRateRatio(99999, 1000) == 1);
    advance_boot_frame_clock();
    CHECK(g_deterministic_clock_anchor.QuadPart == 0 && g_fractional_clock.phase == 0);
    g_loading_startup_clock_frozen_frame = FALSE;
    g_status = 2;
    g_query_performance_counter = QueryPerformanceCounter;
    g_virtual_clock_main_thread_id = GetCurrentThreadId();
    for (LONG i = 1; i <= 10000; ++i) {
        advance_boot_frame_clock();
        LONGLONG current = g_deterministic_clock_anchor.QuadPart;
        uint64_t current_phase = g_fractional_clock.phase;
        CHECK(HktasClockBridge_AdvanceDeterministicFrameClock(i) == 1);
        CHECK(g_deterministic_clock_anchor.QuadPart == current && g_fractional_clock.phase == current_phase);
        CHECK(HktasClockBridge_AdvanceDeterministicFrameClock(i) == -5);
    }
    printf("PASS canonical bootstrap holds time and managed observations never double-advance it\n");
    return 0;
}
