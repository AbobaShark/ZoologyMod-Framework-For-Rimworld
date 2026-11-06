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
        expected=[.658,1.397,1.626,1.634,1.636]
        self.assertEqual([age_speed_factor(size,5.0)
                          for size in (.2,4,13,15,20)],expected)
        stages=parse_stages(fixture(),ROYAL_SOURCE)
        self.assertEqual([value['SpeedFactor'] for _,value in stages],expected)
        self.assertEqual([value['BaseBodyMass_kg'] for _,value in stages],[5.0]*5)

    def test_reference_mass_controls_where_growth_starts_slowing_down(self):
        # The fictional reference mass is not an empirical observation:
        # changing it changes the age at which the speed maximum occurs.
        self.assertGreater(age_speed_factor(20,5),age_speed_factor(15,5))
        self.assertLess(age_speed_factor(20,10),age_speed_factor(15,10))
        self.assertLess(age_speed_factor(20,20),age_speed_factor(15,20))

    def test_checker_rejects_manually_inconsistent_speed_or_mass(self):
        table=fixture()
        table.loc[8,'SpeedFactor']=1.99
        with self.assertRaisesRegex(ValueError,'Hirt running allometry'):
            parse_stages(table)
        table=fixture()
        table.loc[5,'BaseBodyMass_kg']=7
        with self.assertRaisesRegex(ValueError,'share one BaseBodyMass_kg'):
            parse_stages(table)
        table=fixture()
        table.loc[4,'BaseBodyMass_kg']='No'
        with self.assertRaisesRegex(ValueError,'BaseBodyMass_kg'):
            parse_stages(table)
        table=fixture().drop(columns=['BaseBodyMass_kg'])
        with self.assertRaisesRegex(ValueError,'missing columns'):
            parse_stages(table)

    def test_core_factors_are_not_mislabeled_as_species_specific(self):
        # Core factors are shared among animals with many adult masses.
        # Checker must not silently apply a fictitious reference mass to them.
        stages=parse_stages(fixture(),'Core')
        self.assertEqual([value['SpeedFactor'] for _,value in stages],[1,.9,.75,.7])
        self.assertTrue(all('BaseBodyMass_kg' not in value for _,value in stages))

    def test_invalid_mass_is_rejected(self):
        for value in (float('nan'),float('inf'),-1,0):
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    maximum_running_speed_kmh(value)
        with self.assertRaises(ValueError):
            age_speed_factor(0,5)


if __name__ == '__main__':
    unittest.main()
