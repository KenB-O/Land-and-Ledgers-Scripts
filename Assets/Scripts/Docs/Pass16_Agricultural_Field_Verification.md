# Pass XVI — Agricultural field verification

## Scope

Pass XV added typed agricultural premises selection and weekly scene-reference
refreshing. The intended chain is CropFarm/Ranch formation → compatible
agricultural premises → stable field registration.

## Verification status

The source correction is present at the Pass XVI starting commit, but it was
not verifiable in Unity during this pass. The minimal Unity runner stalled in
IL post-processing before the farm PlayMode test could execute.

Consequently there is no fresh Unity evidence for Farm ID, premises ID,
building ID, agricultural plot ID, field ID, field area, rights holder,
duplicate registration, or winter state.

No field, crop inventory, harvest or freight shipment was injected. The next
test must first prove the opening farm has a legitimate compatible site and a
stable CropFieldState after repeated binding and restore.

