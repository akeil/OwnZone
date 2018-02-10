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

## MQTT Topics
We rely on the [OwnTracks topic structure](http://owntracks.org/booklet/guide/topics/)
which constructs topics like this:
```
<prefix>/<user>/<device>
```

The `prefix` must be set in `appsettings` under `Engine.TopicPrefixIn`.
OwnZone will derive the username from the incoming OwnTracks messages
and publish to the following topics:
```
<prefix>/<user>/current
<prefix>/<user>/status/<zone>
```
The prefix for outgoing messages is defined in `appsettings` under 
`Engine.TopicPrefixOut`.

## Zones
*OwnZone* supports the following types of zone definitions:
- Point with radius
- Bounding Box

Zones are defined in [GeoJSON](http://geojson.org) format
as a *FeatureCollection*, each zone is represented by a *Feature*.

The *FeatureCollection* must have a default property `topic`
which denotes the MQTT topic to subscribe for location updates.

The zones are described as individual *Features*. The features
require some additonal properties to describe the zone.

- Point
  with mandatory properties `radius` (*int*) and `name` (*string*).
- LineString
  with mandatory properties `padding` (*int*) and `name` (*string*).

```json
{
    "type": "FeatureCollection",
    "features": [
        {
            "type": "Feature",
            "geometry": {
                "type": "Point",
                "coordinates": [100.0, 50.0]
            },
            "properties": {
                "name": "my zone",
                "radius": 50
            }
        },
        {
            "type": "Feature",
            "geometry": {
                "type": "LineString",
                "coordinates": [
                    [100.0, 50.0],
                    [101.0, 51.0],
                    [102.0, 52.0]
                ]
            },
            "properties": {
                "name": "commute",
                "padding": 20
            }
        }
    ]
}
```

## Filters
Location updates can be filtered by the following criteria:

- **Accuracy**
  If an update has an accuracy rating, drop updates with low accuracy.
  Accuracy is specified in meters.
- **Age**
  Ignore location updates which are too old (e.g. messages that took a long
  time in transport). The message must carry a UTC timestamp for thius filter
  to work.

Filters are part of the `appsettings.json`:
```json
    "Filters": {
        "MaxAge": "1:30:00",
        "MaxAccuracy": 25
    }
```
