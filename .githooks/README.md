# Git hooks (`.githooks/`)

Version-controlled git hooks for this repo. They live here (not in `.git/hooks`, which isn't
tracked) and are activated per-clone with one command.

## Enable

```sh
git config core.hooksPath .githooks
```

(Run once per clone. The setting lives in `.git/config`, which isn't committed.)

## `pre-push` — payroll gate + unit tests

Runs `PayrollGateTests` + `ResetBoundaryTests` + `PayrollModuleCompositionTests` before every
push and aborts the push if any fail. This is the **ToDo #36 residual**: an automated, local
guard so the integration suite can't silently rot again (it had been red for several phases
because nothing ran it).

**Requires** the local dev DB (`allworkhris_dev`) running, with current schema + seeds + the
`payroll_gate_test_fixture.sql` applied — the same prerequisites as running the gate suite by hand.

- **Bypass** (WIP push, or DB not running): `git push --no-verify`
- **Disable**: `git config --unset core.hooksPath`
- **Broaden**: edit the `FILTER` in `pre-push` to cover more suites once their DB state is reliable.
