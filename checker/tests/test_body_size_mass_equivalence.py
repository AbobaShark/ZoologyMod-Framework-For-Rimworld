"""Regression anchors from AnimalStats!SERVICE_BodySizeMassBands (Nov 2026)."""
import unittest
from core.body_size_mass_equivalence import (
    body_size_equivalent_mass_kg, body_size_midpoint_pairs,
    mass_ranges_for_body_size,
)


class BodySizeMassEquivalenceTests(unittest.TestCase):
    def test_actual_animalstats_mass_band_midpoints(self):
        anchors = {
            .05: .10234922857917791,
            .2: 1.6915903616345416,
            .35: 5.578330083476266,
            1.0: 62.002305471872916,
            1.4: 139.38731994816675,
            7.0: 11630.032807639442,
        }
        for size, expected in anchors.items():
            with self.subTest(size=size):
                self.assertAlmostEqual(body_size_equivalent_mass_kg(size), expected,
                                       delta=max(1e-7, expected * 1e-9))

    def test_unattainable_sizes_interpolate_neighboring_bins(self):
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.07),
                               .22393282346936133, places=9)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(4.55),
                               3464.5139746102586, places=6)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(5.25),
                               5294.457899341969, places=6)

    def test_upper_royal_stages_are_not_linear_body_size_times_mass(self):
        self.assertAlmostEqual(body_size_equivalent_mass_kg(6),
                               7545.509632321199, places=5)
        self.assertGreater(body_size_equivalent_mass_kg(7), 10000)

    def test_below_defined_minimum_is_never_extrapolated(self):
        for size in (.01, .049, 0, -1):
            with self.subTest(size=size), self.assertRaises(ValueError):
                body_size_equivalent_mass_kg(size)
        with self.assertRaises(ValueError):
            body_size_equivalent_mass_kg(21)

    def test_rounded_band_intervals_are_monotonic_and_nonempty(self):
        pairs = body_size_midpoint_pairs()
        self.assertGreater(len(pairs), 50)
        for left, right in zip(pairs, pairs[1:]):
            self.assertLess(left[0], right[0])
            self.assertLess(left[1], right[1])
        ranges = mass_ranges_for_body_size(.05)
        self.assertEqual(len(ranges), 1)
        self.assertEqual(ranges[0][0], 0.0)


if __name__ == "__main__":
    unittest.main()
