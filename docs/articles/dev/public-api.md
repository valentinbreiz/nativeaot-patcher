# Public API Tracking

The public surface of the kernel packages is declared in text files checked into the repository, enforced by Roslyn analyzers at build time, guarded against breaking changes at release time, and published as one frozen documentation site per release. This page states the policy that decides what is public, then the three mechanisms and the contributor workflow they impose.

---

## The policy

Three rules decide what is public:

1. **One supported ring.** `Cosmos.Kernel.System` is the API kernels program against: tracked, documented, frozen at release, with deprecation cycles applying there and only there. Contract types that the ring's own signatures expose ride along as public: the device interfaces in `Cosmos.Kernel.HAL.Interfaces`, the VFS contracts in `Cosmos.Kernel.HAL.Vfs`, and the platform interfaces in `Cosmos.Kernel.Core` (`IPortIO`, `ICpuOps`, `IPowerOps`, `IInterruptController`).
2. **Chosen experimental seams.** Extension points are opened deliberately and marked `[Experimental]`: public and usable now, no compatibility promise, promoted to stable by removing the attribute once proven. Referencing one is a build error until the consuming project suppresses its diagnostic ID, which is the consumer's acknowledgement of that contract. The scheduler policy seam in Core is the first, the packet seam in System is the second; a driver kit in the HAL is the planned third.
3. **Everything else is internal.** Visibility is never the extension mechanism; seams are. First-party assemblies and the white-box test kernels reach internals through `InternalsVisibleTo`; anyone else goes through `[UnsafeAccessor]`/`[UnsafeAccessorType]` ([Accessing internals](accessing-internals.md)) and accepts that internals change without notice.

The enforcement test is mechanical: `examples/DevKernel` must compile with no `InternalsVisibleTo` grant. If DevKernel needs a symbol, the symbol becomes public or gets a `Cosmos.Kernel.System` facade; if it does not, the symbol stays internal.

| Assembly | Surface |
|----------|---------|
| `Cosmos.Kernel.System` | The supported ring |
| `Cosmos.Kernel.HAL.Interfaces` | Device and platform contracts, tracked |
| `Cosmos.Kernel.HAL` | VFS contracts only, tracked; drivers, PCI, ports internal |
| `Cosmos.Kernel.Core` | The scheduler seam (`[Experimental]`) plus the platform contract interfaces, tracked |
| Arch assemblies, Native, Plugs, Debug, Boot.Limine | Internal, `InternalsVisibleTo` for first-party |

The last row is policy rather than tracked enforcement: those assemblies are untracked, and their remaining public types shrink as they are touched.

Experimental seams carry diagnostic IDs:

