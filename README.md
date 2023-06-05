# Plugin-DD2
Darkest Dungeon II mod plugin
Force enable editor_prefs.txt (bepinEx option)

# Feature list
0. You can make/use incremental mods like DD1, load them by priority to resolve confilct, and put them in %Game%/mods folder.
1. Unlock all skills at the beginning (editor option)
2. Regenerate seed when creating new heros (editor option)
3. Can replace paths (unlocked) in character sheet when driving (keyboard space)
4. Can replace heros (reserved) in character sheet when driving (keyboard alt+space)
5. Can right click torch to decrease 15 light value (either combat or coach)
6. Press F3 to quick save. Hold Alt and click continue journey to read it.
7. Can force lock/remove quirks in hospital (alt=remove, shift=lock)
8. Can lock more quirks in hospital (price doubles each time)
9. Can choose more than one actor per class in roster (editor option)
10. Can start with less than 4 heros, won't fill at inns (editor option)
11. Change the bias of region required to fight bosses and ignored hunting required.

# Incremental mod support
You should not make mods by replacing the official CSV text, but only by writing the parts that need to be changed in your mods.
The advantage is that your mods will be smaller and have a high probability that they will not be affected by official updates.
In addition, you can load multiple mods at the same time, and the ones loaded first will block the ones later when conflict occurs.

# How to compile
1. install DD2 game (e.g. steam version) and copy absolute game paths.
2. install bepinEX to your game folder (https://github.com/BepInEx/BepInEx/releases).
3. open DD2_Plugin_Binarizer.csproj with any text editor, replace all absolute paths to DD2 folder with your absolute paths.
4. compile and run game.
