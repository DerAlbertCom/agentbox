# Coding standards

## Simplicity

Keep complexity low. Favor readability and ease of change over cleverness, brevity
or dogmatic application of patterns.

- Keep control flow and data flow straightforward. Reduce nesting and branching
  when it makes behavior easier to follow, for example with guard clauses.
- Make intent explicit through clear names and direct operations rather than
  clever indirection.
- Add configuration only for real needs, not hypothetical future requirements.

## Architecture

The rules below guide code organization; [the implementation code map](docs/implementation.md#the-code)
documents the current directory layout.

- Use vertical slice architecture: keep a command, all its subcommands, slice
  registration and command-specific implementation together in one feature folder.
- Put features reused across slices in a shared-features folder, with one folder per
  shared feature, such as user-secret adaptation or sandbox creation orchestration.
  These feature modules own shared application orchestration above technical adapters.
  Technical modules keep one place each (`Configuration`, `Kits`, `LocalState`,
  `Launchers`, `Sbx`, `Health`, `Infrastructure`, `Naming`). Neither shared features
  nor technical modules import a command slice from `Abox.Features`.
- One wrapper class (`SbxClient`) makes every `sbx` call. The flags and the version
  check live in that one place. One wrapper class (`OciRegistry`) makes every `docker`
  call. The arguments are always a list, never a shell string.
- Runtime folder selections and their parsing/matching live in `Configuration`,
  not the shared copy orchestration. Portable store-ID validation lives in
  low-level `Naming.UserSecretId`; both configuration parsing and the sbx adapter
  validate with it. Thus `Configuration` and `Sbx` do not import `UserSecrets` or
  know its orchestration, while the copy flow can consume both. Reporting remains
  above the technical sbx wrapper. These are module responsibilities, not a rule
  that every namespace must be a separate module.
- Each launcher is one class behind `ILauncher`.
- Each command is a class that the container builds and that takes its dependencies
  in its constructor; nothing looks the commands up by hand. The root command holds
  every command the container has, one per slice.
- A slice registers itself: `AddAbox` calls one method per slice and that method adds
  its command and any slice-specific services. The composition root registers shared
  feature modules once for reuse across slices.
- The entry point builds a plain service provider, not a host: it does not load
  `appsettings.json` from the working directory. Commands defer configuration-dependent
  dependencies until their actions run, so help, `exec` and `pipe` need no user configuration.
- The services take their dependencies in their constructor, so the tests can hand them
  a substitute sbx (NSubstitute in the test project).
- The YAML is read in one place (`YamlReader`), so every file is read the same way and
  an unknown key is an error everywhere.

## SOLID principles

Use SOLID to support simplicity and ease of change. Prefer deep modules: hide
necessary complexity behind a small, coherent interface rather than pushing it
onto callers. Apply these principles within the vertical slices and shared
features described above, not as a reason to add layers or an interface for every
class.

- **Single responsibility:** give each module a cohesive responsibility and reason
  to change. Keep related knowledge together; this does not mean tiny classes.
- **Partial classes:** prefer one cohesive class in one file. Avoid splitting
  handwritten code across partial classes to manage size or separate responsibilities;
  needing that split is a signal to reassess the single responsibility principle
  and extract cohesive collaborators instead. Reserve partial classes for concrete
  language or tooling requirements, such as source-generated members, and keep the
  handwritten class cohesive.
- **Open/closed:** where real variation exists, support it through adapters at a
  deliberate seam rather than spreading conditional logic across callers. Do not
  build speculative extension frameworks for possible future requirements.
- **Liskov substitution:** every adapter must honor its interface's contract,
  including invariants, ordering constraints and error modes, so callers need no
  adapter-specific workarounds.
- **Interface segregation:** expose only what callers need to use a module
  correctly. Keep implementation details hidden without fragmenting a deep module
  into shallow, pass-through interfaces.
- **Dependency inversion:** keep application orchestration independent of technical
  details; put those details in adapters behind stable interfaces at real seams.
  Pass dependencies in, using abstractions for actual variation rather than
  requiring a separate interface for every concrete implementation.

## Testing

- Write behavior-driven unit tests: verify observable behavior and results through
  a module's interface, not its implementation details.
- Do not write tests merely to assert configuration values, constants or trivial
  implementations. Test meaningful configuration-dependent behavior instead of
  restating configuration values.
- Mark each test's phases explicitly with `// Arrange`, `// Act` and `// Assert`
  comments.
- Mirror the production feature folder structure in the test project: keep each
  command and its subcommands together, and group shared-feature tests by shared
  feature, following the architecture rules above.

## Domain language

Follow the [domain documentation rules](docs/agents/domain.md) for vocabulary and
decisions, including the root `GLOSSARY.md` when present.

## Formatting and hooks

The code is formatted with [CSharpier](https://csharpier.com), which comes with the
repository as a local tool:

```sh
dotnet csharpier format cli     # format the code
dotnet csharpier check cli      # check the formatting
```

The hook in `.githooks/pre-commit` checks the formatting before every commit. Enable
the hooks of the repository once:

```sh
git config core.hooksPath .githooks
```
