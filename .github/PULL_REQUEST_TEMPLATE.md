## Summary

What does this change do for someone using or maintaining WorkTrail? A few sentences are enough.

## Why

What problem does it solve, and why did you choose this approach?

## Validation

List the commands and manual checks you ran. Say whether you checked the source,
ran builds or tests, tried an unpackaged build, or tested an installed package.

## Changelog and task records

For a notable user or contributor change, update `CHANGELOG.md` under `Unreleased`
in this PR and summarize the entry here. Otherwise, write `No changelog entry`
and briefly explain why, for example an internal refactor or documentation typo.

Use `.github/tasks/archive.md` as supporting evidence for completed work and verify
the entry against the final diff. Keep unfinished work in `.github/tasks/todo.md`
and durable engineering lessons in `.github/tasks/lessons.md`. Do not copy task
transcripts, private plans, or unverified fixes into the changelog.

## Screenshots

If the change affects the UI, add before/after images with private information removed.

## Checklist

- [ ] This pull request stays focused and leaves out unrelated changes.
- [ ] The changelog reflects the final PR scope, or the reason for no entry is recorded above.
- [ ] Applicable task and lesson records reflect what is completed, pending, and verified.
- [ ] New behavior goes through `IWorkTrailApplication`; UI and CLI code only collect input and display results.
- [ ] Public or protected C# APIs have XML documentation where required.
- [ ] New first-party C# files start with `// SPDX-License-Identifier: MIT`.
- [ ] I've recorded where new dependencies and assets came from and checked that their licenses allow this use.
- [ ] No credentials, private paths, personal screenshots, or generated build artifacts are included.
- [ ] I've considered privacy, translations, accessibility, and what happens if something fails.
- [ ] I've described any substantial AI help below, and a person has reviewed the result.

## AI assistance

What did AI tools help with, and what did you personally review? Write `None` if they weren't used.
