#ifndef HKTAS_FRACTIONAL_FRAME_CLOCK_H
#define HKTAS_FRACTIONAL_FRAME_CLOCK_H
#include <stdint.h>
#include <limits.h>

typedef struct hktas_fractional_clock {
    int64_t whole;
    uint64_t fraction;
    uint64_t phase;
} hktas_fractional_clock;

/* A common 64-bit fractional-tick phase survives rate changes and pauses.
 * Conversion truncates by less than 2^-64 QPC ticks per frame. Two 32-bit
 * divisions avoid compiler-specific 128-bit arithmetic. Integers deliberately
 * retain the legacy nearest-tick step for existing movies. */
static int hktas_fractional_configure(hktas_fractional_clock *clock,
    int64_t frequency, int32_t numerator, int32_t denominator)
{
    if (frequency <= 0 || numerator <= 0 || numerator > 1000000000
        || denominator <= 0 || denominator > 1000000
        || numerator < denominator || (int64_t)numerator > 1000LL * denominator
        || frequency > (INT64_MAX - numerator) / denominator) return 0;
    int64_t scaled = frequency * denominator;
    int64_t whole = scaled / numerator;
    uint64_t fraction = 0;
    if (numerator % denominator == 0) {
        int32_t fps = numerator / denominator;
        whole = (frequency + fps / 2) / fps;
    } else {
        uint64_t remainder = (uint64_t)(scaled % numerator);
        uint64_t upper = (remainder << 32) / (uint32_t)numerator;
        remainder = (remainder << 32) % (uint32_t)numerator;
        fraction = (upper << 32) | ((remainder << 32) / (uint32_t)numerator);
    }
    if (whole <= 0) return 0;
    clock->whole = whole;
    clock->fraction = fraction;
    return 1;
}

static int64_t hktas_fractional_peek(const hktas_fractional_clock *clock)
{
    return clock->whole + (clock->phase + clock->fraction < clock->phase ? 1 : 0);
}

static void hktas_fractional_consume(hktas_fractional_clock *clock)
{
    clock->phase += clock->fraction;
}
#endif
