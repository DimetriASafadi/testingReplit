"""Projection/contact regressions for every agricultural view."""
import json
import unittest
from PIL import Image
from build_canonical_farms import ROOT, OUT, FARMS, FACES, project, rotate
from calibrate_ground_camera import measure


class CanonicalFarmTest(unittest.TestCase):
    def test_ground_directions_match_the_city_camera_at_every_rotation(self):
        for turn in range(4):
            a,b,c=project(-1,-1,turn=turn),project(1,-1,turn=turn),project(1,1,turn=turn)
            self.assertAlmostEqual(abs((b[1]-a[1])/(b[0]-a[0])),.5)
            self.assertAlmostEqual(abs((c[1]-b[1])/(c[0]-b[0])),.5)
            foot,tip=project(.4,.3,turn=turn),project(.4,.3,30,turn)
            self.assertEqual(foot[0],tip[0])
            self.assertEqual(foot[1]-tip[1],30)
        self.assertEqual(rotate(.4,.3,3),(.3,-.4)) # game's right orientation

    def test_every_published_view_has_a_real_ground_contact_and_pixel_aligned_edges(self):
        manifest=json.loads((ROOT/'src/realistic-manifest.json').read_text())
        public=json.loads((OUT/'manifest.json').read_text())
        for stem in FARMS:
            dimensions=[]
            for face in FACES:
                key='farm_cattle' if stem=='cattle' and face=='front' else f'{stem}_{face}'
                with self.subTest(key=key):
                    image=Image.open(OUT/'s'/f'{key}.webp').convert('RGBA')
                    meta=manifest[key]
                    self.assertEqual(meta,public[key])
                    self.assertEqual((meta['w'],meta['h']),image.size)
                    self.assertEqual(meta['canonicalFarm'],1)
                    self.assertEqual(meta['ground'],{'w':256,'h':128})
                    self.assertLessEqual(max(image.size),256)
                    contact=meta['h']*meta['ay']+64
                    self.assertAlmostEqual(contact,image.height-1,delta=1)
                    edge=measure(image)
                    self.assertAlmostEqual(edge['left'],.5,delta=.035)
                    self.assertAlmostEqual(edge['right'],-.5,delta=.035)
                    dimensions.append(meta['ground'])
            self.assertTrue(all(d==dimensions[0] for d in dimensions))

    def test_canopies_are_isolated_not_crops_of_an_entire_farm(self):
        for name in ['olive','citrus','orn']:
            im=Image.open(OUT/'canopies'/f'{name}.webp').convert('RGBA')
            self.assertEqual(im.size,(64,64))
            self.assertEqual(im.getpixel((0,0))[3],0)
            self.assertGreater(im.getchannel('A').getextrema()[1],240)


if __name__=='__main__':
    unittest.main()
