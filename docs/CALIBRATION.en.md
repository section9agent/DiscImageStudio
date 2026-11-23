# Assisted calibration

*English translation of [CALIBRATION.md](CALIBRATION.md). Button names match the English UI.*

If this is your first time, read it alongside the [hands-on tutorial: Verbatim AZO DVD-R](CALIBRATION_TUTORIAL.en.md), which shows real records, the actual points clicked in all three rounds, and how the disc looked after fine calibration. This page first covers the photo rectification that every round shares, then generating, measuring, applying, and repeating fine calibration.

Open **Calibration** from the home page or the project sidebar and choose **Round 1 · Rough size**, **Round 2 · Refine edges**, or **Round 3 · Straighten lines**. If you already have reliable starting values you can begin at round 2 or 3. Every disc must be analysed with the `.calibration.json` record saved when it was generated, plus a photo of that same disc.

With a project open, the calibration page keeps both navigation groups ("Design, Export, Burn" and "Preview, Calibrate"); calibration burns live under "Calibrate". Calibration uses its own disc parameters, so changing settings here never touches your design project. Without a project you can calibrate straight from the home page. **Back** returns to wherever you came from.

## What this fork adds

- **Calibration guide.** A banner under the round selector names the current round, shows a progress rail (*Make test disc → Burn it → Photograph → Check edges → Measure → Apply*), says in one sentence what to do next, and has a button for that action. **Hide guide** tucks it away.
- **Automatic edge finding.** When you import a photo, the app looks for the disc's physical outer edge and the centre hole's cut edge and places the 16 rectification points on them. On the example records it lands within 3–5 px of the hand-placed rim points and 1–4 px of the hole points. Always zoom in and check; drag any point that is off. If the rim is cropped or too faint, the app says so and falls back to the old default circle.
- **Snap to lines** (on by default, toggle in the guide while measuring). In round 2 one click on a band edge traces along that edge and adds its points; the trace stops where the edge fades or another line crosses, so click again past that spot. In round 3 each click snaps onto the centre of the thin line. Points stay editable as before.

## Rectify the photo first

Rectify the photo of every calibration disc before measuring bands or tracing lines. If you haven't burned a disc yet, read the photo requirements here and then generate a target as described in the next section.

1. Take a photo or scan that shows the **entire physical outer edge of the disc and the centre hole**, with the whole rim in frame. If glare hides the area you need to measure, change the lighting and photograph the same disc again.
2. Open **the calibration record saved when this disc was generated** and import its photo. Choose the physical size, **12 cm or 8 cm**: this sets the physical outer radius to 60 mm or 40 mm. It is not where you enter the recording-area radius.
3. Drag the outer control points onto the **disc's physical outermost edge** and the inner control points onto the **edge of the centre hole**. (In this fork they usually start there already.) The edge of the recorded area, the clear plastic ring, bright reflection rings and shadows are not the physical boundary. Scroll to zoom and right-drag to pan; zoom in on the hole edge in particular.
4. Click **Calculate rectification** and check the rim and centre hole in the straightened preview. If needed, go back, adjust points and calculate again; once it looks right, confirm the rectification.
5. Do all later measuring on the **rectified photo**. After changing the physical size or any boundary point you must rectify and measure again; don't keep using points from the old coordinates.

**Photo resolution.** Imports keep the original pixel size where possible: the long side is capped at **8192 px** and the total at **33,554,432 pixels (32 × 1024 × 1024)**. Anything larger is scaled down proportionally. Ordinary 12- and 24-megapixel phone photos stay full size.

The rectified image's size follows the local pixel density of the disc in the photo, plus a **3 mm** margin outside the physical rim; it is no longer fixed at 1400 × 1400. Its output is also capped by a pixel budget, at most **5792 × 5792**. Fitting to the window and zooming only change the display, never the working image. More pixels help you inspect detail but don't raise measurement accuracy by the same amount; still check the physical edges, glare, and the result of reburning.

<img src="images/verbatim-azo-20261104/00-photo-points.png" alt="Rim and centre-hole control points on a real disc photo, with an enlarged view of the centre hole" width="900">

