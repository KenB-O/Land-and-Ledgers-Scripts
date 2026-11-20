# Land & Ledgers — Master Scale Audit

**Unity 6000.4.2f1, HDRP. Measurement and recommendations only.**

## Executive finding

The project does not presently share one adult-human physical scale. The standing male measures approximately **1.265 m from boot sole to hat top**. The female measures **1.147 m from boot sole to scalp crown**, or **1.190 m including her hat**. Both are undersized for the proposed adult reference. Their navigation-selection capsules are nevertheless **1.800 m high**.

The horse is **1.518 m to the head/ear envelope**, not 1.518 m at the withers. Its shoulder/withers-region body surface is approximately **1.20 m above the hoof plane**, making the present animal pony-sized rather than a typical freight draft horse. The wagon has a plausible small-wagon envelope, but its seat, hitch and cargo positions must be reconciled with an adult-sized driver and a draft-sized horse before any scaling.

Most architectural doors have a **1.420 m leaf height** and ordinary storeys have **2.000 m floor-to-floor spacing**. They fit the current miniature people much better than full-sized adults. Buildings must therefore be reviewed after people and transport, not enlarged indiscriminately. Natural Wood and Townhall also have major geometry-versus-main-collider mismatches that scaling alone will not fix.

**No production prefab, model import, animation, navigation authority, terrain, economic code or save file was corrected or overwritten.** New static comparison assets are confined to `Assets/Audits/MasterScale/`. The original Main scene's existing dirty state was preserved, not saved by the audit. An isolated runtime test used the comparison scene, not the economic world. The temporary test script was removed and the prior Play Mode start setting restored.

## 1. Master reference and method

Adopt **1 Unity unit = 1 metre**, with a proposed canonical adult male scalp-crown height of **1.75 m** and canonical adult female scalp-crown height of **1.65 m**. These are proposed design references, not claims that all adults should be identical. Define height consistently from boot sole to scalp crown for gameplay clearance; barefoot anatomical stature requires a separately measured sole allowance.

The comparison person is an audit-only copy of Woman in the existing Idle pose. BanBody supplies scalp-crown geometry and BanBoots supplies the support plane. The measured combined height is 1.1472 m; the copy is enlarged by **1.4383**, producing a measured **1.650 m boot-sole-to-crown** reference. Its hat extends higher, approximately 1.712 m. This reference is not the production prefab.

### Measurement distinctions

- Static geometry: actual mesh vertices transformed through the full hierarchy into world space.
- Skinned geometry: independent CPU skinning, summing stored bone weights applied to `bone.localToWorldMatrix × mesh.bindpose × vertex`. This was cross-checked against rig landmarks and representative poses. Four stored weights were used; no renderer-bound height was substituted for stature.
- The first BakeMesh-based pass exposed a scale-convention discrepancy under these scaled renderers. Its results are **superseded**, not correction inputs.
- Translation and frontage yaw were removed for dimensional tables. Import-axis tilt was retained: the -90° X rotation on inch-authored props must not be discarded. Width/height/depth mean normalized world X/Y/Z, not arbitrary mesh-import axes.
- LOD0 renderers were selected explicitly; lower LOD geometry was not summed as additional volume. Active non-LOD meshes were included. Particle/flame/trail envelopes were excluded from physical geometry.
- Renderer and collider bounds were recorded separately. Collider bounds are enclosing boxes, not proof that every point inside the box is solid.
- Standing, walking and Driving clips were evaluated at 0%, 25%, 50% and 75% through an AnimationPlayableOutput on isolated copies. No clip asset was changed.
- Recorded precision is not measurement confidence. Tables are rounded to millimetres; anatomical landmarks, operational intent and some clearances remain uncertain as noted below.

**Important orientation note:** Wagon's length is its root X axis. Horse's anatomical forward is root Z before its authored 90° yaw is applied. Thus Wagon's table width is effectively vehicle length, while Horse's depth is anatomical length. Fence01 runs along Z; its 0.177 m width is post thickness, not panel span.

## 2. Master dimensions table — metres

Human rows use representative standing geometry, not the imported arms-out pose. Architectural rows include active authored exterior attachments. Recommendations are design ranges or conditional correction outcomes, not approved resize commands.

