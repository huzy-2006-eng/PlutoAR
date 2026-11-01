# Pluto AR – Step-by-step setup (do these in order)

The whole project is already written in `PlutoAR\`. You only need to open it, click 3 menu items and build.
Total time ≈ 20–40 min (mostly package download + first Android build).

---

## Step 1 — Open the project (Member 1, on the build laptop)

1. Open **Unity Hub** and make sure you are **signed in** (top-left avatar). Unity will not open without a licence.
2. **Projects → Add → Add project from disk** → select the folder
   `C:\Users\Huzaifa the Great\Pluto\PlutoAR` → it should show editor **6000.6.7f1**. Click it to open.
3. The first open downloads AR Foundation, ARCore, Input System and glTFast. Wait for it to finish (needs internet, 5–10 min).
4. If a popup says **"enable the new Input System backends? Unity will restart"** → click **Yes**.
5. If a popup asks about **API updates** → click **"I Made a Backup. Go Ahead!"**

> ❗ If the Console (bottom panel) shows **red errors**, tell Claude — it can read Unity's log
> (`%LOCALAPPDATA%\Unity\Editor\Editor.log`) and fix them.

## Step 2 — Configure Android (1 click + 1 tick)

1. Top menu bar → **Pluto → 1. Configure Android Settings**. (Switching to Android takes a minute.)
2. **Edit → Project Settings → XR Plug-in Management** → click the **Android tab (robot icon)** → tick **Google ARCore**.
3. In the same window, open **XR Plug-in Management → Project Validation** → click **Fix All** (repeat until nothing red is left).

## Step 3 — Create the scene (1 click)

1. **Pluto → 2. Create AR Scene**.
   This creates `Assets/Pluto/Scenes/PlutoAR.unity` with the AR camera, plane detection and the `PlutoGame` object, already using the Pluto X model.
2. Click the **PlutoGame** object in the Hierarchy. In the Inspector, **Drone Model** should say `PlutoX`.

## Step 4 — Test on the laptop (no phone needed)

Press **▶ Play**. You get a grey floor instead of the camera.

* Click the floor → drone + landing pad appear → menu opens.
* Keyboard: **W/S** throttle, **A/D** yaw, **arrow keys** pitch/roll, **Space** ARM/DISARM, **T** take off, **L** land.
* You can also drag the on-screen sticks with the mouse.
* **If the drone flies backwards** (its camera faces you): select PlutoGame → set **Drone Model Yaw Offset** to `180`, then press Play again.

Members 2 & 3 can tune flight feel / tutorial texts this way while Member 1 does the phone build.

## Step 5 — Put it on the phone

On the phone (once):
1. **Settings → About phone → tap "Build number" 7 times** → Developer options are on.
2. **Settings → Developer options → USB debugging = ON**.
3. Play Store → update **Google Play Services for AR**.
4. Plug into the laptop with a **data** cable → on the phone tap **Allow USB debugging**.

In Unity: **Pluto → 3. Build and Run on Phone**.
The first build takes 5–15 min. The app opens on the phone automatically; the APK is also saved to `PlutoAR\Builds\PlutoAR.apk`.

To mirror the phone on the laptop for the demo, run `scrcpy` in a terminal.

## Step 6 — Using the app

1. Point the phone at a **textured table / floor** (use the mat/newspaper) and move it slowly → cyan surfaces appear.
2. Tap a surface → drone lands on a blue **H** pad → menu.
3. Choose **Guided Tutorial** first, then **Missions 1–3**, or **Free Flight**.

## If something goes wrong

| Problem | Fix |
|---|---|
| "No valid Unity licence" | Sign in to Unity Hub, Preferences → Licenses → Add → Personal |
| Build error mentioning SDK/JDK/NDK | Unity Hub → Installs → 6000.6.7f1 → ⚙ Add modules → tick Android SDK & NDK Tools + OpenJDK |
| Phone not found during Build and Run | Different cable/USB port; accept the USB-debugging popup; run `adb devices` |
| Black screen on phone | ARCore not ticked in XR Plug-in Management (Step 2.2), or Play Services for AR not installed |
| "This phone does not support ARCore" | Use another phone from Google's ARCore supported devices list |
| Planes never appear | More light; textured surface; move the phone slowly sideways |
| Drone too big/small | PlutoGame object → PlutoDrone is built at runtime; change `targetWidth` in `DroneVisual.cs` (metres) |
| Propellers don't spin | They spin only when ARMED. Model parts must be named `Prop_…` (already done for PlutoX) |
