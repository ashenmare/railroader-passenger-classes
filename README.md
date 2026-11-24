# Passenger Classes

**Nexus Mods** https://www.nexusmods.com/railroader/mods/2412

This is the source of Passenger Classes 0.2.0. The ready-made zip is on the Nexus page.

Built for Railroader 0.6.3.

## Install
Unzip into your Railroader Mods folder, turn on "Allow DLL Mods" (Preferences > Features > Experimental), restart, then tick Passenger Classes in
Manage Mods. Without "Allow DLL Mods" the mod does not load and the first-class and sleeper cars come in as plain cars with no classes.

Every passenger car gets a class, from coach to parlor (first class), sleeper, lounge or baggage, and observation cars get the whole-train fare
bonus. It works for the stock cars, the first-class and sleeper twins this mod includes, and car packs by other authors. Coaches and
baggage cars of any pack brake like the stock coaches. Every player in multiplayer needs it, because it adds car definitions.

## Replaces first class
Passenger Classes includes everything First Class did, under the same car ids, so cars in your saves keep working. The two mods cannot
run together: with First Class ticked, the game leaves Passenger Classes off and says so in Manage Mods. To switch:
1. Untick First Class and tick Passenger Classes. Do not delete First Class's folder yet.
2. Load your save. If First Class is still installed you see one prompt, "This Save Uses Different Mods". Choose "Keep My Current Mods".
   ("Use This Save's Mods" is safe here too, but puts First Class back.)
3. If you already deleted First Class's folder you see two prompts: "Mod Not Available", where you choose "Load Without It", then "This Save
   Uses Different Mods", where you must choose "Keep My Current Mods". "Use This Save's Mods" there turns Passenger Classes off, and the
   first-class and sleeper cars are dropped when the save loads and gone at the next save.
4. Save once. From then on the save knows Passenger Classes and the prompts stop. Now you can delete First Class's folder.
Your First Class settings are copied over the first time Passenger Classes loads, if it has none of its own yet.
These prompts are described from the game's code and have not been played yet.
A parlor boards only the first-class share of a crowd (15%), so one that is new to you can run part empty. PPL and PPO cars from car packs
are parlors by default. To make one a coach, turn on "Change car classes" in the settings and right-click it.

HOW A CAR GETS ITS CLASS (the first that says something wins, one field at a time)
1. Your own choice for the car's model: turn on "Change car classes (right-click menu)" in the settings to set classes, and turn it off when you're done (off, the class entries are gone, but classes you set keep working), then right-click a car. The menu's first line says what the car is now ("Parlor (first class), observation", with ", your choice" added when you set it). Then "Make it a coach" (or parlor, sleeper, lounge, baggage car), "Turn observation on" or "off",
   "Take its class off" to take a model's class away, and "Back to its default" to undo your choice. It applies to every car of that model and is
   saved in choices.json in the mod's folder. In multiplayer the host's choices decide, and only the host sees these entries.
2. The car pack's own PassengerClasses.json (see FOR CAR MODELLERS), unless you switch "Read class files from car packs" off.
3. The built-in table: the stock coaches are coaches. The First Class twins are parlors and the sleeper twin a sleeper. Type PBO, PO and
   PPO are observation cars. PPL and PPO are parlors. Types starting CB, and exactly BE, BM or BH, are baggage cars.
4. A car with a passenger slot is a coach. Anything else has no class.

## What each class does
- Coach: nothing special. The game's own boarding and fare.
- Parlor (first class): 15% of each crowd (a setting) wants first class. They board a parlor car before anyone and pay 200% of the fare
  (a setting). A parlor takes only its own riders: it runs part empty by design, and it is not a bigger coach. The seats are the car's
  own. The twins have 70% of the stock car's seats and cost 120% (settings change the price live, the seats are in the car).
- Sleeper: 6% of each crowd (a setting) on trips of at least 30 miles (a setting) wants a sleeper. A sleeper car takes only them,
  and they pay 350% (a setting). A sleeper rider with no berth takes a parlor seat, else a coach. The twin has 35 berths.
