# Worklog Template

> **Purpose**  
> This document is the living record of work done on a single branch.  
> It is created when the branch is opened, updated continuously while work is performed,  
> and finally used as the archive in the Pull Request.  
> The **Summary** section becomes the PR title + description (and therefore the merge commit message into `main`).

> **How to use (for humans and AI)**  
> 1. Copy this template when starting a new branch.  
> 2. Fill the metadata immediately.  
> 3. Keep the **Summary**, **Changelog** and **Work items** sections up to date as you work.  
> 4. Only record *net* changes in the Changelog (do not log intermediate attempts inside the same feature).  
> 5. Breaking changes must always be listed explicitly.  
> 6. Write for a reader who was not in the discussion (including yourself later). Record what landed and any rule that still applies. Do **not** record chat, rejected alternatives, or where drafts were kept.  
> 7. At the end of the work the whole document becomes the PR appendix: Summary → title and short description; Changelog, Breaking changes, and Work items → PR body. Do not commit the filled copy.

Process: [CONTRIBUTING.md](../../CONTRIBUTING.md#worklog).

---

# Worklog: [Short descriptive title]

- **Date:** YYYY-MM-DD
- **Author:** @username
- **Branch:** `feature/xxx` or `fix/yyy`
- **Issues:** #123, #456 (or links)
- **Plan:** [link to plan / issue / document] (optional)

## Summary

This section is used as the **PR title** and **PR description**.

Update it continuously.

```text
[PR title – short, imperative style, max ~70 characters]
```

```text
[PR description – 2–5 sentences.
What was done, why, any breaking changes, how to test.]
```

## Changelog

Only changes introduced by this worklog.  
Do **not** record intermediate iterations of the same feature  
(e.g. if feature A is added and later refined, only “Added A” remains).

This section is the input for the project [CHANGELOG.md](../../CHANGELOG.md). The maintainer copies it after merge. **Do not** edit `CHANGELOG.md` in the PR.

Follow [Keep a Changelog](https://keepachangelog.com/) format.

```markdown
### Added
- ...

### Changed
- ...

### Fixed
- ...

### Removed
- ...

### Deprecated
- ...

### Security
- ...
```

## Breaking changes

List every breaking change introduced by this worklog.  
If there are none, write `None`.

```markdown
- [description + migration notes if needed]
```

## Work items

Archive of the work performed.  
Each logical unit of work (issue, significant set of commits, decision that still applies, etc.) gets its own heading and short description.

Write so the item still makes sense a month later without the conversation that produced it. State the outcome (what is in the branch, and the rule if one was chosen). Do not list options that were not taken, quotes from review or chat, or draft locations.

Add new items as work progresses. Keep a consistent order (newest first or oldest first).

### [Work item title 1]

What was done, what changed, and any decision that remains in force. Links to commits / issues / PRs if useful.

### [Work item title 2]

...

### [Work item title N]

...
