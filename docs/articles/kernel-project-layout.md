# Kernel Project Layout

The Cosmos kernel is composed of layered projects to enforce a clean dependency graph.


```mermaid
flowchart LR;
	UsersKernel-->Cosmos.Kernel;
	UsersKernel-->Cosmos.Kernel.System;
	UsersKernel-->Cosmos.Kernel.HAL;
	UsersKernel-->Cosmos.Kernel.Core;
    Cosmos.Kernel-->Cosmos.Kernel.Boot.*;
	Cosmos.Kernel-->Cosmos.Kernel.SubSystem;
	Cosmos.Kernel.SubSystem-->Cosmos.Kernel.System;
	Cosmos.Kernel.SubSystem-->Cosmos.Kernel.HAL;
    Cosmos.Kernel-->Cosmos.Kernel.System;
    Cosmos.Kernel.System-->Cosmos.Kernel.System.ARM/x86;
	Cosmos.Kernel.System.Plug-->Cosmos.Kernel.System;
    Cosmos.Kernel.System-->Cosmos.Kernel.HAL;
    Cosmos.Kernel.HAL-->Cosmos.Kernel.HAL.Interfaces;
	Cosmos.Kernel.HAL-->Cosmos.Kernel.HAL.ARM/x86;
	Cosmos.Kernel.HAL.ARM/x86-->Cosmos.Kernel.HAL.Interfaces;
	Cosmos.Kernel.HAL.Plug-->Cosmos.Kernel.HAL;
	Cosmos.Kernel.HAL-->Cosmos.Kernel.Core;
    Cosmos.Kernel.Core-->Cosmos.Kernel.Core.ARM/x86;
	Cosmos.Kernel.Core.Plug-->Cosmos.Kernel.Core;
```

- **UsersKernel**: your custom kernel project.
- **Cosmos.Kernel**: aggregates core runtime, system, graphics, HAL and plugs.
- **Cosmos.Kernel.Boot.\***: bootloader specific implementations such as `Cosmos.Kernel.Boot.Limine`.

Only the `Cosmos.Kernel` project may reference `Cosmos.Kernel.Boot.*` projects. A build check fails if other projects introduce such references.