| Prefab | Actual width | Actual height | Actual depth | Recommended dimensions | Assessment |
|---|---:|---:|---:|---|---|
| Man.prefab | 0.463 | 1.265 | 0.275 | Crown 1.75; hat additional; preserve proportions | Requires adult-scale correction. Exact crown-to-hat separation remains unresolved in the combined Cowboy mesh. |
| Woman.prefab | 0.337 | 1.190 | 0.243 | Crown 1.65; audit reference including hat about 1.712, W 0.484, D 0.349 | Requires correction. Measured crown 1.147; approximate visual-root multiplier 1.4383. |
| Horse.prefab | 0.491 | 1.518 | 1.946 | Freight horse withers 1.55–1.75; corresponding head envelope roughly 1.95–2.20 | Requires correction if it represents the opening draft horse; current withers region about 1.20, pony-sized. Breed landmark confirmation required. |
| Wagon.prefab | 4.291 | 2.371 | 1.740 | Retain envelope provisionally; adult-fit seat/hitch/step review | Plausible small covered wagon. X length includes added shafts; stock body/wheels envelope X is 3.072. Do not enlarge merely because people are small. |
| Horse and Wagon.prefab | 4.988 | 2.374 | 1.740 | Recalculate only after component dimensions and hitch separation are approved | Composite, not an independently scaled asset. Root 1; children retain Wagon 0.6 and Horse 0.7. |
| Barrel01.prefab | 0.571 | 0.806 | 0.559 | Retain, typical H 0.75–0.95 and diameter 0.50–0.65 | Correctly sized ordinary barrel. Nested and placed scale variants need naming/use consistency. |
| Can02.prefab | 0.824 | 1.290 | 0.824 | Carryable churn: diameter 0.35–0.45, H 0.55–0.75 | Oversized for a hand-carried milk/liquid can; possibly legitimate bulk storage if deliberately assigned that role. |
| Fence01.prefab | 0.177 | 1.600 | 2.747 | Retain panel span 2.747 and H 1.600 | Plausible livestock fence. End/post-offset pivot is intentional; not a centred panel. |
| HandWaterPump.prefab | 0.226 | 1.710 | 0.943 | Provisional overall H 1.20–1.40; establish grip and spout heights first | Tall for a floor-mounted hand pump. Single mesh includes operating lever; overall top is not itself grip height. |
| LanternHang.prefab | 0.305 | 0.591 | 0.396 | Portable/body H 0.25–0.40; large fixed fixture may retain size | Conditional. Lantern body H 0.563; total includes hanger. Large for a portable lamp but possible fixed fixture. |
| Showel.prefab | 0.358 | 1.728 | 0.062 | Example uniform target 0.280 × 1.350 × 0.048 | Large D-handle spade; candidate reduction about 0.781 after grip/blade review. Import globalScale is already 0.5. |
| WaterTank.prefab | 4.239 | 11.658 | 4.377 | Retain provisionally; elevated storage tower H roughly 8–15 | Plausible elevated/railway tank. Confirm capacity and ladder ergonomics, not just relative size. |
| WindPump.prefab | 10.195 | 26.457 | 12.705 | Ranch role: H 12–18 and rotor diameter roughly 3–6; retain only if deliberately monumental/industrial | Large outlier, role uncertain. An illustrative 0.55 uniform factor gives 5.607 × 14.551 × 6.988; not approved. |
| Package01.prefab | 0.369 | 0.200 | 0.251 | Retain small hand package | Plausible. Slight bottom offset -0.0027 m; fine for placement tolerance. |
| House Natural Wood.prefab | 4.456 | 4.077 | 4.738 | Door clear H ≥2.05–2.10; ordinary storey about 2.8–3.2. Uniform door-derived option: about 6.59 × 6.03 × 7.01 | Architecture undersized at entrances/storey clearance, not simply at roof envelope. |
| Single Story Business Building White Plaster.prefab | 4.272 | 3.320 | 4.722 | Same door/storey criterion; uniform option about 6.32 × 4.91 × 6.98 | Low adult clearance. |
| Double Story Business Building Green Wood.prefab | 4.291 | 5.263 | 6.939 | Same criterion; uniform option about 6.34 × 7.78 × 10.26 | Low 2 m storeys; stairs and duplicate LOD stair collider need review. |
| Double Story Business Building Natural Wood.prefab | 4.473 | 6.098 | 8.240 | Same doorway criterion; uniform visual option about 6.61 × 9.02 × 12.18 | Door undersized; main collider describes a substantially different envelope. Repair geometry/collider agreement first. |
| Double Story Business Building Red Wood.prefab | 8.270 | 5.870 | 8.345 | Same criterion; uniform option about 12.23 × 8.68 × 12.34 | Large footprint itself is legitimate. Door and floor details, not comparison to another building, establish the issue. |
| Townhall.prefab | 10.062 | 5.136 | 11.663 | Door H ≥2.05–2.10; preserve hall volume if selective door repair works. Uniform option about 14.88 × 7.59 × 17.25 | Includes a decorative carriage; shell W is 8.081. 3 m hall spacing may already be suitable. Main Church collider is stale/mismatched. |

