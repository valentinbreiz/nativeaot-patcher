## Available Unit Tests
- **Cosmos.Tests.Build.Asm**:
  Verifies the assembly build task runs via Yasm.
    - Test1
- **Cosmos.Tests.Build.Analyzer.Patcher**:
  Validates that code does not contain plug architecture errors.
    - Test_TypeNotFoundDiagnostic
    - Test_AnalyzeAccessedMember
    - Test_MethodNotImplemented
    - Test_StaticConstructorTooManyParameters
    - Test_StaticConstructorNotImplemented
- **Cosmos.Tests.Scanner**:
  Validates that all required plugs are detected correctly.
    - LoadPlugMethods_ShouldReturnPublicStaticMethods
    - LoadPlugMethods_ShouldReturnEmpty_WhenNoMethodsExist
    - LoadPlugMethods_ShouldContainAddMethod_WhenPlugged
    - LoadPlugs_ShouldFindPluggedClasses
    - LoadPlugs_ShouldIgnoreClassesWithoutPlugAttribute
    - LoadPlugs_ShouldHandleOptionalPlugs
    - FindPluggedAssemblies_ShouldReturnMatchingAssemblies
- **Cosmos.Tests.Patcher**:
  Ensures that plugs are applied successfully to target methods and types.
    - PatchAssembly_ShouldSkipWhenNoMatchingPlugs
    - PatchObjectWithAThis_ShouldPlugInstanceCorrectly
    - PatchConstructor_ShouldPlugCtorCorrectly
    - PatchProperty_ShouldPlugProperty
    - PatchType_ShouldReplaceAllMethodsCorrectly
    - PatchType_ShouldPlugAssembly
    - AddMethod_BehaviorBeforeAndAfterPlug
- **Cosmos.Tests.NativeWrapper**:
  Contains runtime assets but no unit tests.
- **Cosmos.Tests.NativeLibrary**:
  Provides native code used in tests; no unit tests.

## Running Tests
You can run the test suite from the repository root:

```
dotnet test
```
