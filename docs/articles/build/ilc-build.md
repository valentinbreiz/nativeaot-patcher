## Overview

**Cosmos.ilc.Build** is the build system for CosmosOS-projects. It is responsible for compiling the patched dll outputted by **Cosmos.Patcher** into native code which is then linked with native libraries on the target os.

---

## Workflow

### Step 0: Run Patcher
The **Cosmos.ilc.Build**:
- Calls **Cosmos.Patcher** to patch the project assembly.

### Step 1: Compile with ILC
The **Cosmos.ilc.Build**:
- Compiles the patched dll to a `.obj` file.

### Step 2: Link with native libraries
The **Cosmos.ilc.Build**:
- Links the generated `.obj` file with native libraries
- Outputs a native binary file ready for further processing
---
