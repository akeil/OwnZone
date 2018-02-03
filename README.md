# OwnZone
This is a small tool to convert location messages from
an [OwnTracks](http://owntracks.org/) app to named "zones".

*OwnZone* runs as a service in the background and connects
to an [MQTT](https://mqtt.org/) broker where it subscribes to
messages with location info (lat/lon coordinates)
and publishes messages with corresponding zone names.

## Configuration
OZ needs a configuration file with the following contents:

```

```

## Zones
*OwnZone* supports the following types of zone definitions:
- Point with radius
- Bounding Box

