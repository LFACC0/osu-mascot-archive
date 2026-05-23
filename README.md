# Osu! Mascot Archive

Welcome to this first stage of collaborative research and preservation profect for osu! mascot-related media, artwork, references, concepts and community history.

This archive aims to document both official and fan-created material in a structured format.

If you want to collaborate and test this system, feel free to follow the instructions and rules written down here. 
# osu! Mascot Archive — Collaboration Setup

## 1. Install Required Software

### Git

Arch / EndeavourOS:

```bash
sudo pacman -S git git-lfs
```

---

### Obsidian

Download:

https://obsidian.md/

---

# 2. Clone Repository

```bash
git clone git@github.com:USER/REPO.git
```

Example:

```bash
git clone git@github.com:FatalNight/osu-mascot-archive.git
```

---

# 3. Open Vault

In Obsidian:

```text
Open folder as vault
```

Select:

```text
osu-mascot-archive/
```

---

# 4. Install Community Plugins

Enable:

```text
Settings
→ Community Plugins
→ Turn off Safe Mode
```

Required plugins:

- Dataview
- Templater
- Metadata Menu
- Folder Notes
- Excalibrain
- Waypoint
- Custom File Explorer Sorting

---

# 5. Pull Latest Changes

Before starting work:

```bash
git pull
```

---

# 6. Standard Workflow

## Add new assets

Place files inside:

```text
07_Assets/
```

---

## Create entry

Create note inside:

```text
02_Artwork/
```

Example:

```text
PIP-0044.md
```

---

## Add metadata

Example:

```yaml
---
id: PIP-0044

title: Pippi winter outfit

character:
  - pippi

artist:
  - Daru

year: 2013

type:
  - official_art

canon: official

source:
  - osu!stream

tags:
  - character/pippi
  - era/stream

related:
  - "[[Daru]]"

status: archived
---
```

---

## Insert preview

```md
![[07_Assets/PIP-0044.webp]]
```

---

# 7. Staging Workflow

Temporary/raw material goes into:

```text
99_Staging/
```

Use staging for:

- unsorted assets
- missing metadata
- unknown artists
- unresolved sources
- possible duplicates

Move entries into the main archive once curated.

---

# 8. Save Changes

After finishing work:

```bash
git add .
git commit -m "describe your changes"
git push
```

Example:

```bash
git commit -m "added stream-era pippi artwork"
```

---

# 9. Important Rules

## Do not:

- rename IDs after creation
- delete source information
- upload compressed screenshots instead of originals
- reorganize folders without discussion
- overwrite collaborator work

---

## Prefer:

- consistent metadata
- small commits
- descriptive commit messages
- local asset preservation
- structured tags

---

# 10. Core Principles

Priority order:

1. Preservation
2. Consistency
3. Searchability
4. Scalability
5. Relationships

Perfect documentation is less important than sustainable archival workflow.