This figure is rebuilt from the Verbatim AZO DVD-R example record: **cyan** marks the 8 points on the physical outer edge and **orange** the 8 points on the centre-hole edge; the enlargement shows which ring is the right one. It illustrates the recorded points; it is not a screenshot of the app.

<img src="images/verbatim-azo-20261104/00-rectified.png" alt="Straightened disc preview rebuilt from the example record with the app's rectification model" width="640">

The physical size restores the millimetre scale; the size of the recorded area comes from the target's generation parameters. The figure above rebuilds the rectified preview from the same record: the **cyan reference circle** is the physical outer edge and the **orange reference circle** the centre hole. Rectification keeps the real curvature of the burned pattern and does not separately correct lens distortion. Never move the rim points to make the cross lines look straight.

## Choose a round and generate the calibration disc

| When to use it | Pattern burned | What you measure on the photo |
| --- | --- | --- |
| Inner and outer radius still unknown | **Round 1 · Rough size**: three wide bands with angle marks inside each band | Along one radius, the inner and outer edge of all three bands: six points |
| You have starting values and need to refine the radii | **Round 2 · Refine edges**: wide light and dark areas; two opposite boundaries swing back and forth along the radius | The clear **light/dark boundaries**; trace one long stretch where it is continuous, or several segments where it is hidden |
| You can see the thin arms and want to check the final geometry | **Round 3 · Straighten lines**: concentric circles plus a cross | One clear thin arm, clicked along its **centre line** from inside to outside |

1. Choose the round, disc type and the preset matching your blank discs, and check the inner/outer radius and sector count. **Read from disc** fills in the sector count the disc reports (with several recorders, pick one first). Discs with the same capacity can still have different radii.
2. Click **Create target from parameters**. The disc type, geometry, capacity and relevant encoding settings at that moment are fixed in the record. Save the previous disc's record first if you want to keep its photo and inputs.
3. Generate files from the calibration page, or choose streaming burn. CD produces `.wav` (with `.cue`) or `.raw`; DVD and BD produce a drawing-only `.iso`. A `.calibration.json` with the same name is saved next to the file. Streaming burn also saves the record first, then opens the burn page to choose a recorder and confirm.
4. After measuring this round and applying the radii, choose the next round, **create that round's target**, then burn and photograph the new pattern. Switching rounds only changes the next target's settings; it never rewrites the record of a disc you already burned.

Standalone calibration shares the task bar at the bottom of the window with project pages. Streaming burns show preparation, writing and closing, the amount the recorder confirmed, live speed and progress there; expand "Task details" to read or copy the log, or cancel the burn. Progress stays visible when you switch pages or go back home, and the log opens by itself on failure. Calibration solving and file generation report their status there too; calibration records and project output are saved separately, and a finished burn never shows a previous export as its result.

For CD-R/RW the sector count comes from the pregroove lead-out address; single-layer writable DVD uses the total the drive reports. Automatic BD-R reading is limited to the supported 25 GB single-layer sequential no-spare layout. If reading fails, check the media or type the value in.

**Pattern preview and burning.** The page previews the burn pattern at 8192 × 8192. The wide-boundary and cross patterns can be exported as a PNG of the same size; for the band-and-angle-mark pattern the PNG is an illustration of the disc surface. For real burns, generate the track or image from the calibration page, or use streaming burn, so the disc matches its saved record.

## Round 1 · Rough size: estimate the radii from wide bands and angle marks

The three wide bands occupy 3.5–17%, 30.5–51% and 70.5–95.5% of the drawing area's addresses, each with angle marks inside. Measure the band edges first; the app then reads the stripes inside the bands to refine the radius estimate.

<img src="images/verbatim-azo-20261104/01-coarse-points.png" alt="Round 1 points: the six band edges marked along one radius on the rectified photo" width="800">

The six points rebuilt from the example record are numbered **1–6** from inside to outside and correspond to **1 inner, 1 outer, 2 inner, 2 outer, 3 inner, 3 outer** in the app. You measure where each whole band meets the blank area; the fine stripes inside the bands are for reading the angle. The example parameters and the results before and after the angle correction are in the [tutorial](CALIBRATION_TUTORIAL.en.md).

