using System.Runtime;

unsafe class Program
{
    [RuntimeExport("kmain")]
    static void Main() { 
        // Hello and welcome to kernel-land inside C#!

        // Okay well, we *should* set IDT
        
        while (true) ; 
    }
}