# Map health check (web page)

Turns a map description into one self-contained HTML page: a flat picture of the map (shade from the heights, ground
colour from the surface names, steep cells hatched, plates, walls, points, bases, units) and a list of the problems the
gate finds, in Chinese. Every problem that has a place also has a pin on the picture; clicking a pin or a list row
highlights the other. Nothing is loaded from the network.

```sh
dotnet run -c Release --project tools/mapcheck -- Assets/WarCore/Maps/plate-demo.map.json out/plate-demo.html
```

`mapcheck <map.json> <out.html>`. The page is always written. Exit code 0: the gate found no problem. 1: problems
(listed in the page). 2: bad arguments or a file that cannot be read. The binary finds the project root itself.

## Inputs

- The description is read by the same reader and checked by the same gate as the game (`MapGate.Check`), so the problems
  are the gate's own, in its order.
- Colours come from `palette.json` of the cartridge that holds the description's content layer. A surface name the palette
  does not list gets a fixed grey-ish colour computed from the name, and the page says so in a note.
- The words come from `Assets/WarCore/Content/MapTools/Text/map-messages.zh.txt`, one line per problem code (a test
  keeps the table equal to the set of codes the sources can raise).

The page data is `MapPreview.Build` followed by `MapPreview.ToJson`: the same bytes for the same input.
