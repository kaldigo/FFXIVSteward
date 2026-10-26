"""Build the bundled protection list from reviewed public EC observations; no network calls."""
from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path
from urllib.parse import parse_qs, urlparse

ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "docs/data/common-glamour-evidence.json"
BUNDLE = ROOT / "Steward/data/common-glamour.json"
REPORT = ROOT / "docs/common-glamour.md"
JOBS = "pld war drk gnb whm sch ast sge mnk drg nin sam rpr vpr bst brd mch dnc smn blm rdm pct blu crp arm ltw alc bsm gsm wvr cul btn fsh min".split()
SLOTS = "head body hands legs feet weapon offhand earrings necklace bracelets ring".split()
EC = "https://ffxiv.eorzeacollection.com"
PREVIOUS_IDS = {20819, 20820, 20821, 20822, 20823, 20849, 20850, 20851, 20852, 20853}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def source_url(url):
    parsed = urlparse(url)
    require(parsed.scheme == "https" and parsed.netloc == "ffxiv.eorzeacollection.com", "Untrusted source URL")
    return parsed


def validate_usage(row):
    require(row.get("measurementVersion") == 2, "Usage record needs complete-page verification")
    require(re.fullmatch(r"/glamours/(" + "|".join(SLOTS) + r")/[1-9]\d*", row["search"]), "Invalid item search")
    _, _, slot, piece = row["search"].split("/")
    pages, last, size = row["pages"], row["lastPageCount"], row["pageSize"]
    require(type(pages) is int and pages >= 1 and size == 36, "Invalid pagination")
    require(type(last) is int and 1 <= last <= size, "Missing final-page evidence")
    require(row["uses"] == (pages - 1) * size + last, "Usage count differs from observed pagination")
    likes = row["topPageLikes"]
    require(len(likes) == min(size, row["uses"]), "Incomplete likes sample")
    require(all(type(v) is int and v >= 0 for v in likes), "Invalid likes")
    require(likes == sorted(likes, reverse=True), "Likes page is not ordered by loves")
    for field, page in [("firstPage", 1), ("lastPage", pages)]:
        parsed = source_url(row[field])
        query = parse_qs(parsed.query, keep_blank_values=True)
        require(parsed.path == "/glamours", "Unexpected results endpoint")
        expected = {"filter[orderBy]": "loves", "filter[job]": "all", "filter[datePeriod]": "any",
                    "filter[gender]": "any", "filter[server]": "any", "filter[minimumLvl]": "1",
                    "filter[maximumLvl]": "100", "page": str(page), f"filter[{slot}Piece]": piece}
        require(all(query.get(k) == [v] for k, v in expected.items()), "Results scope is not all-time/all-jobs/exact-item")
        require(all(not any(values) or key in expected for key, values in query.items()), "Unexpected active results filter")
    require(bool(row["checkedAt"]), "Missing observation time")
    source_url(row["mostLikedPost"])


def rank(item):
    return (-item["uses"], -item["maxLikes"], -item["topPageLikesTotal"], item["itemId"])


def preserve_existing(previous, replacement):
    removed = {i["itemId"] for i in previous["items"]} - {i["itemId"] for i in replacement["items"]}
    require(not removed, f"Previously bundled protection would be removed: {sorted(removed)}; review removals explicitly")


