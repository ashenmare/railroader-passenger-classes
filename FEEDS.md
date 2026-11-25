Passenger Classes 0.3.0 feeds (passenger-classes api 1, first-class api 1)

Two read-only feeds, for other mods and Railroad Books. Plain data (dictionaries, lists and numbers), main thread, changes nothing, a new
answer every call (except `today` and `yesterday`: read-only objects that are handed out again until a figure changes; do not change them). Read them through Mod Feeds (ModFeeds.Read("passenger-classes")) or by reflection on PassengerClasses.Api.Read() and
PassengerClasses.FirstClassFeed.Api.Read(). Within api 1 keys are only added. With no world loaded the lists are empty. A failed read answers
{ api, feed, error } and nothing else.

Passenger Classes reads one feed itself: Realistic Steam and Fuel Use's "realistic-fuel-use" (passengerHeat, passengerCars). In each car row it reads id, heated, outsideF, type and, when the row has it, `insideF`
(a finite number: the car's own temperature in F); with `insideF` the heat fare slides with it, without it (an older answer, or a value that is not a number) the heated or unheated rule applies. When the row also has `comfortSeconds` and `comfortAt` (the car's comfort integral in seconds, and the game second it is as of; both finite numbers) the fare is the AVERAGE comfort over each rider's ride: the integral is read when riders board and again when they get off, and the change over the time between is their average (-1 to +1); without them the temperature at the stop is used.

