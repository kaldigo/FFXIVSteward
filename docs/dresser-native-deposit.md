# Native dresser deposit investigation — 2026-10-27

## Finding

The ordinary deposit operation is a member function on the already identified
`Client::Game::MirageManager`. It is not exposed by the inspected upstream
FFXIVClientStructs bindings. A local binding now calls it with the same ABI as
the game's confirmation handler:

`bool StoreOrdinary(MirageManager* self, InventoryType container, ushort slot)`

The installed executable has SHA256
`46a52ab1dbe80b9a8d6c4d4531bf66693904200c5135facacd053ef28a5de14a`.
All addresses below are relative virtual addresses for that executable, not
addresses hardcoded into the plugin.

## Independent static evidence

- The function starts at `0x894C10`, immediately before the known
  `StoreNewOutfit` at `0x894CE0`.
- The dresser agent confirmation branch at `0xDB16AF` clears its confirmation
  handle, requires affirmative result zero, checks the selected list index,
  then compares the selected container, slot and item against that list entry.
- At `0xDB1736` it obtains MirageManager. It passes the entry's slot as an
  unsigned 16-bit value in R8, the container in EDX and the manager in RCX,
  calls `0x894C10` at `0xDB174C`, and checks the byte return in AL.
- The target looks up that exact inventory slot, requires condition >= 30000,
  checks for item 21800 (a glamour prism), searches 800 dresser slots for an
  empty entry, and submits the ordinary deposit operation. Failure returns
  false; submission returns true and marks the manager's pending operation.
- The body and caller signatures each have exactly one match in the installed
  executable. The caller's relative target resolves to the body match.

`tools/inspect_dresser_native.py <ffxiv_dx11.exe>` repeats the read-only signature,
target and executable-hash verification with the Python standard library.
Detailed local disassembly is in the workspace's `.work/dresser-native-audit/`.
No injected hooks, speculative native calls or live process writes were used
to establish this finding.

## Implementation

`NativeDresserStore` requires agreement between both signatures. A missing or
inconsistent binding holds ordinary storage. It does not fall back to guessed
callbacks. Outfit pieces use FFXIVClientStructs' existing `StoreNewOutfit` and
`StoreExistingOutfit` bindings, preferring a matching-quality existing outfit
with that piece missing. Native outfit calls accept nine packed source entries;
we submit one approved source followed by invalid entries, so unrelated pieces
are never automatically included. HQ new outfits use the native HQ item-ID flag.

AutoRetainer still owns retainer withdrawals and travel. Steward retains its
live rule decision, gearset/upstream exclusions, quantity reserves, collection
ownership, dresser capacity reserve, repair/prism checks, owned control lease,
character identity and durable operation journal. Dresser compatibility is
checked separately from the rules defining which items are desirable glamour.

Native calls independently check execution and collection switches. Submission
is made only once. Acknowledgement requires a loaded source with exactly one
item removed and live MirageManager ownership of the exact item, including
unlocked outfit pieces. A timeout, rejection or uncertain result keeps the
review hold; it never retries the native call. No Yes/No prompt is opened or
accepted by this path. Existing unrelated dialogs defer submission.

The existing conservative capacity policy remains: it may defer additions to
partial outfits at the configured empty-slot reserve even though adding such a
piece does not consume another dresser slot. This change does not relax that
policy or convert already-stored loose pieces into outfits.

## Validation boundary

Static signatures and ABI/caller checks are verified against the installed
executable. Automated tests cover routing, quality, partial outfits, exact
source/destination acknowledgement and existing protection regressions.
Compilation and automated tests do not prove server acceptance. Ordinary
deposit, new outfit and existing-outfit addition each still require an observed
in-game transfer before being described as in-game tested. The verified implementation is included in preview release 0.1.0.44 for controlled
in-game testing; installed configuration and existing review holds are preserved.

References: [FFXIVClientStructs MirageManager](https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/MirageManager.cs)
and [native symbol names](https://github.com/aers/FFXIVClientStructs/blob/main/ida/data.yml).
The local ordinary binding was derived independently from the installed client;
no GlamourLog implementation was copied.


## Live 0.1.0.44 result and scanner correction

At 08:10:38 UTC, item 19099 created outfit 45280 and item 47243 created
outfit 47708. Both logged exact source consumption and live dresser ownership.
Ordinary storage stopped before submission because the runtime binding was null.

Dalamud `SigScanner.ScanText` follows a leading E8/E9 before returning an address.
Our caller pattern begins with the GetInstance call, so TryScanText returned the
GetInstance function rather than the caller location assumed by our offset arithmetic.
The static executable scan was correct but did not model this API behaviour.

Version 0.1.0.45 uses ScanAllText, which returns literal match locations, and requires
exactly one match for each signature. The target equality check remains mandatory.
The native-tests project compiles the actual production binding against host API
stubs, reproduces ScanText call-following behaviour with allocated test memory, and
checks successful literal resolution plus missing, ambiguous and mismatched cases.
No game function is invoked by these tests. Ordinary and existing-outfit native
acceptance still require live tests.
