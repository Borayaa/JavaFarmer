# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.zig` is the file `main.zig`, a Java window `Utils.java` is `Utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.zig`, `bot.py` or `utils.zig` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.zig` for Zig. It is written every time the game saves your code files, as long as the save has at least one `.zig` window. It is a small Zig file with all the game functions (`harvest`, `canHarvest`, `doAFlip` and the others), the enums `Entity`, `Item`, `Ground`, `Unlock`, `Hat`, `Leaderboard` and `Direction`, the types `Position`, `Companion` and `Drone`, and `allocator`. This is what `@import("farm")` at the top of your files refers to, so you can look up every name and parameter type there.
`farm.zig` is never loaded as a code window, and the game never runs it: in the game, `farm.harvest()` is the real drone command. The functions in the file only exist so that editors and the Zig compiler know their names and types. They don't do anything.
Don't expect everything of Zig to work: the code windows use a part of Zig (see the pages under Programming). Everything the game accepts is real Zig, though, so the real Zig compiler accepts your files too.

## Checking your code with the Zig compiler
If you have Zig installed, you can let the real compiler check your code. Open a terminal in the folder of your save. Your files import `farm` as a module, so you have to tell the compiler where that module is: `zig build-exe --dep farm -Mroot=main.zig -Mfarm=farm.zig` builds `main.zig` with `farm.zig` as the module `farm`.
If you only want to check the code without creating a program, add `-fno-emit-bin`: `zig build-exe -fno-emit-bin --dep farm -Mroot=main.zig -Mfarm=farm.zig` reports the errors and creates nothing.
Other code windows that `main.zig` uses with `@import("utils.zig")` are found automatically, because they are files in the same folder.
The real compiler only checks the functions your program actually uses, while the game checks every function in the file, so the game can find a mistake that the compiler didn't report.
The game functions in `farm.zig` don't do anything outside the game, so only run your programs in the game.

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install the Zig Language extension in VS Code for syntax highlighting and error checking. It uses ZLS, the Zig language server.
ZLS gives code completion for the game functions only if it knows where the module `farm` is. It learns that from a `build.zig` file, which needs Zig installed. Put `build.zig` into the folder of your save: the game never loads a file named `build.zig` (or `build.rs`) as a code window, so you can't give a Zig window the name `build`. In it, add an executable whose root module is the `main.zig` of your save and give that module the import `farm` with the root file `farm.zig` of your save. If you only want syntax highlighting, you don't need any of that.

Open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. Make sure you open the whole folder, not just the individual files, otherwise `farm.zig` and your other code windows won't be found.

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
