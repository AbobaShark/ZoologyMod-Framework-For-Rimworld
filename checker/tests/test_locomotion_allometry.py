"""Independent checks for the empirical Hirt et al. terrestrial speed curve.

Running v(M) peaks at an intermediate body mass, unlike M**0.17.
The reference mass for fictional Megachicken is an explicit model input.
"""
import math
import unittest

from core.locomotion_allometry import (
    age_speed_factor,
    maximum_running_speed_kmh,
)
from core.body_size_mass_equivalence import (
    body_size_equivalent_mass_kg, mass_ranges_for_body_size,
    body_size_for_mass_kg, body_size_midpoint_pairs,
)
from core.life_stages import ROYAL_SOURCE, ROYAL_STAGES, parse_stages
from tests.test_life_stages import fixture


class RunningAllometryTests(unittest.TestCase):
    def test_hirt_running_curve_has_intermediate_optimum(self):
        v=maximum_running_speed_kmh
        self.assertLess(v(1),v(5))
        self.assertLess(v(5),v(90))
        self.assertGreater(v(90),v(200))
        self.assertGreater(v(200),v(1000))
        self.assertAlmostEqual(v(90),63.43,places=2)

    def test_royal_factors_match_the_hirt_equation(self):
        expected=[.434,1.569,.812,.716,.561]
        self.assertEqual([age_speed_factor(size,.35)
                          for size in (.2,4,13,15,20)],expected)
        stages=parse_stages(fixture(),ROYAL_SOURCE)
        self.assertEqual([value['SpeedFactor'] for _,value in stages],expected)
        self.assertEqual([value['BaseBodySize'] for _,value in stages],[.35]*5)

    def test_smooth_forward_map_has_expected_anchors(self):
        for mass, expected in ((.268115,.1), (5,.35), (60,1), (187.5,1.6),
                               (221.8,1.7), (316.9,1.9), (4500,5)):
            self.assertAlmostEqual(body_size_for_mass_kg(mass),expected,places=7)
        masses=[.01*1.01**i for i in range(1600)]
        sizes=[body_size_for_mass_kg(mass) for mass in masses]
        self.assertTrue(all(a<=b for a,b in zip(sizes,sizes[1:])))

    def test_body_size_quantization_maps_large_royal_to_tonnes(self):
        self.assertEqual(len(body_size_midpoint_pairs()),72)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.35),5.5783300835,places=4)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.35*4),139.3873199482,places=3)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.35*13),3464.5139746103,places=3)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.35*15),5294.4578993420,places=3)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.35*20),11630.0328076394,places=3)
        self.assertEqual(mass_ranges_for_body_size(3.9),())
        self.assertEqual(len(mass_ranges_for_body_size(4.0)),1)
        self.assertTrue(all(len(mass_ranges_for_body_size(s))==1
                            for s,_ in body_size_midpoint_pairs()))

    def test_largest_royal_is_slower_after_mass_bin_inversion(self):
        self.assertLess(age_speed_factor(20,.35),age_speed_factor(15,.35))
        self.assertLess(age_speed_factor(15,.35),age_speed_factor(13,.35))
        self.assertNotAlmostEqual(age_speed_factor(20,.35),
                                  maximum_running_speed_kmh(100)/maximum_running_speed_kmh(5),
                                  places=2)

    def test_checker_rejects_manually_inconsistent_speed_or_mass(self):
        table=fixture()
        table.loc[8,'SpeedFactor']=1.99
        with self.assertRaisesRegex(ValueError,'Hirt running allometry'):
            parse_stages(table)
        table=fixture()
        table.loc[5,'BaseBodySize']=.7
        with self.assertRaisesRegex(ValueError,'share one BaseBodySize'):
            parse_stages(table)
        table=fixture()
        table.loc[4,'BaseBodySize']='No'
        with self.assertRaisesRegex(ValueError,'BaseBodySize'):
            parse_stages(table)
        table=fixture().drop(columns=['BaseBodySize'])
        with self.assertRaisesRegex(ValueError,'missing columns'):
            parse_stages(table)

    def test_core_factors_are_not_mislabeled_as_species_specific(self):
        # Core factors are shared among animals with many adult masses.
        # Checker must not silently apply a fictitious reference mass to them.
        stages=parse_stages(fixture(),'Core')
        self.assertEqual([value['SpeedFactor'] for _,value in stages],[1,.9,.75,.7])
        self.assertTrue(all('BaseBodySize' not in value for _,value in stages))

    def test_invalid_mass_is_rejected(self):
        for value in (float('nan'),float('inf'),-1,0):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    maximum_running_speed_kmh(value)
        with self.assertRaises(ValueError):
            age_speed_factor(0,5)


if __name__ == '__main__':
    unittest.main()
