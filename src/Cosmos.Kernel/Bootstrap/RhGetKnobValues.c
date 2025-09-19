typedef unsigned int uint32_t;
typedef int int32_t;
typedef unsigned long long uint64_t;

// Security cookie for buffer overflow protection
uint64_t __security_cookie = 0x2B992DDFA23249D6ULL;

typedef struct {
    uint32_t m_count;
    char *m_first[];  // Flexible array member: keys followed by values
} Config;

extern Config g_compilerEmbeddedKnobsBlob;

extern uint32_t RhGetKnobValues(char *** pResultKeys, char *** pResultValues)
{
    *pResultKeys = g_compilerEmbeddedKnobsBlob.m_first;
    *pResultValues = &g_compilerEmbeddedKnobsBlob.m_first[g_compilerEmbeddedKnobsBlob.m_count];
    return g_compilerEmbeddedKnobsBlob.m_count;
}