- Observation: an observation car at either end of the train, with its platform end free, raises every fare in the train by 20% (a
  setting, the game's own figure). The game only did this for the exact type PBO. The platform end is the end that is not the car's F
  end, the game's own rule. A pack file can say a model has it the other way.
- Lounge: a lounge car (type DLC, and the tavern-observation type PO, by default) in a train gives the riders who change trains off it more
  patience: the game drops a rider who waits to transfer more than 4 hours after boarding, and these wait 1 hour longer (a setting). It moves
  the time the game reads, so it adds to Patient Passengers' and Station Upgrades' own extra time. Off by default ("Lounge cars" in the settings).
- Baggage: see below. Off by default ("Riders bring baggage" in the settings).

## Steam heat and cold-weather fares
"Steam heat" in the settings (on by default) switches the whole feature. Off, there is no heat line on a car and no heat effect on fares, with or without
Realistic Steam and Fuel Use.
With Realistic Steam and Fuel Use and its passenger heat on, the engine supplies the steam and that mod charges the coal and water, and the weather moves
what a car earns. In the cold an unheated car earns less: a coach 25% less, a parlor 30% less, a sleeper 40% less. A heated car earns a little more on a
bitter night: a coach 4%, a parlor 6%, a sleeper 10%. The size grows with the cold, from nothing at 60 F and above to the full figure at 20 F and below,
in a straight line between. When that mod reports the car's inside temperature, the effect slides with it instead, with a buffer: 70 F inside and warmer gives the full
bonus, the bonus slides down in a straight line to nothing at 62 F, nothing is cut down to there, and below 62 F the cut slides to its full figure at 50 F and colder
(a coach at 66 F is +2%, at 56 F -12.5%, at 50 F -25%, with the bitter-night outside scale and the setting on top). Railroads held cars at about 70 F and a car a few degrees
under was no complaint (the 62 F and 50 F corners are a judgement). The fare goes by the AVERAGE over each rider's ride, boarding to getting off, not by the temperature at the stop:
a car that warms from 45 F to 70 F on the way pays by how long it was cold, and a rider who boarded a cold car that stayed warm after the first stretch is not cut for it.
Passenger Classes writes the car's comfort figure down when riders board (the game does not say when a rider got on the car) and reads it again as they get off. A rider
who boarded before the game was loaded has no such note and is worked at the temperature at the stop. The Fare line on a car's window shows the temperature now. A car with no reading keeps the heated or unheated figures above. Baggage cars pay no fare, so nothing changes for them. The percent is of the car's whole fare, the game's fare plus the parlor
or sleeper extra, and it is worked on each rider's fare and paid in the game's own payment for the stop, once, with the extra. The figures are a design
choice, not a measured rule. Heat was part of the ticket and a car left cold was the failure riders complained of. At 14 F a sleeper that is not heated
earns 40% less and a heated one 10% more. "Cold-weather fares" in the settings scales both effects (100 as set, 0 turns the fare effect off).
Without Realistic Steam and Fuel Use, every passenger car is simply heated in the cold months, November to March by the game's calendar (a design choice),
at no cost, with no engine or steam needed, and no fare is cut. The car's row in the feed says "Heated" then and no fare moves (there is no air reading
without that mod, so there is no bonus either). With that mod installed but its passenger heat off, or its answer not readable, the same applies: the
cars are heated for nothing and fares are not cut, because a missing provider is never taken out of fares. If you turn that mod's own passenger heat to
Auto, it follows this Steam heat setting: with Steam heat off here, that mod does not heat or charge for steam either (it notices within a minute). Its On and Off are your word and are not changed by this setting.
A car's row in the feed says "cold car, fares -25%" or "heated, +4%" while the real heat's effect applies. Only the host's computer works fares, so only
it needs the heat feed. Only the game can show how it looks in the car lists that read the feed.

## Fare line and payment breakdown
Two readouts, each its own setting and on by default. Neither changes any fare or payment.
"Fare line in the car window": the Passenger tab of a passenger car's window starts with a Fare row: the car's class and its extra over the coach fare, the
heat effect when one applies, and the observation bonus when its train has one. For example "Sleeper +250%, heated +10%". The other words are "Coach", "First
class +100%", "cold car -25%" and "Heated" (heated at no cost, without Realistic Steam and Fuel Use's heat). With an inside temperature it reads "60 F inside +2%" or "42 F inside -24%". A car the mod gives no class has no row. Only the
host sees it: the fare settings, the classes and the heat are the host's, so a joined client has the game's own window.
"Fare breakdown when a stop pays": a second console line follows the game's own, for example "Sylva: $120 (coach $34, first $52, sleeper $25, heat +$9)".
First and sleeper include their extra, heat is the cold-weather change (a minus for a cold car), bags is the excess baggage fee, and the observation bonus is
inside the class figures. The whole dollars add up to what the game paid. Nothing is said when the parts cannot account for the payment. Each line is also written to Player.log, as the game's own is.
Not seen in the game yet: how either looks, and a second player's view.

