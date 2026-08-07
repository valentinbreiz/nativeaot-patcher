// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Async;


public delegate void KernelAsyncCallback(Exception? exception);

public delegate void KernelAsyncCallback<in TValue>(Exception? exception, TValue value)
    where TValue : allows ref struct;

public delegate void KernelAsyncCallback<in TException, in TValue>(TException? exception, TValue value)
    where TException : Exception
    where TValue : allows ref struct;
