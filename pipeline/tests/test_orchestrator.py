"""Tests for pipeline/orchestrator.py — reporting layer sanity checks.

These tests ensure the reporting layer never produces impossible values:
- Rejection rate > 100%
- Zero cost with non-zero API calls
- Zero seeded with non-zero stage output
- Fallback count > cards at art stage
- Publish-ready > seeded count
- Negative cost

Also validates the score formula against the DSL spec.
"""

import json
import sys
import pytest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

from orchestrator import (
    _validate_report,
    CostTracker,
    RejectionTracker,
    collect_results,
    PipelineRunner,
)


# ── Fixtures ──────────────────────────────────────────────────────────────────

def make_results(**overrides) -> dict:
    base = {
        "stages": {
            "generate": 43,
            "validate": 34,
            "score": 12,
            "simulate": 12,
            "dedupe_moderate": 12,
            "art": 12,
        },
        "art_fallbacks": 0,
        "commission_queue": 0,
        "publish_ready": 12,
        "total_seeded": 60,
    }
    base.update(overrides)
    return base


def make_rejects(stage: str, total: int, reject_count: int):
    r = RejectionTracker()
    r.processed[stage] = total
    r.rejects[stage] = {f"TEST_FAIL_{i}": 1 for i in range(reject_count)}
    return r


def make_cost(text_tokens: int = 0, image_calls: int = 0,
              text_cost: float = 0.0, image_cost: float = 0.0):
    c = CostTracker()
    c.text_prompt_tokens = text_tokens
    c.text_completion_tokens = text_tokens
    c.text_gen_cost = text_cost
    c.image_calls = image_calls
    c.image_successes = image_calls
    c.image_gen_cost = image_cost
    return c


# ── Impossible value tests ────────────────────────────────────────────────────


class TestImpossibleValues:
    """Each test sets up a single impossible condition and asserts it's caught."""

    def test_rejection_over_100_percent(self):
        results = make_results()
        rejects = make_rejects("validate", total=10, reject_count=15)
        cost = make_cost()
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "validate" in v for v in violations), \
            f"Should flag >100% rejection: {violations}"

    def test_zero_cost_with_image_calls(self):
        results = make_results()
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost(image_calls=12, image_cost=0.0)
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "image calls" in v for v in violations), \
            f"Should flag zero cost with image calls: {violations}"

    def test_zero_cost_with_text_tokens(self):
        results = make_results()
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost(text_tokens=5000, text_cost=0.0)
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "text tokens" in v for v in violations), \
            f"Should flag zero cost with text tokens: {violations}"

    def test_zero_seeded_with_nonzero_output(self):
        results = make_results(total_seeded=0)  # 0 seeded, but stages have output
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost()
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "0 seeded" in v for v in violations), \
            f"Should flag 0 seeded with non-zero output: {violations}"

    def test_fallbacks_exceed_art_count(self):
        results = make_results(art_fallbacks=15)  # 15 fallbacks, but only 12 at art
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost()
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "fallbacks" in v for v in violations), \
            f"Should flag fallbacks > art cards: {violations}"

    def test_publish_ready_exceeds_seeded(self):
        results = make_results(publish_ready=70, total_seeded=60)
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost()
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "publish-ready" in v for v in violations), \
            f"Should flag publish-ready > seeded: {violations}"

    def test_negative_cost(self):
        results = make_results()
        rejects = make_rejects("validate", 10, 0)
        cost = make_cost(text_cost=-0.50)
        violations = _validate_report(results, rejects, cost)
        assert any("IMPOSSIBLE" in v and "negative" in v for v in violations), \
            f"Should flag negative cost: {violations}"

    def test_happy_path_no_violations(self):
        """Normal valid data should produce no violations."""
        results = make_results()
        rejects = make_rejects("validate", 10, 2)
        cost = make_cost(text_tokens=5000, text_cost=0.005, image_calls=12, image_cost=0.60)
        violations = _validate_report(results, rejects, cost)
        assert len(violations) == 0, f"Should have no violations: {violations}"


# ── Score formula reconciliation tests ────────────────────────────────────────


