# Hands-on calibration tutorial: Verbatim AZO DVD-R

*English translation of [CALIBRATION_TUTORIAL.md](CALIBRATION_TUTORIAL.md). Button names match the English UI. In this fork, the calibration guide banner walks you through the same steps, the rim/hole points start on the detected edges, and **Snap to lines** speeds up tracing; see [CALIBRATION.en.md](CALIBRATION.en.md#what-this-fork-adds).*

This tutorial follows a real test using the **Verbatim AZO DVD-R preset** through "rough size → refine edges → straighten lines" (rounds 1–3). It shows how the points were actually picked and what a disc burned with the fine-calibrated parameters looked like. All three rounds come with photos, measurements and records you can open; the photo after fine calibration is `P20261104-203512.jpg`.

Every round uses a new blank disc, and keeps **the generation record, the disc actually burned, and the photo of that disc** together. Only the next disc uses the new parameters after you apply a result; an older disc must still be analysed with the record it was generated from. The full feature description is in the [calibration guide](CALIBRATION.en.md).

## Learn photo rectification first: the physical rim and the centre hole

All three rounds start by rectifying the photo before measuring bands or tracing lines. If you already have a calibration disc, follow the steps below; if not, read how to frame the photo and where the points go, then generate your first target as described later.

1. Photograph **the disc's complete physical outer edge and the centre hole**, with the pattern you're measuring as clear as possible. If glare covers part of the pattern, change the lighting and reshoot rather than cropping the rim.
2. On the calibration page, open **the record this disc was generated with**, then import its photo. This example uses the **12 cm** physical size, i.e. a physical outer radius of **60 mm**; don't enter the roughly **58 mm** outer radius of the recorded area.
3. Drag the outer control points onto **the disc's physical outermost edge** and the inner points onto the **edge of the centre hole**. Zoom in on the hole edge: the clear plastic ring, the edge of the recorded area, bright reflection rings and shadows can't stand in for these two physical boundaries. Scroll to zoom; right-drag to pan.
4. Click **Calculate rectification** and check the rim and centre hole in the straightened preview. If something's off, go back, fix the points and calculate again; once it checks out, confirm the rectification.
5. Place all later band and curve points on the **rectified photo**. After changing the physical size or the boundary points, rectify and measure again.

<img src="images/verbatim-azo-20261104/00-photo-points.png" alt="Control points rebuilt on the original photo from the round 1 record: eight on the physical rim, eight on the centre-hole edge, with the hole enlarged" width="900">

This figure is rebuilt from this example's saved record: **cyan** marks the 8 control points on the physical outer edge and **orange** the 8 on the centre-hole edge; the enlargement shows which ring to follow. It explains where the points go; it is not an app screenshot.

<img src="images/verbatim-azo-20261104/00-rectified.png" alt="Straightened disc preview rebuilt with the same control points and the app's rectification model" width="640">

This is the rectified preview rebuilt from the record. The **cyan reference circle** is the physical outer edge and the **orange reference circle** the centre hole. The physical size restores the millimetre scale. Rectification does not remove the real curvature of the burn and does not separately correct lens distortion. Don't move the rim points to make the cross look straight.

## Example files

Download the main-line JSON files below and load them on the calibration page with **Open calibration session**. All three embed the photo, outline control points and measurements, so you can inspect them or solve again directly. An empty top-level `observations` doesn't mean nothing was measured; photo measurements are stored under `photo`. A supplementary case that repeats round 1 before round 2 follows later.

| Order | Record (click to download) | Target | Saved measurements |
| --- | --- | --- | --- |
| 1 | [4f0228e6](examples/verbatim-azo-20261104/calibration-4f0228e6.calibration.json) | Round 1 `AngleSweepBands` | Confirmed rectification; six edge points on three wide bands |
| 2 | [3f2ce8c2](examples/verbatim-azo-20261104/calibration-3f2ce8c2.calibration.json) | Round 2 `WavyBoundary` | Confirmed rectification; 13 boundary segments, 154 points |
| 3 | [14580aac](examples/verbatim-azo-20261104/calibration-14580aac.calibration.json) | Round 3 `ReferenceCross` | Confirmed rectification; 33 points on one thin arm |

Each round's point figure is rebuilt from the record's photo, rectification and measured points; none are app screenshots. Lines only show the click order, not the solver's prediction. The original photos are kept separately: [round 1 photo](images/verbatim-azo-20261104/01-coarse.png), [round 2 photo](images/verbatim-azo-20261104/02-boundary.png), [round 3 photo](images/verbatim-azo-20261104/03-fine.png). Part of the round 3 disc was scored with a craft knife to make it easier to photograph; see the comparison caption below.

## Before you start: choose the preset and check this run's starting values

1. Open **Calibration** from the home page or the project sidebar and choose **DVD** and the **Verbatim AZO DVD-R** preset matching your discs. With a project open, calibration settings are separate from the design project.
2. Insert a blank and check the sector count with **Read from disc**. In this example every round and the final disc are **2,297,888 sectors**, with a DVD `linearDensity` of **133.3 nm**. For your own discs, go by what the drive actually reports.
3. This example's first round was generated with radii of **24.1 / 57.8 mm**. That differs from the current built-in AZO DVD-R preset (about **23.991461 / 57.964894 mm**); only change to the first pair if you want to reproduce this run's starting point.
4. Keep the same batch of discs, the same recorder and the same write settings, and save each round's record. The example didn't record the burner model, speed or exact disc SKU, and they can't be inferred from the JSON.

All three main-line records keep the built-in AZO DVD-R preset's three non-zero track-pitch coefficients. Round 1's band pattern is actually generated and estimated with a constant pitch; applying its result only updates the radii and keeps the original coefficients. Round 2 keeps them too; only round 3 fits them again. Don't treat the coefficients in a round 1 record as something that round measured.

## Before each round: generate the matching calibration disc

Repeat this for every new calibration disc.

1. Choose the round and click **Create target from parameters**. This fixes the disc's generation parameters; switching rounds by itself never changes a target you already generated.
2. Generate the DVD `.iso` from the calibration page and burn it, or use the calibration page's streaming burn. Keep the matching `.calibration.json`. Don't substitute a preview PNG imported into an ordinary design project for a calibration burn.
3. Photograph, import and confirm the rectification as described at the top, then measure for this round.
4. After each round, click **Save calibration session** before creating the next target, so the previous disc's photo and points aren't replaced by a new draft.

## Round 1 · Rough size: get radii close to the real disc

This round's record: [4f0228e6](examples/verbatim-azo-20261104/calibration-4f0228e6.calibration.json). The pattern is three wide bands with angle marks inside them.

<img src="images/verbatim-azo-20261104/01-coarse-points.png" alt="Rectified round 1 photo with the six real band-edge points, ordered 1 inner, 1 outer, 2 inner, 2 outer, 3 inner, 3 outer" width="800">

The six points come from this record and lie along one radius; **1–6** correspond to **1 inner, 1 outer, 2 inner, 2 outer, 3 inner, 3 outer** in the app. Look at where each whole band meets the blank area; don't mistake the angle-mark stripes inside a band for its edge.

1. On the rectified photo, pick a direction on one side that is clear from inside to outside.
2. Following the prompts, click **1 inner, 1 outer, 2 inner, 2 outer, 3 inner, 3 outer**: six boundaries. Measure the edges of whole bands and leave the stripes inside them to the app.
3. Click **Estimate radii and read angle marks** and look at the band estimate and the angle-corrected result. When the three angle marks pass their check, the next target uses the angle-corrected values; if reading fails, check your points or reshoot first. The app may then only show the band estimate.
4. Apply the values for the next target, save this round's record, switch to **Round 2 · Refine edges** and **create a target**.

The band-only radius estimate in this round was **24.016692871476085 / 57.70924224450089 mm**; after reading the three angle marks it was corrected to **23.99575834805153 / 57.97592653885557 mm**. The round 2 disc used these angle-corrected values, so when you use a result, keep "band estimate" and "angle-corrected" apart.

Reopening the first record still shows its original **24.1 / 57.8 mm**. That's expected.

### If one round of rough sizing isn't enough, repeat it

If one round doesn't give starting values good enough for round 2, check your points and photo first. If you still need to refine, generate a new round 1 disc from the result:

1. Apply this round's correction and save the current record.
2. Keep **Round 1 · Rough size** selected and click **Create target from parameters**, so the new pattern uses the values you just applied.
3. Burn it on a new blank, photograph and rectify it, then measure the six band edges on the new disc's photo and estimate again.
4. Repeat as needed; once the starting values are good, apply the result and create a **Round 2 · Refine edges** target.

Clicking "estimate" again on the same photo only recalculates the same measurements; it isn't the disc-by-disc iteration above. The supplementary case below used three rounds; that's not a rule, and it doesn't mean every disc needs exactly three.

## Round 2 · Refine edges: keep correcting the inner and outer radius

This round's record: [3f2ce8c2](examples/verbatim-azo-20261104/calibration-3f2ce8c2.calibration.json). This disc uses the new radii from the previous step and keeps the original pitch coefficients.

<img src="images/verbatim-azo-20261104/02-boundary-points.png" alt="Rectified round 2 photo with thirteen traced boundary segments, 154 points in total" width="800">

The figure keeps this example's **13 segments and 154 points** with their segments and order; **colours only tell segments apart and dashed lines connect each segment's points in click order**; they are not a fitted prediction. Skip anything hidden by glare and move to another clear light/dark boundary.

1. Click along a clear **light/dark boundary**, not the centre of a light or dark band. Trace one segment while it stays visible; where glare hides it, end the segment and start a new one at another clear boundary. (With **Snap to lines** on, one click traces along the edge until it fades or meets another line.)
2. Points should be spread over different radii and follow the boundary's local direction. This example saved **13 segments, 154 points**; that's how this run did it, not a number to copy.
3. Click **Solve parameters**. This example's record saved a range of **±0.02 mm** around the starting values with automatic widening off; the app now defaults new round 2 targets to **±0.01 mm**, and opening the example restores its saved setting.
4. Check that the predicted boundaries hug the visible boundaries, traced or not. If the best value sits at the edge of the search, candidates are ambiguous, or coverage is short, check the starting values, add segments or adjust the range and solve again.
5. Once the prediction is shown, tick that the outline matches and apply the current candidate. Save the record, switch to **Round 3 · Straighten lines** and **create a target**.

The third record's generation radii are **23.990847220268247 / 57.96535646048506 mm**, about **4.911 / 10.570 μm** smaller than this disc's. The three pitch coefficients stay the same.

### Supplementary case: round 2 after three rough-size rounds

Record: [calibration-2ba20083_2.calibration.json](examples/verbatim-azo-20261104/calibration-2ba20083_2.calibration.json). This is a separate test from the same starting parameters that ran three rough-size rounds in a row before generating a round 2 disc. Its boundary points are shown here as a reference for when you need several rough-size rounds.

This round 2 disc was generated with radii of **23.9966141727963 / 57.96012005643783 mm**, a capacity of **2,297,888 sectors** and a bit length of **133.3 nm**, still using pitch set A below. The saved search range is **±0.02 mm** with automatic widening off.

<img src="images/verbatim-azo-20261104/02-boundary-after-three-coarse-points.png" alt="Round 2 disc generated after three rough-size rounds: rectified photo and real boundary points rebuilt from the record" width="800">

The figure rebuilds **17 segments, 169 points** from the supplementary record; colours only tell segments apart and dashed lines show click order, not a fitted prediction. Open the record to see how the clear boundaries were segmented, then solve again following this section.

Several rough-size rounds can refine the starting values step by step. In round 2 you still need to check the prediction, the points and the effect of glare, and decide from the actual result whether to add points or keep adjusting. Three was just how many this attempt used.

## Round 3 · Straighten lines: fit the radii and the pitch shape together

This round's record: [14580aac](examples/verbatim-azo-20261104/calibration-14580aac.calibration.json). The cross is still clearly curved on this disc (see the left photo in the next section); this round is exactly about tracing the real curved arm.

<img src="images/verbatim-azo-20261104/03-fine-points.png" alt="Rectified round 3 photo with thirty-three points along the centre line of one curved thin cross arm" width="800">

These are the **33 points** actually saved in this example; the line only shows their order, and **1, 6, 12, 18, 24, 33** are point numbers. Keep following the same arm's centre line outward, including where it crosses the concentric circles. The location and limits of the hand-enhanced part of the original photo are in the caption in the next section.

1. On the rectified photo, choose one clear thin arm and click evenly along its **centre line** from inside to outside, always staying on the same arm. (With **Snap to lines** on, each click lands on the line's centre.) The circles help you find your place; you don't need to click their crossings.
2. Tick **Fit cubic track pitch**. It needs at least **12 points at different radii** spanning **60%** of the generated band width; with fewer points or less coverage only the radii are fitted and the old coefficients kept. This example has **33 points** covering nearly the whole drawing area.
3. Click **Solve parameters**. This example's initial search range is **±0.02 mm** with automatic widening on. Add points along the real curve so no two neighbours are nearly half a turn apart, which would make the direction ambiguous.
4. Check that the predicted line follows the whole visible arm. Go back to the photo for any points shown in red; solve again after moving points.
5. Tick that the outline matches and apply the candidate. The inner/outer radius and three pitch coefficients it outputs are **a complete new parameter set**; don't add the new coefficients to the old ones by hand.
6. **Apply first, then Save as preset**, with a name you'll recognise. Choose this new preset back in your design project; only then does the project use these parameters.

"Save calibration session" keeps the original generation settings, photo, points and search settings; it does not keep solved candidates, errors or the applied state. "Save as preset" saves the current settings; the two do different jobs. After reopening an old record, solve and apply again before you can save that round's fit again.

## The result after round 3, and how to continue

After applying the round 3 result, **create a target** again in **Round 3 · Straighten lines** and burn a new disc. The right-hand photo below shows a disc burned with these new parameters; the original photo is [P20261104-203512.jpg](images/verbatim-azo-20261104/04-verification.jpg).

| Before: the cross disc used for tracing and solving | After: the disc burned with the new parameters |
| --- | --- |
| ![Before the fine fit the cross lines are clearly curved; the lower left was scored with a craft knife to photograph better](images/verbatim-azo-20261104/03-fine.png) | ![On the final disc the visible cross arms run closer to straight radial lines](images/verbatim-azo-20261104/04-verification.jpg) |
| `14580aac` embedded photo | `P20261104-203512.jpg` |

**Photo note: the twisted cross at the lower left of the left photo was scored with a craft knife to make it easier to photograph. That area contains deliberate scratching, so its line width and sharpness are not what the burn produced.**

After fine calibration, the left and right arms of the result disc run closer to straight radial lines. Some local imperfections remain, but the arms are easier to trace for another round. The two photos were taken at different angles and in different light, so this is a visual comparison only; no millimetre error or physical accuracy can be derived from it.

To keep refining, open **the record the result disc was generated with**, import that disc's photo, rectify again, trace the same clear arm and solve. Use that disc's generation radii and three coefficients as the complete input; apply the new result before burning the next disc, and never add new coefficients onto old ones by hand.

## How the parameters changed, with reference values

This table lists the generation parameters of the three main-line rounds, so you can see how the starting radii turned into the fine-calibrated result. Opening a record restores that row's parameters; the correction it measured goes into the next target.

| Round (record) | Inner radius / mm | Outer radius / mm | Pitch set |
| --- | --- | --- | --- |
| Round 1 (`4f0228e6`) | 24.1 | 57.8 | A |
| Round 2 (`3f2ce8c2`) | 23.99575834805153 | 57.97592653885557 | A |
| Round 3 (`14580aac`) | 23.990847220268247 | 57.96535646048506 | A |

The result disc after round 3 was generated with an inner radius of **23.99144094121467 mm** and an outer radius of **57.964899248503315 mm**, using pitch set **B** below. These are the parameters behind the final photo above.

| Field | A: generation values of the three main-line rounds | B: generation values of the result disc |
| --- | --- | --- |
| `pitchLinear` | -3.9256854319227684E-05 | -7.937249285949148E-05 |
| `pitchQuadratic` | 6.964795915429256E-05 | 0.00014593344294674726 |
| `pitchCubic` | -4.670918950137523E-05 | -8.817669151956023E-05 |

These are dimensionless relative shape coefficients, not track-pitch values in μm. The model is `p(x) = p0 × (1 + c1*x + c2*x² + c3*x³)` with `x = (r - inner radius) / (outer radius - inner radius)`; `p0` follows from the total track length. Inner/outer radius, capacity and coefficients must be saved together; see the [preset documentation](DISC_PRESETS.md) for the format.

| Transition | Inner radius change / μm | Outer radius change / μm | Coefficients |
| --- | --- | --- | --- |
| Round 1 disc → round 2 disc | -104.241652 | +175.926539 | Keep A |
| Round 2 disc → round 3 disc | -4.911128 | -10.570078 | Keep A |
| Round 3 input → result disc | +0.593721 | -0.457212 | A updated to B |

In this example, rounds 1 and 2 corrected the inner and outer radius while keeping pitch set A; round 3 adjusted the radii and the pitch shape together, and the result disc uses set B. Capacity and bit length stayed the same throughout.

These values are for reproducing this example and understanding how the parameters move. For your own discs, adjust from your own photos and save the complete parameter set as a user preset.

## Troubleshooting

| Symptom | Check first |
| --- | --- |
| Round 1 only gives a band estimate and can't read the angle marks | Are the six points in one direction, on the edges of whole bands? Reshoot with different lighting |
| Round 2 result sits at the end of the search range | Are the starting values and range sensible? Add clear boundaries at other radii and solve again |
| Round 3 only fits the radii, no new pitch coefficients | Is the cubic fit ticked? Are there enough points at different radii, with enough coverage? |
| Lots of points but the prediction doesn't fit | Did you mix different arms or boundaries, mistake glare for pattern, or misplace the rim points? |
| Reopening shows the old parameters | The record restores this disc's generation parameters; solve, apply, then save as a preset |
| The new disc is still curved | Did you apply the result and create a new target so all five geometry values carry over? Keep measuring with the new disc's own record |

Back to [README](../README.en.md) · [Full calibration guide](CALIBRATION.en.md) · [Disc preset format](DISC_PRESETS.md)
