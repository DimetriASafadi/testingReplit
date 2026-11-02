"""Small CPU-only regressions for the offline camera projection."""
import unittest
from PIL import Image, ImageDraw
from calibrate_ground_camera import measure, reproject


class GroundCameraTest(unittest.TestCase):
    def source(self):
        image = Image.new("RGBA", (200, 180))
        draw = ImageDraw.Draw(image)
        # Ground +/-0.7 slopes; vertical post standing on the floor.
        draw.polygon([(0, 85), (100, 15), (199, 85), (100, 155)], fill=(150, 130, 100, 255))
        draw.rectangle((90, 20, 94, 95), fill=(30, 30, 40, 255))
        return image

    def test_ground_edges_and_anchor_match_the_camera(self):
        calibration = measure(self.source())
        image, meta = reproject(self.source(), calibration, 204)
        after = measure(image)
        self.assertAlmostEqual(after["left"], .5, delta=.025)
        self.assertAlmostEqual(after["right"], -.5, delta=.025)
        self.assertAlmostEqual(after["fy"], meta["ay"]*meta["h"]+meta["ground"]["h"]/2, delta=2)
        self.assertEqual(meta["ground"]["w"], 2*meta["ground"]["h"])

    def test_verticals_stay_vertical_and_reruns_are_deterministic(self):
        calibration = measure(self.source())
        first, meta = reproject(self.source(), calibration, 204)
        second, repeated_meta = reproject(self.source(), calibration, 204)
        self.assertEqual(first.tobytes(), second.tobytes())
        self.assertEqual(meta, repeated_meta)
        # The x mapping has no dependency on y. Dark vertical post remains
        # within the same five columns over its full surviving height.
        pixels = first.load()
        post = [(x, y) for y in range(first.height) for x in range(first.width)
                if pixels[x, y][3] > 240 and max(pixels[x, y][:3]) < 50]
        self.assertTrue(post)
        self.assertLessEqual(max(x for x, _ in post)-min(x for x, _ in post), 5)

    def test_unreliable_edges_fail_instead_of_guessing(self):
        with self.assertRaises(ValueError):
            measure(Image.new("RGBA", (200, 180)))


if __name__ == "__main__":
    unittest.main()
