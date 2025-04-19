#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT
#endif

EXPORT int Add(int a, int b)
{
    return a + b;
}
