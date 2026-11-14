# XtraSearch behavior evidence

This is an independently written WPF implementation. No original executable code or graphical assets are used. Decompiled output was read to understand contracts; it was not copied into the application.

## Code observations

| Rule | Evidence | XtraSearch behavior |
| --- | --- | --- |
| Separate file-name and containing-text criteria | AgentRansack.exe `0x1400e1b80`, evidence `ev_5aade8ce0b3037ceeae6d36a457c0ab3ce5fdc1c4209bc8897ff9aec268c73de` | Independent match modes and case flags |
| Copy criteria before search; start engine on a worker thread | AgentRansack.exe `0x1400b8060`, evidence `ev_fa79af5cf296a1ef926ffb8e0a19b72c689ecd2d8d9301d1dc9a2bb0ec5a4fa8` | Immutable SearchOptions; background tasks; UI receives batches |
| Notifications include delays 50, 100 and 150 | AgentRansack.exe `0x1400b8060`, notification IDs `0x8072`, `0x8064`, `0x8065` | UI samples progress every 100 ms. Original delay units are inferred as milliseconds, not independently timed |
| Automatic worker count = number of logical processors; clamp 1..1024 | SearchLib.dll `0x1800af8f0`, evidence `ev_4c06f75d783abcec211496b9cca326399c8baf74a54025d29699800d1a4c47bc` | Same automatic formula and bounds. UI value 0 selects automatic |
| Other original speed choices: half cores, one worker, configured count | SearchLib.dll `0x1800af8f0` | Custom numeric field covers these counts |
| Expression engines separated from file filtering | SearchLib.dll `0x180048e10`, evidence `ev_ff9447e5cfc0a95fdeb22b18f5a469c93c555d8b8c9d048ad4451af30670e41f` | Compiled ExpressionMatcher reused by workers |
| Content extraction uses a container and a content searcher | AgentRansack.exe `0x1400a0820` | Own streaming readers, PdfPig and Open XML text readers |
| Regex tester case flag defaults false | AgentRansack.exe `0x1400f9490`, evidence `ev_053612cd1bcca30b4f65e1e49a4a97bebda7bbb25e966eea94225b561c37d957` | Both case flags start false (main-screen default inferred) |
| Binary, document processing, OCR and email flags are independent | AgentRansack.exe `0x1400d3570`, evidence `ev_7f97ed1fa2ee822b82859efa4f5afe7061fdffc5989fe7521ba8113fe5c97b2d` | Core edition provides binary skip and modern document extraction. No license/edition checks |

## Observed layout

Original capture evidence `ev_6c9a63d1de645e6e81e011d0cb92b3f699d9c7889aaac474fe319f5b487bd11a`: Main / Options / Dates; filename, containing text, Look in; Subfolders checked; size in KB with strict > and <; modified after/before; Start/Stop; result table; right-side statistics. Original was 1464 × 1002. XtraSearch intentionally uses a more spacious dark WPF layout, neutral graphics, English text and its own name. It starts at Documents with `*`, rather than reproducing a user's saved network location.

## Rules corroborated by installed English help

Read from the original installation's `help/en/boolean_expressions.htm`, `dos_expressions.htm`, `look_in.htm`, and `threadspriority_settings.htm` after inspecting the binary:

- Boolean expressions apply across a whole file by default; optional line-by-line behavior. Operators are uppercase AND, OR, NOT, grouping and quoted phrases; adjacent terms imply AND. REGEX selects a regex subterm.
- Wildcard expressions containing `*` or `?` enforce start/end boundaries. A bare name searches as a substring; a dot implies an end boundary. Semicolons separate filename patterns. `*.*` includes extensionless files in this implementation (estimated compatibility behavior after code investigation).
- Multiple paths use semicolons. `!` identifies excluded paths. Environment variables expand before searching. Shared/network paths work through ordinary Windows filesystem APIs.
- Regex subterms are evaluated per line. PDF page text is treated as one logical line, and Office text as paragraphs/rows.
- Size comparisons are strict. KB = 1024 bytes is an estimated compatibility choice following code/UI inspection. Date-only bounds include the chosen days; exact original timestamp inclusivity was not established.

## Independent performance and display decisions (estimated)

- Bounded work queue: 512 paths. Worker count follows the observed formula. Literal matching uses ordinal string comparison; regexes compile once, with a 2-second timeout per matching operation.
- Results have no artificial count limit. WPF row/column virtualization is enabled. UI drains up to 500 results per 100-ms update.
- Each result stores up to 200 matching snippets, each at most 2000 characters. The number of matching lines is still fully counted; this is not a result cap. Preview without content search reads at most 200 logical lines.
- Directory reparse points are skipped to prevent cycles. Hidden/system files are included by default. Failures are counted; first 200 messages are retained. Files are never modified by search.
- UTF-8 and BOM-marked UTF-16/UTF-32 use StreamReader detection. Binary skip detects NUL in the first decoded line. Legacy non-Unicode encodings are not automatically identified.
- ZIP entries are searched as text; nested archives and document extraction inside ZIP entries are not implemented. Modern Office/OpenDocument outer files and ordinary PDF files are supported. OCR, email databases, indexing, legacy Office, proximity/fuzzy operators and original plugins are not part of this core desktop implementation.
- `--screenshot` opens without activation, sets Windows NOACTIVATE and bottom z-order, forces 120 refresh ticks, renders the WPF tree to PNG and exits. No fixture results are inserted into normal or screenshot startup.

## Verification

`--self-test` creates fixtures only beneath the working directory and records assertions and a local 2500-file content-search timing. This does not establish relative speed against Agent Ransack. Build with `dotnet build -m:1`. All verification artifacts stay inside app.
