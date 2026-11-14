import unittest

from rimworld_xml_generator import generate_litter_curve


class LitterCurveTests(unittest.TestCase):
    def test_single_birth_is_explicit(self):
        self.assertEqual(generate_litter_curve(1), [(0.5, 0), (1, 1), (1.5, 0)])

    def test_half_integer_means_have_symmetric_positive_support(self):
        for mean in (1.5, 2, 2.5, 3, 4.5, 10):
            with self.subTest(mean=mean):
                points = generate_litter_curve(mean)
                self.assertGreaterEqual(points[0][0], 0.5)
                for (x1, y1), (x2, y2) in zip(points, reversed(points)):
                    self.assertAlmostEqual(x1 + x2, 2 * mean)
                    self.assertEqual(y1, y2)

    def test_no_litter_has_no_curve(self):
        self.assertIsNone(generate_litter_curve(None))


if __name__ == "__main__":
    unittest.main()
