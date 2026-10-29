# Pass XVI — Crop task integration boundary

Planting, tending and harvesting Task connections were not implemented in this
pass. The Unity verification environment failed before test discovery, so an
additional production path would have been untestable.

Required next work, once the runner is reliable:

* create stage Tasks only for eligible fields, seasons, inputs, equipment and workers;
* reserve Person time and equipment through existing authorities;
* advance CropFieldState only from completed work;
* create harvest output once in authoritative farm inventory;
* preserve field/task/output identity across reload and driver rebind;
* connect output to real demand before testing freight.

Winter must remain a legitimate no-harvest result unless a saved field is
already mature. A farm being staffed is not evidence that crop work occurred.

