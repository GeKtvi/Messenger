# Pingo

Learning project for interview prep: a simple messenger built from microservices
(ASP.NET Core + EF Core under `Services/`, Blazor WebAssembly + MudBlazor under `Frontend/`).

## Decision log (required)

`docs/decision-log.md` records every architecture, tooling and convention decision.
Keeping it current is part of finishing any task, not optional.

- Before starting work, read the log so you don't contradict an accepted decision.
  If a request conflicts with one, say so and ask before going ahead.
- Whenever you make or accept a non-trivial decision, add a `D-NNN` entry using the
  template at the top of the log. Examples: a new project, package, pattern, naming
  convention, or data/storage choice, or a deliberate trade-off or shortcut.
  Typos, formatting and pure bug fixes don't need an entry.
- Add the entry in the same commit as the change that implements it.
- Never rewrite an accepted entry's decision. Add a new entry and set the old one's
  status to `Superseded by D-NNN`.
- When wrapping up a task, say which entries you added or superseded, or that none
  were needed.

## Tests

- Run `dotnet test Pingo.slnx` before committing a code change, and don't commit with failing tests.
  The concurrency tests need Docker running. If Docker isn't available, run
  `--filter "Category!=Docker"` and say in your wrap-up that those tests weren't run.
- When you change Message service behaviour, add or update a component test in
  `Services/Message/Pingo.Messages.ComponentTests` that covers it.
- Stop a running API first if the build reports a locked `Pingo.Messages.Api.exe`.

## Homework stage tags

- Each finished homework stage is tagged with an annotated tag, numbered with
  two digits: `01`, `02`, `03`, ...
- Before tagging, add the stage to the "Homework stages" table in `README.md`
  and commit it, so the tagged commit contains its own entry.
- Tag with `git tag -a <NN> -m "Stage <NN>: <short description>"`, then push with
  `git push origin main --follow-tags`.
- Never move or reuse an existing stage tag. Fixes after review go into the next stage.
