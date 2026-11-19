# Music + hidden art on one audio CD (fork feature)

This fork can put a picture *and* playable music on the same CD-R, laid out so a
normal CD player never plays the picture.

## Two layouts

| | Hidden pre-gap | Second session |
|---|---|---|
| Where the art sits | Inner ring, from the hub out to a radius you choose | Outer ring, after the music |
| How it's hidden | It's track 1's pre-gap (INDEX 00); players start at INDEX 01 | Players read only session 1's table of contents |
| Catch | Rewinding back past track 1 on some players plays the art as loud noise | You lose about 2.5 min of disc to the session lead-out/lead-in, which shows up as a plain ring |
| Burned with | Windows IMAPI2 (built in) | cdrecord (cdrtools) |

In both layouts the art bytes are exactly what a normal whole-disc art track would
have at the same sectors, so your calibration presets still apply.

## In the app

1. Make a CD project and design the art as usual.
2. On **Export**, pick a `.wav` output file. The art track is saved there.
3. In **Music + hidden art**, choose the placement, add your songs (44.1 kHz,
   16-bit, stereo PCM WAV), and set the art radius. The summary box shows the
   layout, or tells you what doesn't fit.
4. Either:
   - **Generate**: writes the art WAV plus a `.cue` (pre-gap; burn it with ImgBurn
     in DAO mode) or a `.cdrecord.cmd` script (session 2).
   - **Burn**: press **Scan**, pick the recorder, then press Burn.

Design your art so its picture lives in the ring you're actually using: for
pre-gap that's the inner part of the disc, for session 2 the outer part.

## cdrecord

The second-session burn uses cdrecord, because IMAPI2 can't leave an audio disc
open for another session. The app looks for it in this order:

1. The path in the card's cdrecord box (use **Browse…**).
2. A `cdrtools` folder next to `DiscImageStudio.exe` (`cdrtools\cdrecord.exe`
   plus its `cygwin1.dll`).
3. `PATH`.
4. `C:\Program Files (x86)\cdrtools` and the cdrtfe tools folder.

On Windows, the 32-bit cdrtools builds are known to detect recorders more
reliably than 64-bit ones. Tick **Test mode** first to run `cdrecord -dummy`
with the laser off.

What the Burn button does for session 2:

1. `cdrecord -sao -multi -audio songs…`: session 1, left open.
2. `cdrecord -msinfo`: reads where session 2 really starts.
3. Generates the art for exactly that sector.
4. `cdrecord -tao -audio art.wav`: session 2, then closes the disc.

Don't take the disc out between these steps.

## Command line

```
disc-image-studio cd-generate --input art.png --output art.wav \
  --placement pregap --art-radius 32 --music "a.wav;b.wav"

disc-image-studio cd-generate --input art.png --output art.wav \
  --placement session2 --music "a.wav;b.wav" [--session-start 13125] [--art-radius 50]
```

`--music-gap none` makes the songs gapless. Without `--session-start`, the
session 2 start is an estimate (music + 11,400 sectors). Check it against
`cdrecord -msinfo` before burning session 2.
