# Funclet

.NET compilers do not compile a `catch`, `filter` or `finally` block inline in its method. Each handler becomes a funclet: a small function of its own, called by the exception dispatcher when the handler runs, with its own stack frame but direct access to the parent method's locals through the parent frame.

Stack scanning has to know this. While a handler runs, the stack holds funclet frames whose live references are described by the parent method's metadata, not by anything of their own, and the parent frame deeper on the stack is still live at the same time. A scanner that treated a funclet like an ordinary method would find no GCInfo for it and lose track of live references.

In OrionGC a funclet's metadata redirects to the parent method's GCInfo record, and the funclet is scanned with the parent's slot table applied to the frame the funclet actually runs on. See [How the precise scan walks a thread](../garbage-collector-gcinfo.md#how-the-precise-scan-walks-a-thread).