class TestScoreFormulaReconciliation:
    """Verifies the score formula implementation matches the DSL spec §4.

    Spec: expected(cost) = 2.35 * cost + 0.9
    Bands:
      COMMON:   [-0.8, +0.4]
      UNCOMMON: [-0.5, +0.9]
      RARE:     [-0.3, +1.5]
      RELIC:    [ 0.0, +2.5]
    """

    # Import the actual functions from score module
    # (We test them here to document the spec vs implementation contract)

    def test_expected_score_formula(self):
        """expected(cost) must match 2.35*cost + 0.9 exactly."""
        from modules.score import expected_score
        for cost in range(0, 11):
            expected = 2.35 * cost + 0.9
            actual = expected_score(cost)
            assert actual == expected, f"cost={cost}: expected {expected}, got {actual}"

    def test_rarity_bands(self):
        """Rarity bands must match the spec."""
        from modules.score import RARITY_BANDS
        spec_bands = {
            "COMMON": (-0.8, 0.4),
            "UNCOMMON": (-0.5, 0.9),
            "RARE": (-0.3, 1.5),
            "RELIC": (0.0, 2.5),
        }
        assert RARITY_BANDS == spec_bands, \
            f"Code bands {RARITY_BANDS} != spec bands {spec_bands}"

    def test_molten_golem_auto_adjusted(self):
        """Molten Golem reconciliation.

        The LLM reported power_score 12.5, but the score module RECOMPUTES the
        score from stats via the formula. Computed score = 15.6.
          - At cost 7: expected = 17.35, computed delta = -1.75 (outside UNCOMMON
            band [-0.5, +0.9]) → rejected.
          - Auto-adjust to cost 6: expected = 15.0, delta = +0.6 (in band) → passes.
        The published card is cost 6, powered 15.6.
        """
        from modules.score import compute_power_score, expected_score, check_rarity_band

        card = {
            "id": "emb_u_molten_golem", "name": "Molten Golem",
            "strata": "EMBER", "type": "CREATURE", "rarity": "UNCOMMON",
            "cost": 7, "attack": 8, "vigor": 8, "keywords": [],
            "abilities": [{
                "trigger": "ON_ATTACK", "condition": None,
                "effects": [{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE"}, "amount": 2}],
            }],
        }
        # Computed score (from formula), NOT the LLM's self-report of 12.5
        score = compute_power_score(card)
        assert score == 15.6, f"Expected computed score 15.6, got {score}"

        # At cost 7, delta is -1.75 → outside UNCOMMON band → reject
        e7 = expected_score(7)
        delta7 = score - e7
        assert pytest.approx(delta7, abs=0.001) == -1.75, f"Expected delta -1.75 at cost 7, got {delta7}"
        assert check_rarity_band(delta7, "UNCOMMON") is not None, \
            "Should be rejected at cost 7"

        # Auto-adjusted to cost 6 → delta +0.6 → in band → passes
        card6 = dict(card)
        card6["cost"] = 6
        score6 = compute_power_score(card6)
        e6 = expected_score(6)
        delta6 = score6 - e6
        assert pytest.approx(delta6, abs=0.001) == 0.6, f"Expected delta +0.6 at cost 6, got {delta6}"
        assert check_rarity_band(delta6, "UNCOMMON") is None, \
            f"Should pass at cost 6: {check_rarity_band(delta6, 'UNCOMMON')}"

    def test_molten_golem_llm_reported_score_is_not_used(self):
        """The LLM-reported power_score field must NOT drive the band check.

        Molten Golem's JSON has power_score=12.5 (LLM guess). If that field were
        used, delta at cost 7 would be -4.85. But the score module recomputes it
        to 15.6, so the real delta is -1.75. This test documents that the
        pipeline uses the computed score, not the LLM's self-report.
        """
        from modules.score import compute_power_score

        card = {
            "id": "emb_u_molten_golem", "name": "Molten Golem",
            "strata": "EMBER", "type": "CREATURE", "rarity": "UNCOMMON",
            "cost": 7, "attack": 8, "vigor": 8, "keywords": [],
            "abilities": [{
                "trigger": "ON_ATTACK", "condition": None,
                "effects": [{"op": "DAMAGE", "target": {"scope": "ENEMY_CREATURE"}, "amount": 2}],
            }],
            "power_score": 12.5,  # LLM's self-report — should be ignored
        }
        computed = compute_power_score(card)
        assert computed == 15.6, f"Computed {computed}, should be 15.6 (not 12.5)"
        assert computed != card["power_score"], \
            "Score module must recompute, not trust the LLM's power_score field"

    def test_flameguard_witch_in_band(self):
        """Flameguard Witch at cost 4 has delta -0.05, within UNCOMMON band."""
        from modules.score import compute_power_score, expected_score, check_rarity_band

        card = {
            "id": "emb_u_flameguard_witch", "name": "Flameguard Witch",
            "strata": "EMBER", "type": "CREATURE", "rarity": "UNCOMMON",
            "cost": 4, "attack": 3, "vigor": 5, "keywords": ["SWIFT"],
            "abilities": [{
                "trigger": "ON_TURN_START", "condition": None,
                "effects": [{"op": "BUFF", "target": {"scope": "ALLY_CREATURE", "filter": "ANY", "count": 1},
                              "attack": 2, "vigor": 0, "duration": "PERMANENT"}],
            }],
        }
        score = compute_power_score(card)
        e = expected_score(4)
        delta = score - e
        error = check_rarity_band(delta, "UNCOMMON")
        assert error is None, f"Flameguard Witch should pass: delta={delta:.2f}, error={error}"
        # Actually verify the exact delta
        assert abs(delta - (-0.05)) < 0.001, f"Expected delta -0.05, got {delta}"

    def test_expected_score_monotonic(self):
        """expected(cost) should be strictly increasing with cost."""
        from modules.score import expected_score
        for cost in range(1, 10):
            assert expected_score(cost) > expected_score(cost - 1), \
                f"expected({cost}) <= expected({cost-1})"


# ── Cost tracking tests ──────────────────────────────────────────────────────


class TestCostTracking:
    def test_parse_generate_stdout(self):
        cost = CostTracker()
        stdout = (
            "[generate] Starting batch=b_2026_ember_e2e strata=EMBER model=openai/gpt-4o-mini\n"
            "[generate] Attempt 1/2 — requesting 60 cards\n"
            "[generate] Got 26 cards in batch, 26 accepted total\n"
            "[generate] Attempt 2/2 — requesting 34 cards\n"
            "[generate] Got 17 cards in batch, 43 accepted total\n"
            "[generate] Wrote 43 cards to work/b_2026_ember_e2e/01_raw.json\n"
            "[generate] Summary: {'batch_id': 'b_2026_ember_e2e', 'total_generated': 43, 'accepted': 43, 'rejected': 0}\n"
        )
        cost.parse_generate_stdout(stdout)
        # Should estimate 2 batches × 1500 prompt + 2 × 2500 completion
        assert cost.text_prompt_tokens == 2 * 1500, f"Got {cost.text_prompt_tokens}"
        assert cost.text_completion_tokens == 2 * 2500, f"Got {cost.text_completion_tokens}"
        assert cost.text_gen_cost > 0, "Text cost should be non-zero"

    def test_parse_art_stdout(self):
        cost = CostTracker()
        stdout = (
            "[art] Processing 12 cards (model=black-forest-labs/flux.2-pro, skip_api=False)\n"
            "[art] [12/12] Scorched Warrior (EMBER)\n"
            "[art] Summary: {'batch_id': 'b_2026_ember_e2e', 'total': 12, 'api_calls': 12, 'api_failures': 0}\n"
        )
        cost.parse_art_stdout(stdout)
        assert cost.image_calls == 12, f"Got {cost.image_calls}"
        assert cost.image_successes == 12, f"Got {cost.image_successes}"
        assert cost.image_gen_cost == 12 * 0.05, f"Got {cost.image_gen_cost}"
        assert cost.total > 0

    def test_parse_art_stdout_with_failures(self):
        cost = CostTracker()
        stdout = "Summary: {'api_calls': 5, 'api_failures': 2}"
        cost.parse_art_stdout(stdout)
        assert cost.image_calls == 5
        assert cost.image_failures == 2
        assert cost.image_successes == 3
        assert cost.image_gen_cost == 5 * 0.05

    def test_empty_cost_returns_zero(self):
        cost = CostTracker()
        assert cost.total == 0.0
        assert cost.text_gen_cost == 0.0
        assert cost.image_gen_cost == 0.0


# ── Rejection tracker boundary tests ─────────────────────────────────────────


class TestRejectionTracker:
    def test_no_stage_no_rejects(self):
        r = RejectionTracker()
        assert sum(r.rejects.get("nonexistent", {}).values()) == 0

    def test_rejects_scoped_to_stage(self):
        r = RejectionTracker()
        r.note("validate", {"total_processed": 10})
        r.note_reason("validate", "SCHEMA_FAIL: bad id")
        r.note_reason("validate", "SCHEMA_FAIL: bad op")
        r.note_reason("score", "SCORE_FAIL: delta -5.0")
        # Validate should have 2 rejects, score 1
        assert sum(r.rejects.get("validate", {}).values()) == 2
        assert sum(r.rejects.get("score", {}).values()) == 1
        assert "SCHEMA_FAIL" in r.rejects["validate"]
        assert "SCORE_FAIL" in r.rejects["score"]

    def test_zero_processed_no_division_error(self):
        r = RejectionTracker()
        summary = r.summary()
        assert "0.0%" in summary  # Should handle division by zero gracefully


# ── Collect results sanity tests ─────────────────────────────────────────────


class TestCollectResults:
    def test_seeded_count_from_seed_file(self, tmp_path):
        """Seeded count should come from the seed file, not a fallback guess.

        The real seed file (pipeline/seeds/ember_60.json) has count=60.
        The old code reported "Seeded: 0" because it read rejects.processed
        for the generate stage, which was 0.
        """
        # Build a runner pointing at a dummy work dir
        work_dir = tmp_path / "work"
        work_dir.mkdir()

        runner = PipelineRunner(
            work_dir, "EMBER", 60,
            CostTracker(), RejectionTracker(),
        )

        # The seed file exists at pipeline/seeds/ember_60.json with count=60
        seeded = runner.seeded_count()
        assert seeded == 60, f"Expected 60 seeded from seed file, got {seeded}"

    def test_seeded_count_falls_back_to_arg(self, tmp_path, monkeypatch):
        """If no seed file, fall back to the count argument."""
        work_dir = tmp_path / "work"
        work_dir.mkdir()

        runner = PipelineRunner(
            work_dir, "HOLLOW", 42,
            CostTracker(), RejectionTracker(),
        )

        # No hollow_42.json seed exists — should fall back to 42
        seeded = runner.seeded_count()
        assert seeded == 42, f"Expected fallback 42, got {seeded}"

    def test_commission_queue_count(self, tmp_path, monkeypatch):
        """Commission queue count should reflect pending entries in the doc."""
        # Point ROOT at a temp dir with a commission queue file
        from orchestrator import ROOT
        import orchestrator as orch

        fake_root = tmp_path / "root"
        fake_docs = fake_root / "docs"
        fake_docs.mkdir(parents=True)
        (fake_docs / "ART_COMMISSION_QUEUE.md").write_text(
            "# ART COMMISSION QUEUE\n\n- [ ] Card A\n- [x] Card B\n- [ ] Card C\n"
        )

        monkeypatch.setattr(orch, "ROOT", fake_root)

        work_dir = tmp_path / "work"
        work_dir.mkdir()
        runner = PipelineRunner(
            work_dir, "EMBER", 60,
            CostTracker(), RejectionTracker(),
        )

        # Write a 06_art.json so collect_results has something to count
        (work_dir / "06_art.json").write_text(json.dumps([{"art": {"fallback": False}}]))

        results = collect_results(work_dir, runner, runner.cost, runner.rejects)
        # 2 pending (- [ ]), 1 checked off (- [x])
        assert results["commission_queue"] == 2, \
            f"Expected 2 pending, got {results['commission_queue']}"