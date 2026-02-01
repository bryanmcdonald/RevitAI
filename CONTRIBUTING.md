# Contributing to RevitAI

Thank you for your interest in contributing to RevitAI! This document outlines the process and guidelines for contributing.

## Code of Conduct

We're committed to providing a welcoming and respectful environment for all contributors. Please:

- Be respectful and constructive in discussions
- Focus on the technical merits of ideas
- Welcome newcomers and help them get started
- Accept constructive criticism gracefully

## How to Contribute

### Reporting Issues

- **Search existing issues first** to avoid duplicates
- **Use a clear title** that summarizes the problem
- **Include details**: Revit version, steps to reproduce, expected vs actual behavior
- **Add screenshots** if applicable (especially for UI issues)

### Submitting Pull Requests

1. **Fork the repository** and create your branch from `master`
2. **Follow the coding standards** outlined below
3. **Ensure the build passes** before submitting
4. **Sign your commits** with a DCO sign-off (see below)
5. **Open a pull request** with a clear description of your changes

## Developer Certificate of Origin (DCO)

This project requires a DCO sign-off on all commits. This certifies that you have the right to submit your contribution under the project's GPL-3.0 license.

### How to Sign Off

Add a sign-off line to your commit message:

```
Signed-off-by: Your Name <your.email@example.com>
```

You can do this automatically with `git commit -s`:

```bash
git commit -s -m "Add new feature"
```

### What You're Certifying

By signing off, you certify that:

1. You created the contribution yourself, OR
2. It's based on previous work with a compatible open-source license, OR
3. It was provided to you by someone who certified (1) or (2)

See [developercertificate.org](https://developercertificate.org/) for the full DCO text.

## Coding Standards

### General Guidelines

- **Follow existing patterns** - Match the style of surrounding code
- **Keep changes focused** - One feature or fix per PR
- **Write clear commit messages** - Explain the "why", not just the "what"

### GPL-3.0 License Headers

All new source files **must** include the GPL license header. See [CLAUDE.md](CLAUDE.md#0-add-gpl-license-headers-to-new-files) for the required header templates.

**For C# files:**
```csharp
// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
```

**For XAML files:**
```xml
<!--
    RevitAI - AI-powered assistant for Autodesk Revit
    Copyright (C) 2025 Bryan McDonald

    This program is free software: you can redistribute it and/or modify
    ...
-->
```

### C# Conventions

- Use file-scoped namespaces (`namespace RevitAI.Services;`)
- Use nullable reference types
- Prefer `var` when the type is obvious
- Use meaningful names over comments where possible

### Revit API Guidelines

- **All Revit API calls must run on the main thread** via `ExternalEvent`
- Use the threading infrastructure in `RevitAI.Threading`
- Wrap model modifications in transactions via `TransactionManager`

### Tools

When adding new Claude tools:

1. Implement the `IRevitTool` interface
2. Place in the appropriate folder (`ReadTools/`, `ModifyTools/`, `ViewTools/`)
3. Register in `App.RegisterTools()`
4. Include the GPL license header

## Development Setup

### Prerequisites

- Visual Studio 2022 (17.8+) with .NET 8 SDK
- Revit 2026 installed
- Anthropic API key for testing

### Building

```bash
git clone https://github.com/bryanmcdonald/RevitAI.git
cd RevitAI
```

Open `RevitAI.sln` in Visual Studio and build. The output automatically deploys to your Revit addins folder.

### Project Structure

- **CLAUDE.md** - Development guide and architecture overview
- **docs/** - Detailed implementation specs by phase
- **src/RevitAI/** - Main plugin source code

## Pull Request Process

1. **Create a descriptive PR title** - e.g., "Add get_family_instances tool" not "Fixed stuff"
2. **Fill out the PR template** (if provided)
3. **Link related issues** - Use "Fixes #123" to auto-close issues
4. **Respond to feedback** - Address review comments or explain your reasoning
5. **Keep the PR updated** - Rebase on master if there are conflicts

### PR Checklist

- [ ] Build passes locally
- [ ] New files include GPL license headers
- [ ] Commits are signed off (DCO)
- [ ] Code follows existing patterns
- [ ] PR description explains the changes

## Questions?

- **Issues**: Open a GitHub issue for bugs or feature requests
- **Discussions**: Use GitHub Discussions for questions and ideas

Thank you for contributing to RevitAI!
