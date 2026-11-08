# The kits of the team

This folder holds the kits and the workloads that `abox build` turns into sandbox kits.

```
kits/
  mti-ca-dotnet.workload   the workload: the template, the tools and the scripts
  enable-fzf.sh            a script of the workload, run while the image is built
  pi-config/               a kit: a directory with the descriptor of the kit
    pi-config.yaml
  opencode-config/
    opencode-config.yaml
  github-enterprise-auth/
    github-enterprise-auth.yaml
  elastic-auth/
    elastic-auth.yaml
```

## The workload

A `.workload` file describes the base image of a team:

```yaml
template: docker.io/sbx/shell-docker-image   # the image the base is built on
mise:                                        # the tools, the last one is the default
  - nodejs@24.4.3
  - dotnet@10.0.403
scripts:                                     # run while the image is built
  - enable-fzf.sh
```

`abox build` writes a v3 kit from it: a descriptor (`workload.yaml`) and the recipe
(`workload.dockerfile`). The scripts are copied into the kit, so the kit directory is
the build context.

## The kits

A kit is a directory with a descriptor that follows the
[kit specification](https://github.com/docker/sandbox-kit-spec):

```yaml
# syntax=docker/sandbox-kit:3
schemaVersion: "3"
kind: mixin
displayName: pi configuration
version: "1.0.0"

capabilities:
  - type: com.docker.sandbox/agent-context@2
    config:
      directory: /home/agent/.pi/agent
      filename: AGENTS.md
      content: |
        You work in a sandbox.
```

A kit may also be a reference to a published kit, for example
`docker.io/acme/pi-config:1.0`. Such a kit is used as it is; abox does not build it.

## Where a squad names them

The `.abox` files live in the root of the repository and name the kits of the squad:

```yaml
base: cape
kits:
  - pi-config
  - docker.io/acme/elastic-auth:1.0
```

The name of a kit without a slash or a colon is a directory in this folder. The name of
a kit with a slash or a colon is a reference sbx resolves.