The building uniform options use one **door-derived multiplier, 2.10 / 1.4203 ≈ 1.4786**, rather than separate appearance-based guesses. They are alternatives to selective kit-part repair, not recommended automatic changes to all six roots.

## 3. Stage 1 — rig, animation, transport and collider evidence

### Human standing height

Male Idle samples have boot-to-hat height **1.264–1.267 m**. The imported arms-out pose spans **1.462 × 1.265 × 0.274 m**; that width is arm span, not standing shoulder width. Female Idle crown height is **1.1470–1.1474 m**, full visible height **1.1899–1.1909 m**. Her imported pose spans **0.868 × 1.187 × 0.243 m**.

Male renderer bounds were **1.465 × 1.357 × 0.458 m**, female aggregate renderer bounds **0.943 × 1.219 × 0.672 m**. These padded envelopes are neither body widths nor anatomical heights. Horse renderer bounds are even more misleading: **0.491 × 2.492 × 2.301 m**, compared with actual head-inclusive geometry **0.491 × 1.518 × 1.946 m**.

Male root scale is **0.18**. Cowboy_base local scale is **3.8007**, giving renderer transform scale **0.6841**, while skeleton branches remain at 0.18 world scale. That nontrivial skin/rig relationship must be preserved. Scaling only Cowboy_base cannot be assumed to scale stature correctly; both mesh and complete skeleton must remain coherent.

Female root scale is **0.62**, with a -90° X mesh-container orientation and separately rooted skeleton. An adult-scale correction belongs at the existing visual prefab root, which contains both, not at selected clothing renderers or selected bones. An approximate female root scale of **0.892** reproduces the proposed reference; it is a recommendation, not an applied change.

The male mesh combines hat and body, so **1.265 is explicitly boot-to-hat, not an invented bare-head anatomical height**. Isolate scalp/headcap geometry before assigning a precise male production multiplier. A plausible adult correction is of order 1.4–1.5, but this is not a final calibration.

### Walking and root offsets

Walking geometry at the sampled phases spans male H **1.235–1.296**, female H **1.157–1.210**. Walking width and depth vary strongly with limb swing and must not drive the master stature. Lowest sampled vertices reach **-0.0559 m male** and **-0.0403 m female** relative to their actor origin. Idle minima are approximately -0.008 m and -0.005 m. These are pose/root-support offsets, not reasons to independently resize the clips.

The actual AgentMover calibrates animation speed using `Animator.humanScale × abs(animator.transform.lossyScale.y)`. A coherent visual-root resize therefore affects stride calibration and needs route/foot-sliding testing. The game uses its existing custom AgentMover/grid pathing; do not install or substitute NavMeshAgents for this scale audit.

### Navigation-selection collision

Man and Woman production visual prefabs contain no authored body collider. PopulationPathingDirector adds a **trigger selection capsule** to the neutral actor root: radius **0.28**, height **1.80**, centre Y **0.90**. Its world bounds in the isolated runtime reproduction are **0.56 × 1.80 × 0.56 m**. It exceeds the current visible people by roughly 0.54–0.61 m. This is selection volume, not necessarily locomotion collision. Do not resize pathing clearance from this capsule without inspecting the pathing settings independently.

### Horse and withers

The relevant shoulder bones are Chest_3 at Y **0.9182**, front upper-leg attachments at Y **0.8507**, and Neck_4 at Y **1.1085**, all in metres at authored scale. Dorsal body geometry in the shoulder/neck-root Z band **0.10–0.30 m** reaches **1.1975 m**; hair in the same band reaches **1.2066 m**. Hoof geometry reaches -0.0065 m. This establishes a **roughly 1.20 m withers-region estimate**, not an exact veterinary landmark measurement: mane exclusion and precise shoulder-top selection remain approval items.

For the freight draft-horse role, a target withers reference around **1.65 m** suggests approximately **1.37–1.38** uniform enlargement of the complete horse visual rig. Its head envelope would then be about **2.08–2.10 m**, and root scale around **0.96–0.97**. These are conditional estimates, not a resize authorization.

No horse collider was found. RiderMount is at world Y **1.015**, HitchAnchorBack Y **0.560**, and dismount offsets are ±**0.455 m** lateral with Y **0.035**. Compare RiderMount to the back/withers surface and rider-pelvis convention before changing it. No inference that a bone position equals saddle-surface height is valid.

### Wagon and driving fit

