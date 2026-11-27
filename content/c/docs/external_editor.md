# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.c` is the file `main.c`, a Java window `Utils.java` is `Utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.c`, `bot.py` or `Utils.java` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.h` for C. It is the header that every C window includes with `#include "farm.h"`. It declares all the game functions and constants with their C types, and the lists, sets and maps described on the Programming pages.
`farm.h` is real C, so a normal C editor understands it: IntelliSense knows `harvest()`, `move(Direction d)` and `list_add` as soon as `farm.h` is in the same folder. Keep in mind that the code windows accept only a part of C (see the pages under Programming).

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install a C extension in VS Code (for example "C/C++") for syntax highlighting and IntelliSense.

Once you have that, open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. Make sure you open the whole folder, not just the individual files, otherwise your editor won't find `farm.h`.

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
