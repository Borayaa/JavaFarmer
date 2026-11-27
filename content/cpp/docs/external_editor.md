# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.cpp` is the file `main.cpp`, a Java window `utils.java` is `utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.cpp`, `bot.py` or `utils.java` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.hpp` for C++. It is the header your code includes with `#include "farm.hpp"`. It declares all the game functions and constants with their C++ types, so a C++ editor can show you names and parameter types, and a real compiler (`g++ -std=c++23`) can check your code.
Don't expect everything of C++ to work: the code windows accept a small part of C++ (see the pages under Programming). Classes, pointers, templates and most of the standard library are not supported.

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install a C/C++ extension in VS Code. Set its C++ standard to C++23 so that `std::println` is known.

Once you have that, open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. Make sure you open the whole folder, not just the individual files, otherwise the editor won't find `farm.hpp`.

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
