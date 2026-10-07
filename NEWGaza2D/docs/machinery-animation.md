# Articulated 2D machinery and living construction sites

The game remains Phaser 2D. Blender is used **offline**, on CPU, to render transparent
sprites; players do not download or render 3D models.

- Unbranded yellow excavator and tracked bulldozer; ivory/green ribbed tipper.
- Three alpha WebP atlases, 276 poses: 16 headings with rolling phases, 72 excavator
  work poses, 36 blade-work poses, 36 actual tail-hinge tipping poses, and four
  parked load amounts. Packed art is about 2 MiB; every atlas stays below 4096px.
- Shared orthographic camera, upper-left lighting, matte paint and the city’s
  restrained colour grade. Contact shadows are baked without opaque floor pads.
- Excavator upper carriage, boom, stick, bucket and hydraulic ram articulate
  independently. The tracks stay planted during digging. Loading particles emit
  from the rendered bucket lip; the tipper discharges at its rendered rear outlet.
- Curb positions keep chassis off the ruined building footprint. Trucks can back
  into loading positions. Adjacent heading/pose interpolation avoids snap turns.
- Moving chassis sort by their rear ground-contact envelope rather than their
  centre. Dimensions, current heading, atlas projection and rendered scale determine
  that envelope on every pose; crossfaded headings share a continuous envelope.
  A turning tail therefore goes behind a facade immediately, instead of painting
  over its roof until the vehicle centre has travelled past the sorting boundary.
- Foreground buildings temporarily fade only when their opaque pixels hide an
  active machine or tool. Depth order and real road positions remain unchanged.
- The city uses the original 2:1 projection and logical plot positions. Houses,
  farms, ruins and construction share a smaller, uniform calibrated art scale,
  with a consistent maximum ruin/building height and wider rendered asphalt.
  Machinery is 10% more legible than the first atlas placement; its excavator/
  truck docking separation changed in step, preserving the actual bucket-to-bed
  contact. No saved camera or plot coordinates are rewritten.
- Eight construction stages use the chosen building and front/rear orientation.
  Columns, slabs, formwork and scaffolds precede finished surfaces; grounded workers
  carry materials and lay them. Multi-storey cranes lift, swing and lower slung pallets.
- Farms use planted rows, species-specific ripening, irrigation, harvesting and
  livestock motion. Tree wind uses fragments of the actual farm artwork, not
  unrelated generic canopy stickers.

The recycling factory has a grounded lift-up metal shutter in both building orientations.
Purchased but idle units stay inside it rather than remaining on the street. A job
opens the shutter, sends the three units out one at a time along the same road with
arc-distance separation, and closes it once the last unit clears the apron. On return,
the same team approaches in order, the shutter opens before the first unit reaches it,
and each machine disappears as it crosses into the factory; the shutter closes last.
The shutter and vehicles recover their positions after a reload from saved timestamps.
Simultaneous crews reserve the shared depot approach so they do not occupy its entry
together. The job display distinguishes waiting to leave or return from actual travel.
Existing jobs without these optional timestamps continue to load with their original
deadlines. Clearance is still sixty seconds; purchase prices, income, inventory
settlement and district progression are unchanged. No idle street parking or
post-settlement street tipping remains.

## Rebuild

From repository root:

```sh
blender -b --factory-startup --python NEWGaza2D/tools/render_machinery.py
python NEWGaza2D/tools/pack_machinery.py
node NEWGaza2D/tests/machinery-integrity.mjs
node NEWGaza2D/tests/run-construction.mjs
```

The user-supplied separated Meshy excavator in the existing `exports/meshy_excavator`
folder is read-only input. Truck/dozer rigs are authored in the rendering script.
Raw renders live in the ignored `NEWGaza2D/art-src` folder; packed runtime files and
pose metadata are versioned. No Unity `.meta` files are generated.