- Authored wagon scale **0.6**, full length **4.291**, height **2.371**, transverse width **1.740**.
- Stock body/wheel envelope length **3.072**; added shafts extend the full length. Stock Cart geometry reaches Y **1.180**, canvas Y **2.368**.
- Rear wheel geometry diameter **0.835**, front wheel **0.716**; collider diameters **0.840** and **0.716**. Wheel-axis capsules span **1.800 m**, slightly exceeding visible axle width.
- DriverSeat world point **(0.975, 0.750, 0.2646)**. This is the authored anchor, **not a measured seat-plank surface**. The merged Cart mesh requires face-level selection for that surface. Adult-fit seat/step dimensions cannot be certified from this anchor alone.
- HitchPoint **(0, 0.390, -0.900)** uses a different root axis from the wagon's actual X travel/shaft axis. Existing transport positioning is not determined by this marker alone; do not rotate or move it without checking its consumers.
- CargoRoot/LoadBounds point **(0, 0.510, 0)** is below substantial Cart geometry. It is an anchor point, not a measured usable cargo volume. Validate floor height and load placement separately.
- Main wagon capsule enclosing box **2.580 × 1.536 × 1.536**, centre **(-0.258, 1.614, 0)**; lower BoxCollider **2.880 × 0.852 × 1.530**, centre **(-0.018, 0.834, 0)**. Separate door, wheel and shaft colliders complete the assembly. None of these is a single full-envelope collider.

The inactive decorative DriverSeat/Man child has local scale **0.3**, which under wagon 0.6 gives world scale **0.18**, matching the current male. It is not the authority-owned live driver. Its local offset **(0.217, -0.387, 0)** must not be copied blindly to the runtime actor.

**Critical protection for the newly implemented animation:** PopulationPathingDirector's zero board-site vertical offset does NOT mean the final driver floats. AgentMover.LateUpdate already translates the visual so hips are **0.04 m above DriverSeat**, then aligns ankles to world-metre targets: lateral ±**0.08**, Y **-0.23**, forward **0.33** relative to the seat/actor directions. This existing correction was tested, not modified.

In isolated Play Mode, both characters reached hips **(0.975, 0.790, 0.2646)** with measured target error **0.000000 m** at reporting precision. Both feet reached **(1.305, 0.520, 0.3446)** and **(1.305, 0.520, 0.1846)**. Actor parenting preserved world size: actor local scale became **1.6667** under the 0.6 seat, while visual world scales remained **0.18** and **0.62**. Therefore a blanket 'wagon parenting shrinks the rider' diagnosis would be wrong for the current code.

The saved raw Driving-pose illustration intentionally lacks AgentMover's runtime compensation. Its hips are about **0.413 m above the actor origin** for Man and **0.392 m** for Woman. It is **not** evidence of a current runtime seating defect. The runtime evidence above is the authoritative compensated result.

Horse and wagon are runtime siblings under a neutral transport group; source code preserves their world transforms. Their production separation is **2.3736 m**, matching the authored composite. Increasing horse size may cause overlap with shafts and must include a separation/hitch/contact review. Do not resize the combined root independently.

## 4. Stage 2 — separate physical collider measurements

All dimensions below are normalized world enclosing boxes at authored scale. Runtime or decorative scale overrides multiply them independently.

| Prop | Mesh W × H × D | Collider W × H × D | Finding |
|---|---|---|---|
| Barrel01 | 0.571 × 0.806 × 0.559 | Capsule 0.560 × 0.850 × 0.560 | Collider extends slightly above geometry; reasonable coarse barrel volume. |
| Can02 | 0.824 × 1.290 × 0.824 | Capsule 0.820 × 1.300 × 0.820 | Geometry/collider agree; use-scale problem if carryable. |
| Fence01 | 0.177 × 1.600 × 2.747 | Box 0.177 × 1.600 × 2.747 | Box fills the open spaces between rails; coarse obstacle, not detailed fence collision. |
| HandWaterPump | 0.226 × 1.710 × 0.943 | Box 0.226 × 1.710 × 0.780 | Lever/geometry protrudes beyond collision depth by about 0.164 overall. No explicit grip/spout interaction marker found. |
| LanternHang | 0.305 × 0.591 × 0.396 | Lantern Box 0.405 × 0.564 × 0.415 | Rotated box over-encloses lamp width; total hanger and flame envelope should not define lamp body size. |
| Showel | 0.358 × 1.728 × 0.062 | Box 0.273 × 1.714 × 0.123 | Box misses lateral visual width by about 0.085 and is about twice mesh depth. Blade outline and centre-pivot need refit if interaction becomes physical. |
| WaterTank | 4.239 × 11.658 × 4.377 | Main box 3.220 × 10.998 × 3.252; side/ladder box 0.117 × 10.011 × 1.017 | Main box does not encompass roof and creates solid collision through an open tower footprint. Whether acceptable depends on intended access. |
| WindPump | 10.195 × 26.457 × 12.705 | Box 6.045 × 24.245 × 5.672 | Tower-oriented collider does not cover full rotor/tail sweep; animation sweep and ladder access remain to be measured. |
| Package01 | 0.369 × 0.200 × 0.251 | Box 0.353 × 0.200 × 0.238 | Small mesh protrusions beyond a slightly smaller box; ordinary selection use may be fine. |

