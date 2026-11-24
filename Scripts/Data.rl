// Passenger Classes 0.1.0: the first-class twin of every stock passenger car and the sleeper twin of the Palace Car, carried over from First Class
// under the same ids so saves with those cars keep them, made with modifyAs (as Locomotive Gearing and Aux Fuel Cars clone theirs). A clone is filed under the pack that owns its base, so the model, trucks and paint resolve as the stock
// car's; Info.json requires the three base packs because Data.rl only sees definitions from packs ordered before it.
//
// What a clone sets: its name ("<name> (First Class)" or "(Sleeper)"), its listed price (the base's, 20% or 40% more, to the nearest
// $10), its seats (70% of the base's, rounded down, at least one; the passenger slot only) and its builderPhoto (the buy window
// looks for "<id>--bp0", which no clone has, so it points at the base car's). Everything else, the car type included, is the stock
// car's, so the game still treats it as that kind of car (an observation car stays an observation car). The stock cars stay on sale. The price the player pays follows the dll's setting (src/Pricing.cs);
// the listed price is what the game uses when the dll isn't running.
//
// Id scheme: ashenmare/fc-<the base id after its pack's "rr/">, e.g. ashenmare/fc-pb-pullman-lw-wood-19xx, and ashenmare/sl- for the
// sleeper. The dll knows the class of a car by that prefix. The list below must match src/Logic.cs (tests/ checks it).
//
// Data.rl cannot list definitions, so another pack's coaches are in this list by id, or not at all. A row whose base is not
// loaded is skipped (modifyAs throws before it runs the closure; that throw is caught here, since the public calls cannot ask whether a definition exists,
// and an error inside the closure is thrown again), so the list may name packs a player doesn't have.
//
// Paint: a CarStyle applies to a car only when it names the car's id (CarStyleSelector.StyleAvailable). The Osgood-Bradley coach has
// eight stock styles, each naming the base id, so each also names the clone; the others have none.

import log

// [base id, clone id, [the base's style ids], the percent over the base's listed price, the percent of its seats, the name's tail]
let cars = [
    ["rr/pb-pullman-lw-wood-19xx", "ashenmare/fc-pb-pullman-lw-wood-19xx", [], 120, 70, " (First Class)"],
    ["rr/pb-osgbrad-lightweight-steel-1915", "ashenmare/fc-pb-osgbrad-lightweight-steel-1915", ["2tone", "3tone", "prr", "nyc", "bo", "rio", "sou", "gn"], 120, 70, " (First Class)"],
    ["rr/pb-pullman-heavyweight-early", "ashenmare/fc-pb-pullman-heavyweight-early", [], 120, 70, " (First Class)"],
    ["rr/pbo-pullman-heavyweight-early", "ashenmare/fc-pbo-pullman-heavyweight-early", [], 120, 70, " (First Class)"],
    ["rr/pb-pullman-heavyweight-early", "ashenmare/sl-pb-pullman-heavyweight-early", [], 140, 70, " (Sleeper)"]
]

func modifyDefinitions(context) {
    for c in cars {
        let baseId = c[0]
        let cloneId = c[1]
        var ok = true
        var ran = false
        try {
            context.modifyAs(baseId, cloneId, (obj) => {
                ran = true
                obj["metadata"]["name"] = obj["metadata"]["name"] + c[5]
                let listed = obj["definition"]["basePrice"]
                obj["definition"]["basePrice"] = ((listed * c[3]) / 1000).round() * 10
                for slot in obj["definition"]["loadSlots"] {
                    if slot["requiredLoadIdentifier"] == "rr/passengers" {
                        var seats = ((slot["maximumCapacity"] * c[4]) / 100).floor()
                        if seats < 1 { seats = 1 }
                        slot["maximumCapacity"] = seats
                    }
                }
                obj["definition"]["builderPhoto"] = baseId + "--bp0"
            })
        } catch err {
            if ran { throw err }
            ok = false
            log.warning("[passenger-classes] \(baseId) not loaded, skipped: \(err)")
        }
        if ok {
            for tail in c[2] {
                let styleId = baseId + "-" + tail
                var styleRan = false
                try {
                    context.modify(styleId, (obj) => {
                        styleRan = true
                        var names = []
                        for old in obj["definition"]["carIdentifiers"] { names.add(old) }
                        names.add(cloneId)
                        obj["definition"]["carIdentifiers"] = names
                    })
                } catch err {
                    if styleRan { throw err }
                    log.warning("[passenger-classes] style \(styleId) not changed: \(err)")
                }
            }
        }
    }
}
