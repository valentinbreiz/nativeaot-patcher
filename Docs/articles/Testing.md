## Available Unit Tests
- **Liquip.Patcher.Analyzer.Test**:
  Validates that code does not contain plug architecture errors.
    - Test_TypeNotFoundDiagnostic
    - Test_PlugNotStaticDiagnostic
    - Test_MethodNeedsPlugDiagnostic
    - Test_AnalyzeAccessedMember
    - Test_MethodNotImplemented
- **Liquip.Scanner.Tests**:
  Validates that all required plugs are detected correctly.
    - LoadPlugMethods_ShouldReturnPublicStaticMethods
    - LoadPlugMethods_ShouldReturnEmpty_WhenNoMethodsExist
    - LoadPlugMethods_ShouldContainAddMethod_WhenPlugged
    - LoadPlugs_ShouldFindPluggedClasses
    - LoadPlugs_ShouldIgnoreClassesWithoutPlugAttribute
    - LoadPlugs_ShouldHandleOptionalPlugs
- **Liquip.Patcher.Tests**:
  Ensures that plugs are applied successfully to target methods and types.
    - PatchObjectWithAThis_ShouldPlugInstanceCorrectly
    - PatchConstructor_ShouldPlugCtorCorrectly
    - PatchType_ShouldReplaceAllMethodsCorrectly
    - PatchType_ShouldPlugAssembly
    - AddMethod_BehaviorBeforeAndAfterPlug
- **Liquip.ilc.Tests**:
  Tests patched assemblies for runtime behavior on NativeAOT.

## Use
Two options are available to run the test suite.

- The first one is to run the tests inside Visual Studio.

- And the second is to run Liquip.Patcher.Tests using Docker. You have to go at the root of the repository and run:

`docker build -t patcher-tests .`

`docker run --rm patcher-tests`