A can, lamp or windpump cannot be assigned a unique historically correct size from its name alone. Carry/use classification is the explicit gate before approving their suggested ranges. For pumps, the operating grip at a representative lever angle matters more than the highest vertex. For tall structures, ladder rung spacing, access and capacity matter more than matching a small house's roof line.

## 5. Stage 3 — architecture and clearances

### Entrances and floor geometry

Common single door leaves measure **0.7267 m wide × 1.4203 m high**. Frames have outer geometry about **0.8167 × 1.4673 m**. The frame's outer size is **not** clear opening size. Townhall's front double leaves each measure **0.5722 × 1.4203 m**; combined outer leaf span is about **1.136 m**, still with the same low height.

Ground-floor mesh tops are generally at root-relative **Y 0.400 m**. Common next-floor tops are **Y 2.400 m**, upper ceiling/floor underside **Y 2.275 m**. This yields about **1.875 m geometric headroom** and **2.000 m floor-to-floor spacing** in the common modules. An adult 1.75 m crown with a hat has insufficient comfortable margin, and cannot fit the 1.420 m doors without crouching.

| Building | Representative floor dimensions | Vertical evidence | Entrance evidence |
|---|---|---|---|
| House Natural Wood | Main ground tiles span about 4 × 3 m, excluding porch | Floor top 0.400; upper underside 2.275; top 2.400 | Single leaf 0.727 × 1.420 |
| Single Story White Plaster | Main ground tiles about 4 × 3 m | Same common 1.875 m geometric clearance | Single leaf 0.727 × 1.420 |
| Double Story Green Wood | Ground tiles about 4 × 5 m; upper tiles interrupted by stairs | Tops 0.400, 2.400, 4.400; ceiling underside 2.275/4.275 | Ground and upper leaf heights 1.420 |
| Double Story Natural Wood | Main ground tiles about 4 × 6 m | Ground top 0.400; roof/floor tops at 4.400; intermediate clear storey requires additional face selection | Ground leaf 0.727 × 1.420 |
| Double Story Red Wood | Main ground tiles about 6 × 6 m; exterior porches extend footprint | Ground top 0.400; some upper surfaces are sloped/distorted, not a single flat storey slab | Ground leaf 0.727 × 1.420 |
| Townhall | Main ground tiles about 4 × 8 m; porch/attachments much wider | Ground top 0.400; upper underside 3.275, top 3.400: about 2.875 m geometric hall headroom | Double front leaf span about 1.136, height 1.420; side leaf 0.727 × 1.420 |

These are measured tile envelopes, not certified net usable interior areas. The natural-wood intermediate floor and red-wood sloped floor require explicit plane/face analysis. Common windows measure about **0.559 × 0.765 m**, or two vertically split **0.559 × 0.382 m** leaves; ground window centres about **Y 1.45–1.49 m**, corresponding to sills about **1.07–1.11 m** above the prefab origin, or **0.67–0.71 m above the 0.4 m floor**.

Exterior stair assembly boxes are approximately **0.535 m high × 0.55 m run**, commonly about **1.03 m** or **2.07 m** wide. That 0.535 m includes embedded/underside geometry and is **not one riser height**. Actual tread/riser faces still require selection. Green Wood's interior flights include approximately **1.0 m width**, with individual assembly rises about **1.16 m**. Clearance under rails and stair headroom cannot be certified by those assembly bounds.

The proposed uniform architectural factor would increase ordinary storeys to about **2.96 m**, window modules to about **0.827 × 1.131 m**, and door leaves to about **1.074 × 2.100 m**. This is a coherent door-derived option. It should not be applied to Townhall merely because Townhall is larger; its main hall headroom is already more believable.

### Main collision versus visuals

| Building | Main collider enclosing box W × H × D | Visual envelope | Assessment |
|---|---|---|---|
| House Natural Wood | 4.000 × 3.072 × 4.000 | 4.456 × 4.077 × 4.738 | Coarse shell excludes roof/porch; topology must determine door access. |
| Single Story White Plaster | 4.201 × 2.868 × 4.063 | 4.272 × 3.320 × 4.722 | Coarse envelope, not proof of a clearance error by itself. |
| Double Story Green Wood | 4.147 × 4.688 × 6.258 | 4.291 × 5.263 × 6.939 | Porch/roof excluded. Duplicate stair MeshColliders in both LOD0 and LOD1 are active. |
| Double Story Natural Wood | 6.495 × 4.922 × 9.150 | 4.473 × 6.098 × 8.240 | Major mismatch: collision is wider/deeper but lower than present kitbashed visual. Do not cure by uniform scaling. |
| Double Story Red Wood | 7.760 × 4.432 × 7.601 | 8.270 × 5.870 × 8.345 | Coarse shell versus larger roof/porches; detail access needs checking. |
| Townhall | 4.461 × 7.452 × 9.663 | Shell 8.081 × 5.136 × 11.663; decorated W 10.062 | Main Church collider extends about 2.3 m above current visual envelope yet omits substantial side attachments. Reconcile to actual architecture. |