"passenger-classes" is the feed to read. "first-class" is the feed the mod First Class published, kept with the same ids and keys so readers
written for that mod (such as the Industry Inspector's Passengers tab) keep working after First Class is removed. Its values are the same
data as the new feed's; it declares First Class's four car columns again (First class, First class seats used, Sleeper, Sleeper berths used; same ids), and no station columns: the station values are the new feed's columns alone.

The passenger-classes feed declares twelve Mod Feeds columns, so a reader that shows things from other mods shows them with no code for this one:
- car "Passenger class" (text), cars[].class, by car id
- car "Observation car" (bool), cars[].observation, by car id
- car "Seats" (number), cars[].seats, by car id (passenger cars only)
- car "Seats used" (number), cars[].seatsUsed, by car id (passenger cars only)
- station "First-class riders waiting" (number), stations[].firstClassWaiting, by industry id
- station "First class, no room" (number), stations[].firstClassNoRoom, by industry id
- station "Sleeper riders waiting" (number), stations[].sleeperWaiting, by industry id
- station "Sleeper, no berth" (number), stations[].sleeperNoBerth, by industry id
- car "Cold-weather fare" (text), cars[].heatEffect, by car id (only while it moves the fare)
- car "Baggage aboard" and "Baggage room" (number, lb), cars[].baggageUsedLb and baggageRoomLb, by car id (baggage on only)
- station "Held by baggage" (number), stations[].heldByBaggage, by industry id (baggage on only)

## passenger-classes: top-level keys

| Key | Type | Meaning |
|---|---|---|
| `api` | int | 1 |
| `feed` | string | "passenger-classes" |
| `settings` | object | the settings in force, below |
| `classes` | list of strings | the class words a car can have: coach, parlor, sleeper, lounge, baggage |
| `cars` | list | every passenger car and every car with a class (baggage cars), below |
| `stations` | list | every station, below |
| `today` | object | the game day so far, from the host's own boarding and fares (the day book, below) |
| `yesterday` | object | the game day before, the same shape |
| `error` | string | only when the feed failed: then it is all there is besides `api` and `feed` |

## passenger-classes: settings

| Key | Type | Meaning |
|---|---|---|
| `sharePercent` | int | the share of each crowd that wants first class, percent |
| `farePercent` | int | a first-class rider's fare as a percent of the coach fare |
| `pricePercent` | int | a first-class twin's price as a percent of the stock car's |
| `sleeperSharePercent` | int | the share of each crowd that wants a sleeper, percent |
| `sleeperFarePercent` | int | a sleeper rider's fare as a percent of the coach fare |
| `sleeperPricePercent` | int | a sleeper twin's price as a percent of the stock car's |
| `observationPercent` | int | the fare bonus for the whole train with an observation car at its end, as a percent (120 is the game's own) |
| `sleeperMiles` | int | the shortest trip with sleeper riders, in miles |
| `convertParlorPercent` | int | the share of its seats a coach keeps when the player makes it a parlor, percent |
| `convertSleeperPercent` | int | the same for a sleeper (berths), percent |
| `steamHeat` | bool | the Steam heat setting: the whole heat feature (the Heat line and the cold-weather fare); added in 0.2.0 |
| `fareLine` | bool | the Fare line in the car window, on or off; added in 0.2.0 |
| `payBreakdown` | bool | the breakdown line a stop's payment gets, on or off; added in 0.2.0 |
| `heatFarePercent` | int | the cold-weather fare effect as a percent of its set size (100 as set, 0 off); added in 0.2.0 |
| `fareBase` | int | the coach fare up to 2 miles, dollars (1 as the game's); added in 0.3.0 |
| `farePerMile` | number | the coach fare's rise for each mile past 2, dollars (7/48, about 0.1458, as the game's); added in 0.3.0 |
| `fareCap` | int or null | the most a coach fare is, dollars (8 as the game's); null for Unlimited; added in 0.3.0 |
| `readPackFiles` | bool | whether a pack's PassengerClasses.json is read |
| `loungePatience` | bool | the lounge rule (transfer riders off a train with a lounge car wait longer); off by default |
| `loungeHours` | int | the extra hours of patience |
| `baggageRiders` | bool | the baggage rule; off by default |
| `bagNearCoachLb` | int | pounds of bags a coach rider brings on a $3 to $5 fare |
| `bagNearFirstLb` | int | the same for a first-class rider |
| `bagNearSleeperLb` | int | the same for a sleeper rider |
| `bagFarPercent` | int | the figure on a $6 to $8 fare, as a percent of the $3 to $5 one |
| `bagExcessPercent` | int | the share of riders who bring double their allowance, percent |
| `bagFeePer100` | int | the excess fee, dollars per 100 lb |
| `bagRackLb` | int | pounds of its riders' bags each coach seat's rack holds (a lounge car too) |
| `bagRackParlorLb` | int | the same for a parlor seat |
| `bagRackSleeperLb` | int | the same for a sleeper berth |
| `bagLuggageLb` | int | the luggage area of each car with seats, in pounds |

## passenger-classes: cars

| Key | Type | Meaning |
|---|---|---|
| `id` | string | the car's id |
| `definition` | string | the car's definition id |
| `class` | string | the car model's class word |
| `classSource` | string | where the class came from: default, built-in, pack file or player |
| `observation` | bool | whether the model is an observation car (the whole-train bonus) |
| `seats` | int | the car's seats (passenger cars only) |
| `seatsUsed` | int | riders aboard (passenger cars only) |
| `baggageLb` | int | pounds the car's baggage compartment holds, when the built-in table or a pack's file gives a figure (the stock baggage car: 40,000) |
| `baggageUsedLb` | int | with baggage on, pounds of bags aboard (normal and excess, from the riders aboard at the last service; passenger and baggage cars only) |
| `baggageRoomLb` | int | with baggage on, what the car's racks (seats x its class's rack figure), its luggage area and its baggage compartment hold; `baggageCompartmentLb` is the compartment alone |
| `baggageCompartmentLb` | int | with baggage on, the pounds of the car's baggage compartment alone: a baggage car's or a combine's baggage end, 0 for a seated car without one (passenger and baggage cars only) |
| `heatEffectPercent` | number | passenger cars, only while Realistic Steam and Fuel Use's passenger heat is on and the cold-weather fare moves this car's fare: the change as a signed percent of the fare now (-25 for an unheated coach in 20 F air, +4 for a heated one; with an inside temperature in the other mod's row, any figure between); added in 0.2.0 |
| `heatEffect` | string | the same in words, `"cold car, fares -25%"` or `"heated, +4%"`; absent when the effect rounds to 0. Without Realistic Steam and Fuel Use's heat (not installed, its heat off or not readable) it is `"Heated"` in the cold months (November to March) and no fare moves; absent with Steam heat off |
| `firstClassSeatsUsed` | int | riders aboard a parlor car (parlor cars only) |
| `sleeperBerthsUsed` | int | riders aboard a sleeper car (sleeper cars only) |
| `noRoomSinceArrival` | int | parlor cars: first-class riders who found no first-class room and rode coach, since this car last pulled into a station; sleeper cars: sleeper riders who found no berth, in the same time (parlor and sleeper cars only) |

## passenger-classes: stations

| Key | Type | Meaning |
|---|---|---|
| `id` | string | the station's own id |
| `industryId` | string | the industry id (what other mods key stations by) |
| `name` | string | the station's name |
| `waiting` | int | people waiting, all destinations |
| `firstClassWaiting` | int | how many of them want first class: the host's own count; a client's is its own estimate (the count is not saved or sent) |
| `sleeperWaiting` | int | how many of them want a sleeper (trips of the setting's distance or more), counted the same way |
| `firstClassNoRoom` | int | first-class riders who found no first-class room and rode coach, since the last train pulled in; counted only while a parlor car is at the station |
| `sleeperNoBerth` | int | sleeper riders who found no berth and rode first class or coach, since the last train pulled in; counted only while a sleeper car is at the station |
| `heldByBaggage` | int | with baggage on, riders left waiting because their bags did not fit the train's baggage room (as of the last boarding; cleared when a train pulls in) |

## passenger-classes: the day book (today and yesterday)

`today` and `yesterday` have the same shape, one game day each. A day is the game clock's day (seconds divided by 86,400, rolled at midnight); `day` is the game's day worked out from the clock's seconds ("Day 4" is 4; within a few seconds of the number the game's own clock shows). `today` and `yesterday` are built again only when a figure changes or the day does, and until then every read gets the same objects: treat them as read-only. The book is kept in memory on the host, bounded, and nothing is saved. After a world load it starts counting at the first figure (or the first read) and `today.partial` says so; `yesterday.known` is false until a midnight has passed since then. On a client both days have `known` false and nothing in them. A read never rolls the day: after midnight and before the first new figure, `yesterday` is the day that ended and `today` is empty.

| Key | Type | Meaning |
|---|---|---|
| `day` | int | the game's day, worked out from the clock's seconds |
| `known` | bool | false when this computer has no figures for the day (a day before the load, a client, or the book stood down) |
| `partial` | bool | true when counting began after the day began (a load), so the day may be short |
| `unavailable` | bool | true when the day book is off on this computer: the game version lacks something it needs, or it stood down after repeated errors (then `known` is false and the figures are empty); boarding and fares are not affected |
| `total` | object | the whole railroad's figures for the day: the group below, exact whatever the row limits |
| `stations` | list | one row per station with figures: `id`, `name` and the group below; at most 128 a day |
| `trains` | list | one row per train: `id` (the lead engine's car id) and `name` (its road mark and number) and the group below without `paid`; at most 64 a day |
| `stationsFull` | bool | the day passed the station row limit; `total` is still whole |
| `trainsFull` | bool | the day passed the train row limit |

A train is the cars coupled together at that moment and its lead engine is the first locomotive from the A end of the coupled cars. A train with no locomotive has no row (its riders and fares are in `total` and the station's row). A train that changes engines is a new row. Station figures belong to the station where the rider boarded (`boarded`, `left`) or got off (the fares and `paid`).

The group, in `total`, in every station row and (without `paid`) in every train row:

| Key | Type | Meaning |
|---|---|---|
| `boarded` | object | riders who boarded, by the class of the car: `coach`, `first`, `sleeper` (ints; `first` is a parlor car). The game's own count of what each car received, counted per boarding leg at the boarding station: a rider who changes trains counts at each boarding (`left` is counted the same way). The fares and `paid` follow the game's ledger: once, at the arrival station, by the last car's class |
| `gameFare` | object | dollars: the game's own fare for the riders who got off, by the class of the car they rode, `coach`, `first`, `sleeper` (any observation-car bonus is already in it; it is not counted separately; from 0.3.0 it is the fare the coach fare settings give) |
| `extra` | object | dollars: the extra this mod added, `first` and `sleeper` |
| `heatFare` | object | dollars: what the cold-weather fare added, by class (`coach`, `first`, `sleeper`); negative for unheated cars in the cold, zero without Realistic Steam and Fuel Use's passenger heat; added in 0.2.0 |
| `bagFees` | number | dollars: excess-baggage fees accrued (with baggage on) |
| `paid` | number | dollars the game paid the station on the day it paid (its payment comes about 5 seconds after the last rider got off, so a fare worked out just before midnight can be paid in the next day). The game rounds each payment up to whole dollars, so this is the ledger's figure and the parts above are the unrounded sums; they differ by less than a dollar a payment. Not in a train row |
| `left` | object | riders counted, by reason: `noRoom` (wanted first class and rode a lower class: the first-class car had no seat), `noBerth` (wanted a sleeper and rode a lower class), `heldForBags` (left waiting because their bags did not fit the train), `gaveUp` (expired from a platform) |
| `bands` | object | the same four counts split by the fare band of the trip, each a list of 9 ints: index 0 a trip the game cannot measure, 1 to 8 the dollar fare the game charges (the distance fare before any bonus) |

Keys inside `boarded`, `gameFare`, `extra` and `heatFare` (a class), and inside `left` and `bands` (a reason):

| Key | Type | Meaning |
|---|---|---|
| `coach` | int or number | the class of the car the riders sat in: a coach (or a lounge car); not in `extra` |
| `first` | int or number | a parlor car (first class) |
| `sleeper` | int or number | a sleeper car |
| `noRoom` | int or list | first-class riders who rode a lower class (a list of 9 in `bands`) |
| `noBerth` | int or list | sleeper riders who rode a lower class |
| `heldForBags` | int or list | riders left waiting by a train's bags |
| `gaveUp` | int or list | riders the game expired from a platform |

Reading the left-behind counts. `noRoom` and `noBerth` are riders who still rode and paid at a lower class: the money not made is the difference in fare percent (a `noRoom` rider: the first-class percent less 100; a `noBerth` rider: the sleeper percent less 100 if they rode coach, or less the first-class percent if they rode a parlor car) times the fare band. `heldForBags` are riders who waited for a later train and may still ride. `gaveUp` is the money lost. `heldForBags` is a level the boarding repeats every step; the day counts the most riders seen held for one station and destination, once, and an increase is credited to the train that saw it (a train that sat for fifty steps with ten held counts ten). Its band is the trip from this station to the destination. `gaveUp` counts only riders the game expires from a platform that is not their own (waiting more than four game hours for a train); riders who start at a station and wait there are not expired by the game, and riders expiring aboard a car are not counted. A zero in `gaveUp` is not "nobody gave up".

## first-class (kept for older readers): keys

The same station rows as above. `settings` has only the first six keys. `cars` has, for each passenger car (baggage cars left out): `id`, `definition`,
`firstClass` and `sleeper` (true when the car is a parlor or a sleeper), and for a parlor or sleeper car `firstClassSeatsUsed` or `sleeperBerthsUsed` and
`noRoomSinceArrival`, as above.

| Key | Type | Meaning |
|---|---|---|
| `firstClass` | bool | true for a parlor car |
| `sleeper` | bool | true for a sleeper car |
| `firstClassIds` | list of strings | the definition ids of the first-class twins |
| `sleeperIds` | list of strings | the definition ids of the sleeper twins |