## The station window
On the host, the Passengers tab of the station agent window (click the depot) reads Count, First, Sleeper, Destination, Longest Wait. First and Sleeper
are the riders who want first class and a sleeper, parts of Count, from the same count the feed's station rows carry. Longest Wait is the game's own
(riders who changed trains here only, a stop of its own riders reads N/A). With two or more destinations a bold Total row closes the list.
With both shares at 0, on a client (the count is the host's), or on a game version whose window differs from 0.6.3's, you get the game's own table.
Station Upgrades' section stays under it. Not seen in the game yet: whether the columns fit the window.

## Seats when you change a class
Right-clicking a model into a class asks first ("Make every ... a sleeper? 42 berths each. 6 on your railroad.") because it changes every car of
that model. A coach made a parlor keeps 70% of its seats (a setting, the same as the First Class twins), and one made a sleeper 50% of them as
berths (a setting): an 84-seat coach made a sleeper has 42 berths. The change is made in the car's definition, so the boarding, the hover text
(30/42), the car inspector, Tag Loads and the Industry Inspector all show it. Cars built as a parlor or a sleeper (the twins, a pack's parlor
cars) keep their own seats, and a pack whose file gives `seats` for a car in its class has those. "default" puts the seats back. A class chosen in an earlier build re-seats its model the next time a world loads (one line in the log says so). Riders already
aboard stay aboard. In multiplayer every player needs the same choices, since the seats are in each computer's own definitions.

## Refit
Turn a coach you own into its first-class or sleeper twin, or a twin back into a coach, without selling it. Stand the car on a repair
track, right-click it and pick "Refit as first class", "Refit as sleeper" (the Palace Car) or "Refit back to coach". The game asks first,
with the price. The car keeps its number, its place in the train, its paint, its condition and its miles. Only the host has these entries.
- The car must be yours, empty, and standing still with the train it is coupled to. A car on an open Used Equipment lease can't be refitted. Once the lease is paid off it can (with Used Equipment 0.6.3 or later).
- The price is what the first-class or sleeper car costs over the coach, plus labor: 10% of the first-class or sleeper car's price, at
  least $250 (two settings). Back to a coach costs the labor only, and nothing is paid back. At the default prices: open platform coach
  $1,424, Osgood-Bradley coach $2,064, Palace Car $2,192 (as a sleeper $3,699), Palace Observation $2,512.
- It is one Equipment line in the ledger.
- A camera following the car lets go of it.
- Livery Loader decals placed on the coach model don't follow the car to its twin.
- If the game can't build the new car, the old one is put back and nothing is charged. The log says why.
- On a game version where a refit can't work, these entries are missing and the log says so once. The rest of the mod is unaffected.
- Played alone, not yet with a second player. In multiplayer every player should see the car change and stay coupled.

## Brakes
The game gives a car 0.9 of its empty weight as nominal braking only when its type code starts with PB or CB, and 0.7 for every other
type, so most car packs brake a fifth weaker than the stock coaches. Passenger Classes gives every car with a passenger slot, and every
car with a class, the coach figure, in the car and in the Auto Engineer's stopping plan. Locomotives and tenders are untouched. A class
you choose from the menu applies to the cars already on your track.

## The stock baggage car
The C-44-C-425B Baggage Car is listed at $0 in the game, so it cannot be bought. It costs $3,500 here, a little under the cheapest coach
because it has no seats. Its type already brakes like a coach. A price the game or another mod gives it is left alone. With baggage on it
holds 40,000 lb of bags (the game's own slot for it is a mail load nothing uses).

BAGGAGE (off by default)
The shop lists the stock baggage car's capacity as 1,000: that is the game's unused mail slot. This mod gives it 40,000 lb of baggage room.
Riders bring checked bags by the fare they pay (the game's $1 to $8 distance fare) and the class of the car they sit in:
    fare        coach     first class   sleeper
    $1 and $2   none      none          none
    $3 to $5    25 lb     50 lb         100 lb
    $6 to $8    50 lb     100 lb        200 lb
1 rider in 5 (a setting) brings double: on average each rider also brings 20% of their allowance as excess, which pays $2 per 100 lb (a
setting) with the fare. Every car carries its own riders' normal bags in its racks: coach 30 lb a seat, parlor 60 lb a seat, sleeper 120 lb a berth
(three settings, a lounge car uses the coach figure). Each passenger car also has a small luggage area (500 lb, a setting) for its own riders' bags
past the racks: the normal ones first, then the excess. The area is the car's own: it is not shared along the train. What is left past a car's
racks and area needs baggage room somewhere in the train: a baggage car, a combine's baggage end (the stock-style 12,000 lb), any car whose pack gives
it a baggage slot of pounds (a baggage-express car's 40,000 lb), or the baggage figure in a pack's file. A rider whose bags don't fit waits on the
platform for the next train, where they were in the crowd, with the age they had. The excess bags that don't fit their car's area ride in whatever
room is left after the normal bags, and pay the fee in that proportion. The excess that fits the area always rides and pays. Those riders are not
turned away for it.
So an all-coach local needs no baggage car, and neither does a first-class car on a $3 to $5 trip: a 56-seat parlor with 56 riders at 50 lb has 2,800 lb of bags, and its racks
hold 56 x 60 = 3,360. Long runs and sleepers still want a baggage car or a combine. Worked example, cars full, defaults:
- Short run ($3 to $5): an 80-seat coach has 80 x 25 = 2,000 lb of bags in racks of 2,400, and its 400 lb of excess fits the 500 lb area:
  it needs nothing from a baggage car. A 56-seat parlor has 2,800 lb in racks of 3,360, and 560 lb of excess: the area takes 500, 60 lb is
  left for a baggage room.
- Long run ($6 to $8): the coach has 4,000 lb against racks of 2,400 and an area of 500, so 1,100 lb of normal bags and 800 lb of excess need
  baggage room. A parlor of 56 (5,600 lb, racks 3,360): 1,740 lb of normal bags and 1,120 of excess. A sleeper of 22 berths (4,400 lb, racks
  2,640): 1,260 lb of normal bags and 880 of excess. One parlor, one sleeper and three coaches need 6,300 lb of normal bags and 4,400 of excess
  in a baggage room, 10,700 lb: one 12,000 lb combine is enough. With none, a long-run coach boards 58 riders (2,900 lb at 50 lb each).
The bags are counted from the riders aboard each time a car is served, as averages. Nothing is saved, and the game's own dropping of expired
riders is counted from the next service. Bags follow the class of the car a rider is in, not the class they wanted.

## For car modellers
Put a file named PassengerClasses.json beside your pack's Definitions.json. Never put anything for this mod inside Definitions.json: a
component the game does not know rejects the whole pack for players without the mod.

  { "format": 1,
    "cars": {
      "yourpack/ppo70": { "class": "parlor", "observation": true },
      "yourpack/be60":  { "class": "baggage", "baggage": 600 }
    } }

class: coach, parlor, sleeper, lounge or baggage. observation: true or false. platformAtOtherEnd: true if the platform is at the car's
F end. seats: the seats the car has as a parlor or a sleeper (wins over the share a coach keeps when the player converts it). baggage: pounds of bags the car's baggage compartment holds, on top of the seat racks, with baggage on this is the car's room (replacing a baggage slot's figure). Also read and kept, not used yet:
farePercent, and from the older CustomBonus component by the same names, bonusAmount, minNumOfCars, maxNumOfCars, startHour, endHour,
minimumOfCartype and maximumOfCartype.
Any "format" number is read the same way. Keys and class words this version does not know are ignored. A file that is not valid JSON is
skipped, with one line in the log. Most packs need no file: the type codes above are recognized, and players can fix a model with the
right-click menu (once "Change car classes" is on). Only cars of the pack the file sits in are read from it.

SETTINGS (Preferences > Mods, all take effect at once except the seats)
Change car classes (right-click menu). Refit labor and its minimum. First-class riders, fare and car price. The share of its seats a coach keeps made a parlor or a sleeper. Sleeper riders, fare, car price and shortest trip. The observation bonus. Reading pack files. The
Steam heat and Cold-weather fares. The Fare line and the fare breakdown switches. Lounge switch and its extra hours. The baggage switch, the bags by fare and class, the share bringing double, the fee, the racks by class and the luggage area. Also "Debug log (for bug reports)" (see DEBUG LOG).

## Debug log
The "Debug log (for bug reports)" setting is one switch for every ashenmare mod that has it: turn it on in any of them and
it is on in all, now, and it stays on after a restart until you turn it off. While it is on, the mods write one file per
game session, Documents\Railroader\Logs\ashenmare-debug-<date>-<time>.log (Linux/Proton: the same folder inside the
game's Proton prefix). To report a bug: turn it on, play until it happens, turn it off, and send the session's files (the newest ones). Each
line is a time, the mod, what it decided and the numbers it used: the class each car model got and why, who boarded and what
they paid, who was turned away and why, the observation bonus, a class change. It holds no chat, no player or company names, and
this mod's lines carry no home folder, account name, machine name, Steam id or address (a path is cut to what follows
Railroader). A file stops at 5 MB and the next one starts. Files older than 7 days go, and the oldest go once they pass 100 MB together. With the setting off nothing is
written and nothing extra runs.

## The feed
Two feeds. "passenger-classes" lists each passenger and baggage car with its class, where the class came from, observation, seats and
seats used, and each station with the first-class and sleeper riders waiting and those who found no room or berth since the last train
pulled in. "first-class" is First Class's own feed with the same keys and its four car columns, so readers written for it, such as the
Industry Inspector's Passengers tab, keep working. FEEDS.md has every key.
The feed's car rows say when the cold-weather fare applies (heatEffect). The "passenger-classes" feed also carries today's and yesterday's figures for Railroad Books: riders boarded by class, fares by part (the game's
fare, the first-class and sleeper extra, baggage fees), what the station was paid, and who was left behind and why, for the whole railroad, each
station and each train (named by its lead engine, the first locomotive from the A end). Only the host has them: nothing is saved, so they start
counting when a game loads and that day is marked partial, and a client's days read as unknown. A rider who wanted first class or a sleeper and rode
a lower class is counted, and still paid a fare: only the difference between the classes is lost. On a game version that lacks something the day
figures need they are off (the feed says "unavailable") and nothing else changes. Riders held back by baggage are counted once a day at the most held at a
station for a destination. They may still ride later. "Gave up" counts only riders the game drops from a platform that is not their own
(waiting more than 4 game hours), so riders who start at a station and wait there are never in it, and a zero there does not mean nobody gave up.
Fares are the unrounded sums. The game rounds each payment up, so the "paid" figure is the ledger's. A day holds at most 128 station rows and 64
train rows. The railroad's totals are always whole, and the day says when the rows ran out.

## Things worth knowing
- Which riders want which class is a count kept in memory for each station and destination. Nothing is saved. A loaded game starts again
  at the shares. A rider who changes trains joins that crowd like anyone else.
- The sleeper distance is measured from the station where a rider boards to their destination, not from where they began their journey: a
  rider changing trains on a short last leg of a long trip has no sleeper.
- The extra a premium rider pays is added to the game's own fare payment at the station, one ledger line a station.
- Boarding and fares run on the host. Braking and the Auto Engineer's plans are the game's physics as far as the code shows. Give every
  player the same settings and the same class choices.
- Only the game can show where an observation car's platform end is on a model. If the bonus does not appear with a car at the tail,
  turn the car around, or put platformAtOtherEnd in its file entry.
- Not tested in multiplayer.

## Removing it
Sell your first-class and sleeper cars first. A car whose definition is missing is dropped when the game loads and is gone after the next
save, with a message from the game. The rest of the save is unaffected. Class choices and settings stay in the mod's folder.

## Changes
0.2.0: First release.
Added classes for every passenger car, set by the player, a pack file or the built-in table.
Added the observation bonus for every observation car.
Added coach brakes for every passenger and baggage car.
Added sleeper riders on long trips only.
Added lounge cars that give transferring riders more patience.
Added riders' baggage with baggage-car room and an excess fee.
Added the stock baggage car to the shop.
Added today and yesterday figures to the feed for Railroad Books.
Added First and Sleeper columns to the station window.
Added refit of a coach to first class or a sleeper, and back.
Added the First Class twins under their old ids.
Added steam heat: unheated cars earn less in the cold, heated cars a little more.
Added a fare that follows the car's inside temperature over each rider's ride.
Added Steam heat and Cold-weather fares settings.
Added a Fare line to a passenger car's window.
Added a console line with the fare by class and heat when a stop pays.

## Building

Needs the .NET SDK (8 or newer) and a Railroader install for the game's dlls. Point the build at the game's
Managed folder (`Railroader_Data/Managed`) with a property or an environment variable.

```
dotnet build PassengerClasses.csproj -c Release -p:ManagedPath="<game folder>/Railroader_Data/Managed"
```

or set `RAILROADER_MANAGED` to that folder once and run `dotnet build PassengerClasses.csproj -c Release`. The build stops with a
message if the folder is missing. NuGet brings in Harmony and the .NET Framework reference assemblies.

The output is `bin/Release/PassengerClasses.dll` and `bin/Release/0Harmony.dll`. To try it, put both and the `Scripts` folder beside `Info.json`
in a folder named `PassengerClasses` under `Documents/Railroader/Mods`, and turn on Allow DLL Mods in Preferences.

## License

MIT, see LICENSE.
The release zip includes Harmony (MIT), see THIRD-PARTY-NOTICES.txt.

Making a Railroader mod? Use our settings menu in yours: https://github.com/ashenmare/railroader-mod-settings
Made with AI assistance (Claude), every release reviewed and audited in game by a human.
