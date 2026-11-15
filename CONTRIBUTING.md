# Contributing to Berth

Berth is built section by section from a validated TODO graph in [`todo/`](todo/TODO-00-INDEX.md). A change should either ship a section or capture new work as a section. A small bug fix with a regression test is also welcome.

## How do I start?

1. Read [AGENTS.md](AGENTS.md) for the project rules. [`design/SPEC.md`](design/SPEC.md) is the design contract, and [`docs/plans/2026-11-15-berth-foundation.md`](docs/plans/2026-11-15-berth-foundation.md) the architecture.
2. Run `python scripts/todo-graph.py query ready` to see which work can start now.
3. Turn on the repository's git hooks, once per clone: `git config core.hooksPath scripts/hooks`. A commit that would leave the generated plan stale or the TODO graph invalid is then refused before it lands. To undo it: `git config --unset core.hooksPath`.

## What do I need?

- Windows 11, version 24H2 or later.
- The .NET SDK that [`global.json`](global.json) names, once the solution exists.
- Python 3.11 or later, for the plan tooling in `scripts/`.
- Node.js, for `scripts/verify-agent-tiers.mjs`.

## Which checks must pass?

```
python scripts/todo-graph.py validate
python scripts/todo-graph.py plan --check
python -m unittest discover -s scripts/tests
node scripts/verify-agent-tiers.mjs
```

Once the solution exists (`D00 T01 §4`), one command runs these and the build and tests: `pwsh scripts/check-all.ps1`.

`todo/implementation-plan.md` is generated. Edit the TODO sections, then run `python scripts/todo-graph.py plan --sync`.

## What does a good change look like?

- It fixes the cause in the layer that owns the behaviour, not with a filter over the result.
- A bug fix has a regression test that fails before the fix and passes after it.
- A change to how something looks or reads goes to `design/SPEC.md` first, and every colour, size and type style comes from `design/tokens.json`.
- Status always has a word as well as a colour.
- A test never touches the user's own layouts or settings (it sets `BERTH_DATA`), and never moves a desktop icon it did not create.
- It never adds a secret or a personal path.
- It uses no em dashes or en dashes, in prose, code or commit messages.

## How do I report something?

Use the [issue forms](https://github.com/rizonetech/Berth/issues/new/choose) for bugs and ideas. Security problems go through [SECURITY.md](SECURITY.md), never a public issue.

## Under what licence are contributions made?

By contributing, you agree that your contribution is licensed under the [GNU General Public License, version 3 or later](LICENSE), the same as the project.

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