1. Find a direction on one side where all bands are visible from inside to outside. A clear area of about 45° is enough to try a reading; if other areas are clear too, the app checks them as well. Glare and the light/dark mapping of Blu-ray discs can make the three bands look bright; go by where band and gap meet.
2. Starting with the band nearest the centre, click **1 inner, 1 outer, 2 inner, 2 outer, 3 inner, 3 outer** in order. The first click sets the direction; then click each whole-band edge along the guide line. Leave the stripes inside the bands to the app. If you misplace one, select that boundary and click **Remeasure boundary**; to use another side, click **Clear all boundaries and start over**.
3. Click **Estimate radii and read angle marks**. When the three marks agree, the page shows the band estimate, the angle-corrected inner and outer radius, and the per-turn angle error at the inner, middle and outer marks relative to the generation values. A positive value means the real disc turned further over the same number of sectors. A new target built from the result uses the angle-corrected values.
4. If the stripes are too faint at all three places or disagree, the page only shows the band estimate. Reshoot the same disc with different lighting. If glare covers the middle, measuring both sides of bands 1 and 3 still gives a radius estimate, but the angle correction needs six points in one direction. Round 1 assumes a constant track pitch; any pitch coefficients you have set are kept for later rounds.

If one round still doesn't give good starting values, repeat it: **apply the rough-size correction → save the current record → keep Round 1 selected and create a new target → burn it on a new blank → photograph, rectify and measure the six points again → estimate again**. Each new disc must use its own generation record and photo; recalculating the same photo is not the same as iterating with new burns. Once the starting values are good, apply them and create a round 2 disc. The tutorial includes a [supplementary record that went from the same starting values through three rough-size rounds into round 2](CALIBRATION_TUTORIAL.en.md); three is just how many that attempt needed, not a requirement.

<img src="images/verbatim-azo-20261104/02-boundary-after-three-coarse-points.png" alt="Round 2 example after three rough-size rounds: seventeen boundary segments and 169 points saved on the rectified photo" width="800">