def build(evidence):
    require(evidence["schemaVersion"] == 1, "Unsupported evidence schema")
    require(evidence["minimumUses"] == 25, "Changing the reviewed frequency floor requires review")
    strata = evidence["discovery"]["strata"]
    require(Counter((r["job"], r["order"]) for r in strata) == Counter((job, order) for job in JOBS for order in ["date", "loves"]), "Incomplete or duplicate job strata")
    observed = {r["path"]: r for r in evidence["observations"]}
    for row in strata:
        source_url(row["url"])
        require(len(row["posts"]) == 6, "Incomplete discovery stratum")
        require(len({p["url"] for p in row["posts"]}) == 6, "Duplicate outfits within a discovery stratum")
        for post in row["posts"]:
            require(post["url"] in observed, "An outfit was not inspected")
            require(type(post["likes"]) is int and post["likes"] >= 0, "Invalid discovery likes")
    seen_slots = {path.split("/")[2] for row in observed.values() for path in row["pieces"]}
    require(set(SLOTS) <= seen_slots, "An inventory equipment slot was not assessed")
    candidates = evidence["catalogue"]["rows"]
    require(len({c["facts"]["id"] for c in candidates}) == len(candidates), "Duplicate game item IDs")
    census = evidence["census"]
    require(census["year"] == 2025 and census["source"] == EC + "/year-in-review/2025", "Unreviewed census source")
    require(Counter(a["name"] for a in census["assessments"]) == Counter(i["name"] for i in census["items"]), "Incomplete official census cross-check")
    candidate_ids = {c["facts"]["id"] for c in candidates}
    for assessment in census["assessments"]:
        if assessment["decision"] == "candidate":
            require(assessment["facts"]["id"] in candidate_ids, "Eligible official census seed was missed")
    usage = {r["search"]: r for r in evidence["usage"]["rows"]}
    require(len(usage) == len(evidence["usage"]["rows"]), "Duplicate usage records")
    for row in usage.values():
        validate_usage(row)
    items = []
    for candidate in candidates:
        facts = candidate["facts"]
        require(candidate["name"].casefold() == facts["name"].casefold(), "Game name mismatch")
        require(type(facts["id"]) is int and facts["id"] > 0, "Invalid game item ID")
        require(facts["level"] > 1 and facts["ilvl"] > 1 and not facts["pvp"] and not facts["armoire"], "Already protected candidate")
        require(facts["rarity"] <= 3 and (facts["rarity"] != 3 or facts["marketable"]), "Rarity-protected candidate")
        acquisition = candidate.get("acquisition", {}).get("kind", "Unknown")
        if acquisition == "Special":
            continue
        require(candidate["search"] in usage, "Unmeasured candidate: " + candidate["name"])
        count = usage[candidate["search"]]
        require(count["itemId"] == facts["id"] and count["name"] == candidate["name"], "EC IDs were confused with game IDs")
        if count["uses"] < evidence["minimumUses"]:
            continue
        items.append({"itemId": facts["id"], "name": facts["name"], "source": EC + candidate["search"],
                      "slot": candidate["search"].split("/")[2], "uses": count["uses"],
                      "maxLikes": count["topPageLikes"][0], "topPageLikesTotal": sum(count["topPageLikes"]),
                      "acquisition": acquisition, "checkedAt": count["checkedAt"]})
    items.sort(key=rank)
    require(PREVIOUS_IDS <= {i["itemId"] for i in items}, "A previously protected Skallic item would lose protection")
    bundle = {"schemaVersion": 1, "checkedAt": evidence["checkedAt"], "minimumUses": evidence["minimumUses"],
              "description": "Frequency-led EC collection candidates from a job-balanced discovery sample and official census cross-check. All-time visible uses rank first; top-page likes support ties. This is not an exhaustive site census.",
              "items": items}
    return bundle


