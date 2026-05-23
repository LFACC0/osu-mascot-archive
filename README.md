# osu-mascot-archive
## Core Philosophy

- Preserve first, organize later.
    
- Metadata matters more than long prose.
    
- Keep entries lightweight and scalable.
    
- Separate capture from curation.
    
- Folders are broad categories; YAML handles organization.
    

---

# Folder Structure

```text
osu-mascot-archive/

    00_Home/

    01_Characters/
    02_Artwork/
    03_Artists/
    04_Events/
    05_Eras/
    06_Concepts/

    07_Assets/
        official/
        community/
        contests/
        memes/
        screenshots/
        lost-media/

    08_Templates/

    99_Staging/
        00_inbox/
        01_needs-id/
        02_needs-source/
        03_needs-tags/
        04_possible-duplicates/
        05_unverified/
```

---

# Required Plugins

- Dataview
    
- Templater
    
- Metadata Menu
    
- Folder Notes
    
- Excalibrain
    
- Custom File Explorer Sorting
    
- Waypoint
    

Recommended theme:

- Minimal Theme
    
- Style Settings
    

---

# Folder Philosophy

Avoid deep nesting.

BAD:

```text
Artwork/Pippi/Official/2013/Winter/
```

GOOD:

```text
02_Artwork/PIP-0044.md
```

Organization should happen through:

- YAML
    
- tags
    
- Dataview
    
- relationships
    

---

# Entry IDs

```text
PIP-0001
YUZ-0001
MOCH-0001
```

IDs should never be reused.

---

# Asset Naming

Recommended:

```text
PIP-0044_daru_2013_official.webp
```

Minimal alternative:

```text
PIP-0044.webp
```

---

# Canon Classification

```yaml
canon: official
```

Possible values:

- official
    
- semi-official
    
- community
    
- fanon
    
- unknown
    
- lost-media
    

---

# Tagging

GOOD:

```yaml
tags:
  - character/pippi
  - source/official
  - era/lazer
  - type/fanart
```

BAD:

```yaml
#pippi #cute #animegirl
```

---

# Metadata Template

```yaml
---
id: PIP-0044

title: Pippi winter outfit

characters:
  - pippi

artists:
  - Daru

year: 2013

type:
  - official-art

canon: official

source:
  - osu!stream

source_url:
  - https://...

tags:
  - character/pippi
  - era/stream

related:
  - "[[osu!stream era]]"

status: archived
---
```

---

# Standard Entry Structure

```md
---
(here goes the metadata)
---

# Title of the entry

![[PIP-0044.webp]]

## Notes

Used during osu!stream promotional material.
Any short description about the entry.
```

---

# Workflow

```text
Find material
    ↓
Save asset locally
    ↓
Create note from template
    ↓
Fill YAML metadata
    ↓
Insert preview
    ↓
Add minimal notes
    ↓
Archive complete
```

---

# Staging Philosophy

Purpose:

- inbox
    
- raw intake
    
- unresolved material
    
- temporary processing
    

Workflow:

```text
Find content
    ↓
Store in staging
    ↓
Process later
    ↓
Move into archive
```

Staging is NOT a permanent storage.

---
# Extra tools (for more mature archive stage)
#### Relationships

Example:

```yaml
related:
  - "[[pippi]]"
  - "[[Daru]]"
  - "[[osu!stream era]]"
```

---

#### Character Hub Example

````md
# Pippi

```dataview
table year, artist, type
from "02_Artwork"
where contains(character, "pippi")
sort year asc
```
````

---

# Git Workflow

Initialize repository:

```bash
git init
```

Save changes:

```bash
git add .
git commit -m "added pippi archive entries"
```

Upload:

```bash
git push
```

Download collaborator changes:

```bash
git pull
```

---

# Git LFS

```bash
git lfs install
git lfs track "*.png"
git lfs track "*.jpg"
git lfs track "*.webp"
```

---

# .gitignore

```gitignore
.obsidian/workspace*
.obsidian/cache
.trash
.DS_Store
```

Do not ignore the entire `.obsidian/` folder.

---

# Long-Term Goal

The archive may eventually evolve into:

- public wiki
    
- fandom preservation project
    
- historical database
    
- visual relationship graph
    
- research resource
    
- collaborative archive
    
- Anything else
---

# Final Principle

Prioritize:

1. Preservation
    
2. Consistency
    
3. Searchability
    
4. Scalability
    
5. Relationships
    

A sustainable workflow matters more than perfect documentation.
