# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.js` is the file `main.js`, a Java window `utils.java` is `utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.js`, `bot.py` or `utils.java` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.js` for JavaScript. It lists all the game functions and constants with JSDoc types, so you can look up names and parameter types.
If you add the line `import "./farm.js";` at the top of a file, your editor knows the game's functions like `harvest()`. The line does nothing in the game, so you can leave it in.
Don't expect everything to work like in a browser or in node: the code windows use a subset of JavaScript (see the pages under Programming).

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

VS Code understands JavaScript out of the box, you don't need an extension.

Open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. Make sure you open the whole folder, not just the individual files, otherwise `farm.js` won't be found.

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!