The collider differences are not automatically wrong for inaccessible shells. They are important uncertainty flags wherever actors approach, operate doors, pass under roofs or climb stairs. Visual and collider vertex sets must agree on walkable/blocked regions before architecture changes are certified.

## 6. Hierarchy, pivots, import units and placed state

### Import scales

All human and horse model importers use globalScale **1**, fileScale **0.01**, useFileScale enabled. Most Western wagon, barrel, can, fence, lamp, tower and building FBXs use fileScale **0.0254**: inch-to-metre conversion. HandWaterPump and Package01 use **0.01**. Added wagon bridge beams use **0.001**. Showel alone has importer globalScale **0.5**, in addition to fileScale **0.0254**.

Different fileScale values are not evidence of inconsistent world units; they often mean different source authoring units. **Do not multiply the measured world dimensions by fileScale again**, and do not change correct inch conversion to 1 in an attempt to standardize Inspector values.

### Root and nested transforms

- Human 0.18/0.62, horse 0.7, wagon 0.6 and building 0.5 roots are explicit authored multipliers. All are uniform. Their numerical differences are not themselves the defect; measured anatomy and clearances establish the defect.
- Cowboy's 3.8007 renderer-child scale versus separately rooted skeleton is the highest-risk nested scale convention.
- Wagon shafts have local scale **(1.0076, 1.1667, 1.1667)**. Preserve intended shaft dimensions and attached colliders; do not flatten them to 1.
- Buildings have deliberate nonuniform kit-part scales: stretched pillars, shortened porch pieces, reduced roofs and stair details. Examples include White/Green pillars with a 0.4145 axis, Natural/Red pillars with a 2.0 or greater axis, and Red roofs with 0.5462/0.4204 on one axis. These affect measurement, but many are intentional kitbashing rather than errors.
- Natural Wood's Props parent has scale **(-1, 1, 1)**. Mirroring must be carried through collision/pivot analysis. Do not replace it without checking intended handedness.
- Green Wood retains LOD0 and LOD1 stair collider copies. LOD render switching does not automatically deactivate those colliders.

### Pivot and support offsets

Barrel, can and towers use base pivots and -90° X import rotations. Fence is near an end post, extending Z **-0.110 to +2.637 m**. Showel is centre-pivoted with Y **-0.864 to +0.864 m**. LanternHang is a suspension pivot with physical geometry down to **-0.591 m**. Package bottom is approximately **-0.0027 m**. Buildings contain embedded stair/foundation geometry below the root by approximately **0.08–0.14 m**, with usable floor tops around +0.4 m.

Several standalone prefab roots also retain source-library world positions, and Horse and Wagon's root is authored at **(-69.634, 0.624, -8.479)**. These translations do not change dimensions but are placement hazards when using Instantiate(prefab,parent) without resetting local position. PopulationPathingDirector explicitly resets human Visual localPosition to zero; transport placement supplies explicit world position/rotation. Do not 'fix' root translations globally during this audit.

### Loaded scene and spawned scale

The 18 matching prefab-root instances in Main's `Prefab Environment` library are under an **inactive hierarchy**. Their zero active-renderer measurements in the initial placed-instance log mean **hidden library instances**, not zero-size assets. They do not represent the live generated town.

That library contains Barrel01 local scale **1.5**, a mirrored commercial Barrel01 **(-1.5, 1.5, 1.5)**, and LanternHang **1.5**, in addition to standard roots. Those are different physical sizes from their standalone prefabs. In building prefabs, nested Barrel01 commonly has effective scale **0.75** and therefore height about **0.604 m**, with capsule height **0.638 m**. Can01 decorative copies commonly inherit building scale 0.5; this observation is not a measurement of Can02. Avoid conflating different named variants.

The isolated runtime reproduction confirmed the actual human prefab scale under a neutral actor root and world-scale-preserving wagon-seat parenting. Production startup code preserves Horse/Wagon root scale and uses a neutral sibling group. **A live Main-scene spawn/save-load cycle was not run**, so generated town buildings, runtime exterior prop spawning, existing save overrides and all terrain-specific instance placements remain an explicit follow-up inventory rather than a claimed complete runtime census.

