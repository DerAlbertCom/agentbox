# Local kits with a build file are built into a staged copy

abox used to hand a local kit to sbx exactly as it lies in the repository. Kits whose payload is compiled, such as the TypeScript `sbox` commands, would then need their build output committed, where it can go stale. So a local kit folder with an executable `build` file is copied into the build's staging folder, `build <stagedKitDir>` runs with the kit's repository folder as the working directory (so it can reach sibling top-level packages), and the staged copy is what sbx sees for verification, `create --local` and publishing. Kits without `build` still pass through unchanged, and the kit descriptor format does not change, so sbx sees nothing new.

## Considered Options

- **Commit the build output.** Rejected: generated files in review, and they can drift from their sources.
- **Build in the kit's Dockerfile.** Rejected: every create would compile, and the toolchain would have to be in the image or a build stage that sbx assembles.
- **A descriptor field for the build.** Rejected: the descriptor belongs to the kit spec that sbx reads; an executable `build` file needs no new format.

## Consequences

- `--metadata-only` builds also run kit build steps, because publishing uses the staged copy. A build script may need tools such as Docker even though abox itself calls none in that mode.
- Publication fingerprints cover the staged copy, not the kit's repository folder or the packages its `build` reads. A source change after `abox build --no-personal` is not detected by `publish`; rebuild before publishing.
- Kit build steps need executable files, so they run on Linux and macOS hosts. On Windows a kit's `build` file is not recognized and the kit passes through unchanged.
