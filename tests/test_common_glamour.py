import copy
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("common_glamour", ROOT / "tools/build_common_glamour.py")
curation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(curation)


class CommonGlamourEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.evidence = json.loads(curation.EVIDENCE.read_text(encoding="utf-8"))
        cls.bundle = curation.build(cls.evidence)

    def test_frozen_outputs_match_reviewed_evidence(self):
        self.assertEqual(json.loads(curation.BUNDLE.read_text(encoding="utf-8")), self.bundle)
        self.assertEqual(curation.REPORT.read_text(encoding="utf-8"), curation.render_report(self.evidence, self.bundle))

    def test_frequency_always_beats_likes(self):
        frequent = {"uses": 100, "maxLikes": 0, "topPageLikesTotal": 0, "itemId": 2}
        liked = {"uses": 99, "maxLikes": 1000000, "topPageLikesTotal": 1000000, "itemId": 1}
        self.assertLess(curation.rank(frequent), curation.rank(liked))

    def test_very_high_likes_do_not_bypass_frequency_floor(self):
        evidence = copy.deepcopy(self.evidence)
        row = next(r for r in evidence["usage"]["rows"] if r["uses"] < 25)
        row["topPageLikes"][0] = 1000000
        self.assertNotIn(row["itemId"], {i["itemId"] for i in curation.build(evidence)["items"]})

    def test_page_count_is_not_rounded_up_to_a_full_last_page(self):
        evidence = copy.deepcopy(self.evidence)
        row = next(r for r in evidence["usage"]["rows"] if r["lastPageCount"] < 36)
        row["uses"] = row["pages"] * 36
        with self.assertRaisesRegex(ValueError, "observed pagination"):
            curation.build(evidence)

    def test_incomplete_measurement_cannot_become_zero(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["usage"]["rows"][0]["lastPageCount"] = 0
        with self.assertRaisesRegex(ValueError, "final-page evidence"):
            curation.build(evidence)

    def test_early_loading_guard_records_require_rechecking(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["usage"]["rows"][0].pop("measurementVersion")
        with self.assertRaisesRegex(ValueError, "complete-page verification"):
            curation.build(evidence)

    def test_exact_item_ids_are_verified_against_game_metadata(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["usage"]["rows"][0]["itemId"] += 1
        with self.assertRaisesRegex(ValueError, "game IDs"):
            curation.build(evidence)

    def test_filtered_scope_is_not_called_global_frequency(self):
        evidence = copy.deepcopy(self.evidence)
        row = evidence["usage"]["rows"][0]
        row["firstPage"] = row["firstPage"].replace("filter%5Bjob%5D=all", "filter%5Bjob%5D=pld")
        with self.assertRaisesRegex(ValueError, "all-time/all-jobs/exact-item"):
            curation.build(evidence)

    def test_all_job_strata_must_be_present(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["discovery"]["strata"].pop()
        with self.assertRaisesRegex(ValueError, "job strata"):
            curation.build(evidence)

    def test_uninspected_sample_is_rejected(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["discovery"]["strata"][0]["posts"][0]["url"] = "/glamour/not-inspected"
        with self.assertRaisesRegex(ValueError, "not inspected"):
            curation.build(evidence)

    def test_known_special_reward_is_excluded(self):
        evidence = copy.deepcopy(self.evidence)
        item = next(i for i in self.bundle["items"] if i["itemId"] not in curation.PREVIOUS_IDS)
        candidate = next(c for c in evidence["catalogue"]["rows"] if c["facts"]["id"] == item["itemId"])
        candidate["acquisition"] = {"kind": "Special"}
        self.assertNotIn(item["itemId"], {i["itemId"] for i in curation.build(evidence)["items"]})

    def test_unknown_acquisition_is_not_promoted_to_ordinary(self):
        before = copy.deepcopy(self.evidence)
        bundle = curation.build(self.evidence)
        unknown = {c["facts"]["id"] for c in self.evidence["catalogue"]["rows"] if not c.get("acquisition")}
        self.assertTrue(all(i["acquisition"] == "Unknown" for i in bundle["items"] if i["itemId"] in unknown))
        self.assertEqual(before, self.evidence)

    def test_duplicate_game_ids_are_rejected(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["catalogue"]["rows"].append(copy.deepcopy(evidence["catalogue"]["rows"][0]))
        with self.assertRaisesRegex(ValueError, "Duplicate game item IDs"):
            curation.build(evidence)

    def test_unmarketable_blue_cannot_enter_incremental_candidates(self):
        evidence = copy.deepcopy(self.evidence)
        facts = evidence["catalogue"]["rows"][0]["facts"]
        facts.update(rarity=3, marketable=False)
        with self.assertRaisesRegex(ValueError, "Rarity-protected candidate"):
            curation.build(evidence)

    def test_existing_skallic_protection_cannot_silently_disappear(self):
        evidence = copy.deepcopy(self.evidence)
        evidence["catalogue"]["rows"] = [c for c in evidence["catalogue"]["rows"] if c["facts"]["id"] != 20819]
        with self.assertRaisesRegex(ValueError, "lose protection"):
            curation.build(evidence)

    def test_future_rebuild_cannot_silently_remove_any_existing_entry(self):
        replacement = copy.deepcopy(self.bundle)
        replacement["items"].pop()
        with self.assertRaisesRegex(ValueError, "Previously bundled protection"):
            curation.preserve_existing(self.bundle, replacement)

    def test_eligible_official_census_leader_cannot_be_missed(self):
        evidence = copy.deepcopy(self.evidence)
        seed = next(a for a in evidence["census"]["assessments"] if a["decision"] == "candidate")
        evidence["catalogue"]["rows"] = [c for c in evidence["catalogue"]["rows"] if c["facts"]["id"] != seed["facts"]["id"]]
        with self.assertRaisesRegex(ValueError, "census seed was missed"):
            curation.build(evidence)


if __name__ == "__main__":
    unittest.main()
