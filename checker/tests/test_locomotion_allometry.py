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
        expected=[.370,1.658,.995,.855,.670]
        self.assertEqual([age_speed_factor(size,.3)
                          for size in (.2,4,13,15,20)],expected)
        stages=parse_stages(fixture(),ROYAL_SOURCE)
        self.assertEqual([value['SpeedFactor'] for _,value in stages],expected)
        self.assertEqual([value['BaseBodySize'] for _,value in stages],[.3]*5)

    def test_body_size_quantization_maps_large_royal_to_tonnes(self):
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.3),4.747281,places=4)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.3*4),98.010198,places=4)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.3*13),1954.978512,places=3)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.3*15),3356.519895,places=3)
        self.assertAlmostEqual(body_size_equivalent_mass_kg(.3*20),7531.794925,places=3)
        self.assertEqual(mass_ranges_for_body_size(3.9),())
        self.assertEqual(len(mass_ranges_for_body_size(4.0)),2)

    def test_largest_royal_is_slower_after_mass_bin_inversion(self):
        self.assertLess(age_speed_factor(20,.3),age_speed_factor(15,.3))
        self.assertLess(age_speed_factor(15,.3),age_speed_factor(13,.3))
        self.assertNotAlmostEqual(age_speed_factor(20,.3),
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