| ID | Seam |
|----|------|
| `COSMOS0001` | The scheduler policy seam: `IScheduler`, `SchedulerManager`, `Thread`, `PerCpuState`, `SchedulerExtensible`, `InterruptMaskScope`, `ThreadState`, `ThreadFlags` ([Scheduler - Writing a Scheduler](scheduler-plugging.md)) |
| `COSMOS0002` | The packet seam: the protocol packet types (`EthernetPacket`, ARP, `IPPacket`, ICMP, `UdpPacket`, DHCP, DNS, `TcpPacket`), `NetworkStack.Send`/`HandlePacket`, and the client members that take or return packets ([Network - Crafting packets](../user/network.md#crafting-packets)) |

---

## The declared surface

Projects opt in with `<CosmosTrackPublicApi>true</CosmosTrackPublicApi>` in their `.csproj` (wired in `Directory.Build.props`). That covers `Cosmos.Kernel.System`, `Cosmos.Kernel.Core`, `Cosmos.Kernel.HAL`, and `Cosmos.Kernel.HAL.Interfaces`.

An opted-in project references [Microsoft.CodeAnalysis.PublicApiAnalyzers](https://github.com/dotnet/roslyn-analyzers/blob/main/src/PublicApiAnalyzers/PublicApiAnalyzers.Help.md), which requires every `public` symbol to appear in one of two files next to the `.csproj`:

| File | Contents |
|------|----------|
| `PublicAPI.Shipped.txt` | The surface of the last release. Frozen: it only changes when a release is cut. |
| `PublicAPI.Unshipped.txt` | Surface added, changed, or removed since then. Emptied into `Shipped` at release time. |

Two diagnostics enforce the contract, both raised as build errors:

| Rule | Meaning |
|------|---------|
| `RS0016` | A public symbol exists in code but is not declared in either file. |
| `RS0017` | A declared symbol no longer exists in code. |

The effect is that any change to the public surface, deliberate or accidental, must land in the same commit as a diff of `PublicAPI.Unshipped.txt`, where the review can see it.

Three categories stay out of the files, the first two through `.editorconfig` overrides that set `RS0016`/`RS0017` to `none` under their paths:

- The vendored directories (`SharpZipLib`, the PNG decoder, the TrueType fonts). Their types that are still `public` leak into the package anyway; making them `internal` is part of the pre-release surface cleanup, and keeping them out of the files means that cleanup will not churn the declared surface.
- The generated `KernelVersion.g.cs` that carries `Kernel.VersionString`: the declared-API format records constant values, and this one changes with every version stamp.
- `internal` symbols, including everything exposed to the test kernels through `InternalsVisibleTo`.

### Changing the public API

1. Make the code change. The build now fails with `RS0016` or `RS0017`.
2. Run `make api` from the repository root. It rebuilds the tracked projects and applies the analyzer's code fixes to `PublicAPI.Unshipped.txt`. The IDE quick fix ("Add to public API") on each diagnostic is equivalent.
3. Commit the txt diff together with the code.

Removing or changing a symbol that already shipped is recorded as a `*REMOVED*` line in `Unshipped`, which is exactly what it is: a breaking change, visible as such in the PR. Before the first release, while `Shipped` is empty, a removal simply drops the line.

---

## Package validation

The same `CosmosTrackPublicApi` flag enables [NuGet package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/package-validation/overview) on the project, and `Directory.Build.props` holds the baseline knob:

```xml
<CosmosApiBaselineVersion></CosmosApiBaselineVersion>
```

The property is empty until the first Gen3 release is published on NuGet.org. Once it is pinned to that version, two guards activate on their own:

- `PackageValidationBaselineVersion` makes every pack compare the package against the baseline release and fail on binary breaking changes.
- The `API compatibility guard` step in `release.yml` downloads the baseline package from NuGet.org and runs [Microsoft.DotNet.ApiCompat.Tool](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/global-tool) against the freshly built one, as a final gate before publishing.

An intentional breaking change is recorded in a `CompatibilitySuppressions.xml` next to the project (`dotnet pack /p:ApiCompatGenerateSuppressionFile=true` generates it), so it too becomes a reviewable diff.

---

## Versioned docs

The docs site at the root of `gh-pages` follows the main branch: it is the dev documentation, rebuilt by `build-docs.yml` on every docs push. Releases get frozen copies next to it, published by the `publish-docs` job of `release.yml` when a `v*` tag is pushed:

| Path | Contents |
|------|----------|
| `/` | Dev docs, follows main. |
| `/vX.Y.Z/` | The docs as built from tag `vX.Y.Z`, never rebuilt. |
| `/latest/` | Alias of the newest release, stable URL for external links. |
| `/versions.json` | The release list, newest first, plus the `latest` marker. |

The version dropdown in the site navbar comes from `docs/templates/custom/public/main.js`. It reads `versions.json`, lists `dev` plus every release, and keeps the reader on the same page across versions when the page exists there. It only appears once `versions.json` exists, i.e. after the first tagged release; until then the site behaves exactly as before.

The dev deploys use `keep_files: true` so they never wipe the `v*/` folders, at the cost of deleted dev pages lingering on the branch until overwritten.

---

## Release checklist

What cutting a release changes in this system:

1. Tag `vX.Y.Z`. CI builds the packages, runs the API compatibility guard, publishes to NuGet.org, and freezes `/vX.Y.Z/` docs.
2. Move the contents of `PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt` (drop the `*REMOVED*` pairs), leaving `Unshipped` empty.
3. Set `CosmosApiBaselineVersion` in `Directory.Build.props` to `X.Y.Z` so the next cycle validates against this release.