Footprint-authority corner rectangles at authored scale are approximately: House **4.844 × 4.735**, White **4.534 × 5.089**, Green **4.610 × 7.212**, Natural **5.108 × 8.706**, Red **8.821 × 8.639**, Townhall **12.391 × 12.740 m**. These are gameplay claim rectangles, not mesh dimensions. The authority stores local corner and road-access positions and derives grid claims; the default authoring cell size is 2 m. A building correction must reconcile these corners, named anchors and BuildingDefinition claims, not only its renderers.

## 7. Prioritized correction recommendations — approval required

### Ordered work, humans outward

1. **Establish and approve human stature.** Confirm the boot-sole/crown convention and 1.75/1.65 references. Isolate male headcap from the fused hat before computing an exact multiplier. Apply only to the complete visual/rig root, leaving the neutral identity/path actor unchanged. Recheck selection capsule, animation stride, foot support, clothing and all existing adults. Highest visual-consistency impact; do not calibrate props to the current miniature people.
2. **Review horse, wagon and driver as a unit.** Confirm opening horse breed/withers landmark, then adult driver seat-plank/pelvis fit, step positions, knee clearance, shaft spacing and horse separation. Preserve the existing Driving clip, AgentMover pelvis correction and ankle IK, live driver identity and neutral transport authority. Existing compensation already works at the current scale; any future larger driver must be tested for reach/clamping.
3. **Review interactive/carryable props.** Can02 and Showel first; HandWaterPump grip/spout and Lantern role next. Keep Barrel01, Fence01 and Package01 as provisional correctly sized standards. Use the same reference person and metres, not independent eyeballing. Refit colliders where visual outlines change and validate operating contact points.
4. **Review tall functional structures.** Retain WaterTank pending capacity/ladder checks. Decide whether WindPump is a ranch structure or a deliberate industrial landmark before proposing a resize. Validate rotor sweep and tower access separately from the master-envelope change.
5. **Architecture last.** After adults and interaction scale are fixed, prioritize doorway/headroom agreement. First resolve Natural Wood/Townhall collision mismatches and Green duplicate stair collision. Choose selective kit-module changes versus the one door-derived uniform factor. Reconcile floor/stair geometry, anchors, foundations, road frontage and footprint authority. Different large building footprints remain legitimate.
6. **Validate migration before applying broadly.** Test at least one of each affected prefab on representative terrain and an existing save. Check pedestrian routes, doorway approach/open/close, boarding/dismount, freight departure/return, cargo placement, selection, mirrored variants and already placed objects. Only then apply approved per-prefab changes with an explicit placed-instance/save strategy.

### Safe change layer and blast radius

| Change approach | Suitable use | Risk / required checks |
|---|---|---|
| Complete human visual-root scale | Preferred candidate for adult stature; includes all bones/skin branches | Keep neutral actor root and authority unchanged; validate stride, support plane, clothing, selection and seated leg reach. Never scale one renderer independently of its skeleton. |
| Static prop visual child | Suitable only when a true geometry-only child exists | Most named props are root MeshFilter/Renderer + collider assemblies, so they are NOT immediately safe visual-child-only edits. A visual-only change would leave collision and contacts stale. |
| Complete horse visual rig | Candidate for breed/withers correction | RiderMount/hitch/dismount anchors are sibling gameplay landmarks; visual-only scale leaves them at old positions. Coordinate approved anchor changes; preserve authority root and identifiers. |
| Wagon geometry children | Potentially localized seat/plank/shaft work | Root anchors, wheel/shaft colliders, cargo and current runtime ankle targets must be revalidated together. Do not globally scale the wagon as a proxy for driver fit. |
| Model importer scale | Only a demonstrated source-unit error across all consumers | Highest shared-asset blast radius; affects all FBX meshes, avatars, bindposes, embedded animation and colliders referencing that model. The audited 0.01/0.0254/0.001 file-unit conversions are not demonstrated errors. |
| Building visual children / individual modules | Selective door/storey repair while retaining chosen footprint | Refit door collision, animation pivots, frames/windows/stairs/ceilings and markers. Roof and floor kitbashing prevents a single universal child-scale shortcut. |
| Building prefab root scale | Conditional coherent architectural enlargement | Affects every child, exterior prop, anchor, collider and gameplay corner; grid claim and terrain seating must be recomputed. Existing world objects and saved placements need migration. Never use a blanket global factor. |

### Explicit gameplay and persistence warnings

