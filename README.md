# Pluto AR Flight School

An Android AR app that lets beginners learn to fly the **Pluto** drone safely. A virtual Pluto X is anchored on a real table or floor through the phone camera, and the pilot flies it with an on-screen controller that copies the Pluto Controller app. The app works without connecting to a real drone.

**Pitch:** *Learn to fly the Pluto drone in your own room: zero crashes, zero broken propellers.*

## Features

* **AR scene:** ARCore plane detection, tap to place, world anchor, re-place button, tracking-loss messages, and a ground shadow to judge height.
* **Virtual controller:** two multi-touch joysticks plus ARM/DISARM, TAKE OFF and LAND buttons.
* **Flight model:** altitude hold, smoothed acceleration (auto-stabilise), speed and altitude limits, a ground floor, a range limit, body tilt and spinning propellers.
* **State machine:** Disarmed → Armed → Taking off → Flying ⇄ Landing → Landed → Disarmed (+ Crashed). Invalid actions are blocked, for example disarming in the air.
* **7-stage guided tutorial:** every stage is completed by a real pilot action and gives corrective prompts as you fly.
* **3 scored missions:** Takeoff & Landing, Hover & Directional Control, and the AR Navigation Challenge (rings + pillars).
* **HUD ("Pluto Flight Academy" design):** brand header with a live tracking pill, Help and Menu; an objective card with step count, live hints and a progress meter driven by the real stage checks; a telemetry card (altitude, flight state, vertical speed, ground speed, timer, altitude bar with target marker); one prioritised warning area; scanning guidance with an aiming cue; and welcome, mission-select and results screens that show only measured values. Colours, spacing and rounded shapes come from `PlutoTheme.cs`, and every touch target is at least 48 dp.
* **Extras:** difficulty levels (wind + speed), a flight-path trail, keyboard and gamepad support, and a laptop test mode in the Unity Editor.

## Tech stack

| | |
|---|---|
| Engine | Unity 6 (6000.6.7f1), Built-in render pipeline |
| AR | AR Foundation 6.7.1 + Google ARCore XR Plugin 6.7.1 |
| Input | Unity Input System 1.20.1 (uGUI on-screen sticks) |
| Model import | glTFast 6.1.0 (Pluto X `.glb`) |
| Language | C# |
| Target | Android 7.0+ (API 24), ARM64, IL2CPP, ARCore-supported phone |

## Setup & build

See [`SETUP_GUIDE.md`](SETUP_GUIDE.md). In short: open the project in Unity Hub, then run **Pluto → 1. Configure Android Settings**. Tick **Google ARCore** under XR Plug-in Management and click **Fix All** in Project Validation. Then run **Pluto → 2. Create AR Scene** and **Pluto → 3. Build and Run on Phone**.

## Controller mapping (Mode 2, as in the Pluto Controller app)

| Control | Action | Keyboard (editor) | Gamepad |
|---|---|---|---|
| Left stick ↑ / ↓ | Throttle: climb / descend (centre = hold altitude) | W / S | Left stick |
| Left stick ← / → | Yaw: rotate left / right | A / D | Left stick |
| Right stick ↑ / ↓ | Pitch: forward / back (relative to the nose) | ↑ / ↓ | Right stick |
| Right stick ← / → | Roll: slide left / right (relative to the nose) | ← / → | Right stick |
| ARM / DISARM | Start / stop motors (only on the ground) | Space | A / Cross |
| TAKE OFF | Auto-climb to 0.4 m and hover | T | Y / Triangle |
| LAND | Auto-descend at 0.25 m/s | L | B / Circle |

The red/green LEDs on the drone mark its **nose** (forward).

## Simulation logic

All positions are in metres in the Arena's local space, where y is the height above the real surface. Every frame (`DroneController.cs`):

```
target   = (noseForward * pitch + noseRight * roll) * maxSpeed  +  up * throttle * maxClimb
velocity = Lerp(velocity, target, 1 - exp(-responsiveness * dt))   // gentle auto-stabilise
yaw     += yawInput * yawRate * dt
position += velocity * dt (+ optional wind drift)
clamp: y >= 0 (ground), y <= 1.6 m, horizontal range <= 2.5 m
touching the ground while descending → Landed (warning if faster than 0.6 m/s)
```

Defaults: 0.5–0.8 m/s horizontal speed (depends on difficulty), 0.5 m/s climb, 100°/s yaw, and a model about 15 cm wide. The drone lifts off when throttle goes above 30%. In Mission 3 a pillar collision triggers a crash, which plays a fall and then disarms the drone.

## Guided tutorial

