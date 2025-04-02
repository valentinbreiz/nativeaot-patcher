using System.Runtime;

unsafe class Program
{
    [RuntimeExport("kmain")]
    static void Main()
    { 
        while (true) ; 
    }
}