- **Animation:** preserve avatars, bindposes and the new Driving animation. Enlarging rig-only children can deform meshes; changing importer scales can alter retargeted root translations. Selection/renderer bounds must not drive stature.
- **Navigation:** custom grid pathing, door/road-access anchors and movement speeds are separate from mesh size. Re-evaluate world-metre clearance and frontage after approved changes; do not replace controllers.
- **Collision:** refit actual solids and motion sweeps, not only enclosing boxes. Duplicate LOD colliders remain active unless explicitly addressed. Mirrored/negative scales deserve independent validation.
- **Wagon seating:** preserve world-size parenting, 0.04 m pelvis clearance and current world-metre foot targets. Larger legs may need different approved targets or step geometry; test reach and knee clearance rather than rewriting animation.
- **Save/load:** this audit did not establish that every placed override is serialized identically. Prefab edits can alter already spawned geometry on reconstruction while saved origins, grid claims, authority IDs or instance-scale overrides retain older assumptions. Inspect persistence formats before correction; preserve person/animal/equipment/building IDs.
- **Already placed objects:** nested decorative objects inherit both parent and local overrides; do not assume standalone scale equals placed scale. Architecture changes may require foundation, road/frontage, neighbour spacing and collision migration. No terrain changes are part of this audit.
- **Economy and transport authority:** sizes are presentation/physical-geometry work. Do not rewrite economic identity, shipment, reservation, transport ownership or journey state to make a visual correction.

## 8. Screenshots and supporting artifacts

The static comparison scene is `Assets/Audits/MasterScale/Comparison.unity`, separate from Main. It contains measured posed visual snapshots and metre rulers, **no gameplay authority components**. It is intentionally suspended above the main world to avoid terrain overlap. Absolute comparison-scene placement is not a gameplay placement recommendation.

- [Transport baseline](TransportComparison.png): authored standing Man, Woman, measured 1.65 m crown reference, horse, wagon and raw Driving pose. The raw pose lacks runtime correction; see RuntimeVerification.txt for compensated seating.
- [Small props baseline](PropComparison.png): reference person, barrel, can, fence viewed end-on, pump, lamp, shovel and package. Physical dimensions are in the table even when a chosen angle hides panel length.
- [Tall functional structures](TallComparison.png): reference person, 5 m ruler, WaterTank and WindPump at authored physical scale. All share one camera/world scale.
- Individual `Building0Front.png` through `Building5Front.png` and `BuildingFrontPanels.png` are exploratory frontage captures. Foreground occlusion/render-cache artifacts limit some panels; **do not use their pixels as dimensional evidence or rely on an apparent duplicate person as prefab content**. The vertex and threshold measurements above are authoritative. Further corrected entrance captures are provided where available.

Supporting files:

- `VerifiedMeasurements.tsv`: normalized authored geometry, including import/bind-pose human widths and root scales.
- `VerifiedGeometry.txt`: per-renderer vertex boxes, collider bounds, landmark offsets and nonuniform transforms.
- `HierarchyAudit.txt`: complete component/hierarchy inventory, renderer bounds, import settings and clips from the initial pass. Rotations/skin dimensions in that initial pass are not the final physical table.
- `PoseMeasurements.txt`: all 24 standing/walking/driving pose samples.
- `HorseLandmarks.txt`: shoulder/neck/front-leg bones and centreline surface-height sampling.
- `AnchorsAndImports.txt`: serialized seat/hitch/footprint data and every encountered model import scale.
- `PlacedInstances.txt`: inactive Main prefab-library roots and authored scale overrides; zero active-renderer boxes are not zero physical size.
- `RuntimeVerification.txt`: successful isolated runtime spawning, selection and compensated driver pelvis/foot results.
- `AuthoredMeasurements.tsv`: **superseded exploratory measurement**. Do not use for corrections; it removed import-axis rotation and used a scaled BakeMesh convention that was independently corrected.

## 9. Verification status and remaining gates

**Verified:** all 20 requested prefabs resolved; full hierarchy/components inspected; mesh vertices and colliders measured separately; mixed source-unit scales distinguished; skin geometry independently cross-checked; 24 pose samples evaluated; static metre-reference setup built; isolated Play Mode human scale/selection/seat compensation passed; no console errors were returned at the runtime check; editor returned to Edit Mode and Main's prior unsaved state was preserved.

**Not certified yet:** exact male scalp crown under the fused hat; exact veterinary withers landmark and breed; wagon seat-plank face height and true usable cargo volume; pump grip/spout operating heights; intended can/lamp/windpump role; actual clear door-opening faces, stair treads/risers, all headroom planes; live generated Main town dimensions, terrain contacts and existing-save reconstruction; full navigation/freight journey regression after any future correction. These gates prevent an unearned claim that every correction is safe or that all dimensions have a unique real-world target.

**Decision:** approve the master human-height convention and measurements first. The only immediately proposed numeric human correction with a directly separable crown/support measurement is the female complete-visual-root calibration. All production corrections remain unapplied pending approval and the relevant gates above.