def render_report(evidence, bundle):
    candidates = evidence["catalogue"]["rows"]
    excluded = evidence["catalogue"]["excluded"]
    usage = {r["search"]: r for r in evidence["usage"]["rows"]}
    items = bundle["items"]
    strata = evidence["discovery"]["strata"]
    sample = {p["url"] for s in strata for p in s["posts"]}
    lines = ["# Common glamour protection list", "", f"Reviewed {evidence['checkedAt']}. The bundle contains **{len(items)} exact game item IDs**.", "",
             "Frequency is the primary signal: at least 25 visible all-time Eorzea Collection outfits using the exact piece. Ranking is uses, then the highest likes, then the sum of likes on the most-loved first results page. Likes never bypass the frequency floor. These likes describe up to 36 outfits, not lifetime total likes.", "",
             f"Discovery inspected {len(sample)} distinct outfits: six recent and six most-loved submissions for each of EC's {len(JOBS)} job filters. Additional seeds come from the original Skallic sets and EC's official [2025 usage census](https://ffxiv.eorzeacollection.com/year-in-review/2025). This is a bounded discovery sample followed by per-item global counts, **not an exhaustive ranking of every item on EC**.", "",
             "Counts are point-in-time public search observations. Full pages contain 36 outfits; uses = (page count − 1) × 36 + final-page outfits. Both result pages, the top-page likes and observation times are saved in [the evidence](data/common-glamour-evidence.json). Deleted/private submissions and later changes can alter counts. EC catalogue IDs are resolved by exact English names against installed game sheets; they are not treated as game item IDs.", "",
             "Level-one glamour equipment, nonmarketable blues, higher rarities, armoire items and identified special rewards are excluded from new candidates because existing rules cover them. Recipe outputs establish crafted sources; EC gearset source labels establish dungeon/special sources. Unknown acquisition is explicitly labelled Unknown and never changes native source classification. Including a popular piece with uncertain acquisition only adds collection protection; it does not permit disposal.", "",
             "Personal native/Glamaholic plates and keep lists are deliberately not used to subtract items from this general list. The existing rules still preserve equipped/gearset items and user overrides. One exact collected copy satisfies collection protection; live ownership and existing surplus safeguards still apply. Absence from this list does not prove an item is disposable. No plugin execution or in-game cleanup was performed to build it.", "",
             "## Slot coverage", "", "Both ring positions share the site's single ring filter. Fashion accessories/facewear outside this inventory equipment workflow are recorded separately. A slot was assessed even when no piece survives the existing protections and frequency floor.", "",
             "| Slot | Candidates assessed | Protected by this list |", "| --- | ---: | ---: |"]
    for slot in SLOTS:
        lines.append(f"| {slot} | {sum(c['search'].split('/')[2] == slot for c in candidates)} | {sum(i['slot'] == slot for i in items)} |")
    lines += ["", "## Job coverage", "", "Each job filter has 12 inspected discovery occurrences. The table below reports original game equipment-job applicability of the selected IDs; it is not a claim that EC outfit intention or current glamour restrictions equal equipment restrictions. BST may be an EC discovery filter without matching installed equipment metadata.", "",
              "| Job | Discovery occurrences | Selected IDs with equipment-job flag |", "| --- | ---: | ---: |"]
    selected_ids = {i["itemId"] for i in items}
    for job in JOBS:
        lines.append(f"| {job.upper()} | {sum(len(s['posts']) for s in strata if s['job'] == job)} | {sum(c['facts']['id'] in selected_ids and job.upper() in c['facts']['jobs'] for c in candidates)} |")
    reasons = Counter(e["excludedReason"] for e in excluded)
    reasons["identified special acquisition"] = sum(c.get("acquisition", {}).get("kind") == "Special" for c in candidates)
    reasons["fewer than 25 visible uses"] = sum(r["uses"] < 25 for r in usage.values())
    lines += ["", "## Exclusions and uncertainty", "", "| Reason | Pieces |", "| --- | ---: |"]
    for reason, count in sorted(reasons.items()):
        lines.append(f"| {reason} | {count} |")
    lines += ["", f"Selected pieces with uncertain acquisition: **{sum(i['acquisition'] == 'Unknown' for i in items)}**. They are explicitly protected by popularity while source knowledge remains uncertain.", "",
              "## Protected items", "", "| Rank | Exact item / EC evidence | Game ID | Slot | Uses | Highest likes | Acquisition |", "| ---: | --- | ---: | --- | ---: | ---: | --- |"]
    for n, item in enumerate(items, 1):
        lines.append(f"| {n} | [{item['name']}]({item['source']}) | {item['itemId']} | {item['slot']} | {item['uses']} | {item['maxLikes']} | {item['acquisition']} |")
    lines += ["", "## Rebuilding and updating", "", "Run `python tools/build_common_glamour.py --check` to validate the frozen evidence and both generated outputs. Omit `--check` to rebuild. This command makes no network calls and never reads player configuration. New observations require a fresh review of public EC pages and exact installed-game metadata. Do not substitute search errors for zero uses, sum duplicate/overlapping results, treat views as uses, or convert Unknown acquisition to Ordinary. The builder requires the original ten Skallic entries and compares every existing bundled ID before writing. It rejects removals; review any future protection removal explicitly.", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    evidence = json.loads(EVIDENCE.read_text(encoding="utf-8"))
    bundle = build(evidence)
    if BUNDLE.exists():
        preserve_existing(json.loads(BUNDLE.read_text(encoding="utf-8")), bundle)
    outputs = {BUNDLE: json.dumps(bundle, ensure_ascii=False, indent=2) + "\n", REPORT: render_report(evidence, bundle)}
    for path, content in outputs.items():
        if args.check:
            require(path.exists() and path.read_text(encoding="utf-8") == content, "Generated output differs: " + str(path))
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
    print(f"Verified {len(bundle['items'])} common glamour items from saved EC frequency evidence.")


if __name__ == "__main__":
    main()