| # | Stage | Completes when… | Corrective feedback |
|---|---|---|---|
| 1 | Meet the controller | each stick pushed in all 4 directions | live checklist |
| 2 | Arm | state becomes Armed | "sticks do nothing until armed" |
| 3 | Take off | altitude 0.5 ± 0.1 m held for 2 s | climb more / too high |
| 4 | Hover | inside the hover box for 5 s | "drifting left – nudge right stick right" |
| 5 | Directions | 30 cm forward, back, left and right | per-direction progress, "too low" |
| 6 | Navigation | 3 rings passed in order | direction + distance to the next ring |
| 7 | Land & disarm | soft touchdown on the pad, then DISARM | off-pad / too-hard landing |

## Missions

| Mission | Goal | Score |
|---|---|---|
| 1 Takeoff & Landing | climb to 0.4 m, land on the orange pad, disarm | 1000 − 8/s − landing error − hard-landing penalty |
| 2 Hover & Directional | hold the hover box 5 s, visit 4 waypoints within 0.35–0.65 m, land, disarm | 1000 − 5/s − 60 per second outside the altitude band |
| 3 AR Navigation | 5 rings in order around 3 pillars, land, disarm (hitting a pillar = fail) | 1500 − 8/s |

Best scores are saved on the phone.

## Code map (`Assets/Pluto/Scripts`)

| File | Responsibility |
|---|---|
| `PlutoGame.cs` | entry point, builds everything, app flow, menu, difficulty |
| `ARPlacementManager.cs` | plane detection, tap-to-place, anchor, tracking messages, editor fallback |
| `DroneController.cs` | flight model |
| `FlightStateMachine.cs` | flight states and allowed transitions |
| `DroneVisual.cs` | model loading/scaling, propellers, tilt, LEDs, shadow, trail |
| `ControllerUI.cs`, `VirtualJoystick.cs` | virtual Pluto controller |
| `HUDManager.cs`, `UIFactory.cs`, `PlutoTheme.cs` | cockpit HUD, warning area, dialog screens, design tokens and generated rounded sprites |
| `Stage.cs` | stage runner + shared helpers (`GuidedMode`) |
| `TutorialManager.cs`, `MissionManager.cs` | tutorial and missions |
| `ArenaBuilder.cs`, `Checkpoint.cs` | pads, zones, waypoints, pillars, rings |
| `Editor/PlutoSetup.cs` | **Pluto** menu: Android settings, scene creation, build |

## Device requirements

* An Android phone on Google's **ARCore supported devices** list, running Android 7.0 or newer, with Google Play Services for AR installed.
* A well-lit, textured horizontal surface. Plain white or glossy tables track poorly.

## Known limitations

* The physics is simplified on purpose. There's no aerodynamics, battery model or real PID loop.
* Collisions only exist with the virtual pillars and the ground. Real objects such as walls or cups are not detected, because there is no depth occlusion.
* Only horizontal planes are used. The drone stays on the plane where it was first placed.
* The CAD model was cut down to 150k faces with Blender for phone performance, so some fine detail is lost.
* The joystick layout follows the standard Mode 2 layout; button positions may differ slightly from the latest Pluto Controller app.

## Third-party assets

* **Pluto X 3D model**, supplied by the organisers (Drona Aviation). It was merged and reduced in Blender so the propellers can spin.
* Unity packages: AR Foundation, Google ARCore XR Plugin, Input System, glTFast, uGUI.
* Everything else was written by the team: the UI, joysticks, placeholder drone, rings and pads.

## Testing log

Test device: **Realme GT 7 (RMX5061)**, Android 16, MediaTek Dimensity (MT6989), Google Play Services for AR 1.57. Surface: wooden desk under indoor lighting. The editor test mode was also checked on a Windows 11 laptop in Unity 6000.6.7f1.

| # | Build | Test | Result |
|---|---|---|---|
| 1 | v0.1 | App launch, camera permission, ARCore session start | ✅ Pass |
| 2 | v0.1 | Plane detection on the wooden desk | ✅ Pass. Plane outline shown after slowly moving the phone |
| 3 | v0.1 | Tap-to-place | ❌ Fail. Taps outside the exact detected polygon were ignored |
| 4 | v1.0 | Tap-to-place with aiming ring + infinite-plane fallback | ✅ Pass. Fix for #3 |
| 5 | v1.0 | Drone stays anchored while walking around the desk | ✅ Pass |
| 6 | v1.0 | Joysticks: throttle, yaw, pitch, roll (both thumbs together) | ✅ Pass |
| 7 | v1.0 | ARM / TAKE OFF / LAND buttons; disarm blocked in the air | ✅ Pass |
| 8 | v1.0 | Guided tutorial stages 1–7 completed in order | ✅ Pass |
| 9 | v1.0 | Mission 1: Takeoff & Landing | ✅ Pass |
| 10 | v1.0 | Mission 2: Hover & Directional Control | ✅ Pass |
| 11 | v1.0 | Mission 3: AR Navigation (rings + pillar crash) | ✅ Pass |
| 12 | v1.0 | Editor test mode (keyboard flight on the laptop) | ✅ Pass |
| 13 | v1.0 | Demo video recorded with scrcpy | ✅ Done |
