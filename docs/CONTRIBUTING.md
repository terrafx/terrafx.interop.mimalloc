# Contributing to TerraFX

The following is a set of guidelines for contributing to TerraFX.

## Table of Contents

* [Code of Conduct](#code-of-conduct)
* [License](#license)
* [What should I know?](#what-should-i-know)
* [Development and coding agents](#development-and-coding-agents)
* [Pull Requests](#pull-requests)
* [Issues](#issues)
* [Questions](#questions)

### Code of Conduct

TerraFX and everyone contributing (this includes issues, pull requests, the
wiki, etc) must abide by the [CODE_OF_CONDUCT](CODE_OF_CONDUCT.md).
Instances of abusive, harassing, or otherwise unacceptable behavior may be
reported by contacting the project team at admin@terrafx.dev.

### License

Copyright © Tanner Gooding and Contributors. Licensed under the MIT License
(MIT). See [LICENSE](../LICENSE.md) in the repository root for more information.

### What should I know?

Most of the basics of the project, such as what it is about, and its goals are
covered in our [README](README.md).

### Development and coding agents

See [repository guidance](../AGENTS.md) for the source layout, allocator
conventions, and build/test commands. Copilot uses the repository instructions
and app commands under `.github`; the
[mimalloc maintainer agent](../.github/agents/mimalloc-maintainer.agent.md)
provides an allocator-focused role. Task-specific skills cover
[upstream porting](../.github/skills/port-upstream-mimalloc/SKILL.md) and
[benchmarking](../.github/skills/benchmark-mimalloc/SKILL.md).

The cloud agent's `copilot-setup-steps` workflow installs .NET and restores
dependencies. It does not replace the build and validation for a change.

### Pull Requests

All pull requests should follow our
[PULL_REQUEST_TEMPLATE](PULL_REQUEST_TEMPLATE.md). It is additionally
recommended that an issue be opened, discussed, and approved first to ensure
that the change will be accepted. Any pull requests not following pull request
template will be requested to be updated. Any pull requests opened without a
corresponding issue may be delayed or be required to undergo further changes
before being accepted.

### Issues

All issues should follow our [ISSUE_TEMPLATE](ISSUE_TEMPLATE.md). It is
additionaly recommended to prefix your issue with an appriopriate "tag" such as
`[QUESTION]`, `[BUG]`, `[REGRESSION]`, `[IDEA]`, or `[PROPOSAL]`.

### Questions

Feel free to open an issue prefixed with `[QUESTION]`.