The figure shows round 2 after three rough-size rounds, rebuilt from the record: **17 segments, 169 points**. Colours only tell segments apart; dashed lines show the click order within a segment, not a fitted prediction. Generation parameters and detailed steps are in the [tutorial's supplementary case](CALIBRATION_TUTORIAL.en.md).

The drawing area has to be wide enough to separate the three angle marks. Standard 12 cm CDs are supported from 70 to 85 minutes (315,000–382,500 sectors) at 1200 mm/s. Single-layer DVD and BD must also meet their capacity, sector alignment and band-width requirements. If an 8 cm CD or a small DVD with a very narrow drawing area can't create this pattern, check the preset, or start at round 2 with existing values.

## Round 2 · Refine edges: trace light/dark boundaries continuously or in segments

At every radius the wide-boundary pattern is half light and half dark. The two boundaries are 180° apart and swing slowly back and forth three times across the drawing area. Locally clear areas usually give boundaries spread over different radii.

The pattern is curved by design, so judge the result by whether the predicted boundaries hug the photo, not by whether anything looks straight. You measure the light/dark boundary, not a band's centre line. Trace the boundary where it actually goes, whether straight, curved, doubling back or wrapping around.

<img src="images/verbatim-azo-20261104/02-boundary-points.png" alt="Round 2 points: thirteen light/dark boundary segments with 154 points on the rectified photo" width="800">

This figure rebuilds the example's **13 segments and 154 points**. **Colours only tell segments apart, and dashed lines connect the points of one segment in click order**; they are not a fitted prediction. These counts are what this record happened to have, not a requirement for every disc. More examples and reference parameters are in the [tutorial](CALIBRATION_TUTORIAL.en.md).

1. Create a round 2 disc with the parameters from round 1, burn it and photograph it. Open the matching record and confirm the rectification.
2. Where a boundary is clear and continuous, click along it in order, or click a turning point first and extend to both ends. (With **Snap to lines** on, one click traces along the edge for you.) A preview line shows where the point will connect. Near an existing line, a new point is inserted into that segment; when both ends are close, click the end you want to continue from to fix the direction, and press **Esc** to return to automatic. Dragging a point only corrects its position; the click order survives saving and reopening. A segment needs at least 6 different points and must cover 55% of the drawing radius width; add more points where it turns a lot. Straight, gently curved and doubling-back stretches are all fine; there's no need to split segments artificially or hunt for sharp tips.
3. Skip what you can't see clearly: click **Start another segment** and pick another visible boundary. If you only have short stretches, use at least 4 different radii with at least 3 points each; 5–8 segments is a good target. With two long boundaries, at least 6 points each spanning 10% of the drawing radius width works. In every case the total must span at least 55% from inside to outside. You don't need to count turns or bridge parts hidden by glare. About 45° on one side is usable, and any other clear areas help. Points should follow the boundary, not sit on one radius at its crossings.
4. Click **Solve parameters**. A new round 2 target searches ±0.01 mm around the starting values by default, which suits a round 1 result already within about ±0.005 mm; a manual range saved in the record is restored with it. If the best value sits on the edge of the range, check the starting values or widen the range manually. With too few points, too little coverage, or candidates that can't be told apart, add segments elsewhere and calculate again.
5. Check that the two predicted boundaries hug every visible boundary, traced or not. When the prediction is shown and you've ticked that it matches, apply the current candidate and use the corrected parameters for a round 3 disc. Large deviation, a search on the edge of its range, or several solutions keep a warning for you to judge, but they don't block applying a candidate you've confirmed matches. Switching candidates or editing points clears the confirmation, and edited points need solving again. This round keeps the disc's generation track-pitch coefficients; the cubic pitch fit belongs to round 3.

Segmented measuring still needs enough spread across radii and enough local direction. A single clear continuous arc can exceed 90°, so don't split it just for an angle limit; add points spread along the boundary and avoid gaps of nearly half a turn between neighbouring observations. Tracing the same place twice adds no independent evidence. When coverage is short or ambiguous, prefer adding points on the same disc, or reshoot it with different lighting.

## Round 3 · Straighten lines: click along one thin cross arm

Round 3 uses concentric circles and a cross. The circles scale with the disc's generation radius, so how many you can see depends on the recorded area.

<img src="images/verbatim-azo-20261104/03-fine-points.png" alt="Round 3 points: thirty-three points along the centre line of one curved thin cross arm, from inside to outside" width="800">

This figure rebuilds the example's **33 real points**. The line only shows the click order and is not a fitted prediction; **1, 6, 12, 18, 24, 33** are point numbers. Follow the real arm's centre line; don't turn onto a circle or jump to another arm. In the example's original photo, the twisted cross at lower left was scored with a craft knife to make it easier to photograph. Its width and sharpness there are not what the burn looked like; see the original photo and note in the [tutorial](CALIBRATION_TUTORIAL.en.md).

1. Click evenly along the centre line of one clear arm, from the centre outward. (With **Snap to lines** on, each click lands on the line's centre.) To correct only the radii you need at least 6 points spanning at least 18% of the generated band width; more points on clear stretches help. Points don't have to sit on circle crossings, but always stay on the same arm. Drag existing points to correct them, click empty space to add one, and press Delete to remove the selected point.
2. For CD, DVD and BD you can tick **Fit cubic track pitch** in round 3. It needs at least 12 points at different radii covering 60% of the band's radial width. With fewer points or less coverage, the existing pitch shape is kept and only the radii are fitted.
3. Click **Solve parameters**. The search starts at ±0.02 mm and widens automatically when the best solution nears the edge. If two neighbouring points are nearly half a turn apart, add points along the real path so the curve can be followed correctly.
4. Check that the predicted line on the photo follows the real arm from inside to outside. Points far off are shown in red. A mean deviation above 0.35 mm or a maximum above 0.8 mm triggers a "large deviation" note. These thresholds are a reference; you can still apply after ticking that it matches. If only a few points agree, add points in the middle and solve again.
5. Once the prediction is shown, tick that the outline matches and apply the parameters. To use the result in a design project, **apply first, then Save as preset**, and choose that preset back in the project. Switching candidates or editing points needs checking again; edited points need solving again.

Non-zero CD coefficients use the same arc-length model as DVD and BD: total track length is `sectors / 75 × linear velocity`, and byte position `q` maps to `q × linear velocity / (75 × 2352)` mm. With all three coefficients zero, the original area formula and output are kept. This convention matches the parameter conversion in [CD non-constant track pitch feedback #78](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/issues/78); repeated-calibration checks cover the coefficient-bearing target discs described in [#92](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/issues/92).

## Checking after round 3, and repeating it

After applying a round 3 result, stay in **Round 3 · Straighten lines**, **create a new target** and burn a new disc, saving its generation record. Photograph it and check whether the thin arms now run closer to straight radial lines from inside to outside, and whether any local bends or hard-to-read areas remain. The tutorial's result disc still has local imperfections, but its arms are much easier to trace for another round.

To run round 3 again, open **the record saved when the new disc was generated**, import the new disc's photo, rectify again, then trace the same clear arm, solve and check the prediction. Never analyse a new photo with the previous disc's record, and never change only the radii while keeping coefficients that don't belong with them.

The fit uses the disc's generation radii and all three pitch coefficients together and outputs a complete new parameter set; **don't add the result to the old coefficients by hand**. Applying a candidate only updates the next target's settings; the current photo's generation parameters stay unchanged. Inner/outer radius, capacity and the three coefficients must always be used as a set.

## Saving, going back, and older records

**Back** leaves an unfinished calibration; the app keeps the current draft. With a project open you return to the page you came from; from the home page you return home. **Save calibration session** lets you reopen the original pattern, photo, rim and hole points, marks and traced points later. After changing the physical size or boundary points, rectify and measure again; after editing measured points, solve again. Save the record you want to keep before creating each new target.

**Save calibration session** keeps this disc's generation parameters and measurements. If you've checked the prediction and ticked that the outline matches, it also saves that fit, its errors and the selected candidate; you don't have to apply first. Reopening rebuilds the prediction, restores the confirmation if it succeeds, and puts the selected fit back into the next target's settings. The original target's parameters stay unchanged, so the photo still matches the right generation geometry.

Fits that were never confirmed, were unconfirmed, or went stale after measurements changed are not saved. Older records, and records without a confirmed fit, still restore the disc's original generation parameters and need solving and confirming again. **Save as preset** saves the current settings; to use a fit in a design project, apply it, save a preset, and choose that preset in the project.

Photos are embedded in the record as lossless PNG at the working resolution after import. When you reopen it, the photo keeps the saved pixel size and the rectified image is rebuilt by the same rules at the same size, without needing the original photo file. All sampling uses an 8-bit-per-channel BGRA working image, and high-bit-depth photos are converted to it first, so keeping the pixel size doesn't mean keeping the original bit depth. High-resolution photos make records larger and need more memory and time to read, rectify and save.

New versions still open older calibration records, but photo detail already scaled down in an old record can't be recovered; re-import the original photo, rectify and measure again. Older app versions can't read new high-resolution records beyond their photo size or file size limits.

For discs you already have, open the record they were generated with. The app restores the matching reference image, combined pattern, timing marks, short arcs or wide bands, and keeps old three-point or arc-drag data. On old single-line discs, keep tracing along the **centre line** of the 6 mm thick line; on half-black/half-white discs, trace along the diameter boundary. Existing wide-band discs can be re-measured along a radius; discs with angle marks inside the bands need six single points in one direction for the three-mark correction. Old discs with bands only can still be measured at the band edges, but angle marks that were never burned can't be read.

## References

The light/dark boundary measurement follows the idea in [community discussion #49](https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio/issues/49). The cross reuses the project's original reference image; see the [reference image notes](../src/DiscImageStudio.Core/Calibration/Patterns/README.md) for the original and file details.
