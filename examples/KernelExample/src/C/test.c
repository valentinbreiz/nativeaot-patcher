typedef unsigned short char16_t;

extern char16_t* testGCC()
{
    static char16_t hello[] = u"Hello from GCC";
    return hello;
}