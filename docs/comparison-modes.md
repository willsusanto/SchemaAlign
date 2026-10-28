# Incremental and snapshot comparisons

[← SchemaAlign](../README.md#documentation)

Choose the mode **in the wizard or with `--mode` on diff/sync**. It is a per-run choice and does not belong in the JSON configuration file.

| Mode | Use it when… | Tables omitted from the desired schema |
| --- | --- | --- |
| **Incremental** — default | Your diagram contains only the tables relevant to this sprint. | Preserved. |
| **Snapshot** | Your desired schema describes the complete system. | Reported for removal. |

For example, sprint one introduces **MaskedAccount**. Sprint two's diagram describes only **MaskedRequest**. In incremental mode, SchemaAlign compares the sprint-two tables while preserving MaskedAccount and the rest of the existing system. You do not need to copy every earlier sprint's tables into each new diagram.

**Included tables still need their complete desired column definitions.** Incremental describes a partial set of tables, not a partial set of columns within each table.

| Item missing from the desired schema | Incremental | Snapshot |
| --- | --- | --- |
| Entire table | Preserve | Report removal |
| Column within an included table | Report removal | Report removal |
| Foreign key within an included table | Preserve | Report removal |

Both modes detect additions and modifications. Reporting a removal does not apply it: execution permissions, selected changes, and the destination's supported operations still determine what happens.
