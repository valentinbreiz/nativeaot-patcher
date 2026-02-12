// This code is licensed under MIT license (see LICENSE for details)

namespace Cosmos.Kernel.Core.Async;

public delegate void KernelAsyncCallback<TException>(TException? exception) where TException : Exception;

public delegate void KernelAsyncCallback<TException, TValue>(TException? exception, TValue value)
    where TException : Exception
    where TValue : allows ref struct;
