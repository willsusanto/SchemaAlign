## Context re-entry (multi-project juggling)

I am juggling several projects, each with several concurrent sessions, and have usually lost
the thread by the time I return to any one of them. Write every user-facing message for cold
re-entry — assume I remember nothing from the scrollback:

- **Open with a recap.** Before any summary, decision point, or question: 2–3 plain sentences on
  what we were just working on, why, and where it stands now.
- **Plain language.** No invented codenames, abbreviations, or callbacks like "the earlier fix"
  or "option B from before" — restate the thing in place, every time.
- **Self-contained questions.** When asking me to decide something, the question itself
  must carry everything needed to answer it: the background, the options, the tradeoffs, and
  your recommendation. Never require scrolling back.
- **One question at a time.** When a summary or decision point holds several open questions or
  next steps, say so up front ("three decisions are waiting; here's the first"), then present
  only the first and wait for the answer before raising the next. Never dump them all at once —
  it's too much mental load.
- **Anchor the work.** Name the project, branch, and PR when reporting status — several other
  sessions look just like this one.
- **End with the next action.** Close long updates with the single thing waiting on me,
  or say explicitly that nothing is.

## Worktrees

When starting any new feature or fix, begin by creating a separate git worktree from the base
branch and do all work inside it, so parallel agents never overwrite each other's changes.

When creating a new branch, always use the `feature/` prefix followed by a short,
descriptive, lowercase, hyphen-separated name, including for fixes.
For example, use `feature/markdown-truncation` with the worktree folder
`D:\Repo\SchemaAlign-markdown-truncation`.

Always place the new worktree beside the original project folder, under the same parent
directory. Name it `<project-name>-<feature-slug>`, using a short, descriptive, lowercase,
hyphen-separated slug derived from the feature or fix.
For example, if the original project is `D:\Repo\SchemaAlign`, a worktree for fixing markdown
truncation must be `D:\Repo\SchemaAlign-markdown-truncation`.
Derive the parent directory and project name from the original project checkout, even when
working from an existing worktree. Never nest worktrees inside the project or place them in
an unrelated directory. If the destination already exists, choose a distinct descriptive slug
or append a numeric suffix; never overwrite an existing folder.

After the work is merged, clean up by removing the worktree. For the next task, create a fresh
worktree from the latest base branch — don't reuse old trees.

## TDD is mandatory

Every change follows **failing test first → implement → verify**:
1. Write the test(s) that capture the desired behavior and watch them **fail** (red).
2. Implement the minimum to make them pass.
3. Run the suite + typecheck and confirm green.
4. Make sure to mask every table names, columns, types given. Never use real data.

Don't write implementation before a failing test exists. When fixing a bug, reproduce it with a
failing test first.

## Verify before claiming "done"

Never report something as working without running it. "Done" means: relevant tests green,
typecheck clean, and — for user-facing flows — exercised end to end (e.g. Playwright for web
flows). If tests fail or a step was skipped, say so plainly with the output.
