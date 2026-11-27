# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.java` is the file `main.java`, a Rust window `utils.rs` is `utils.rs`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.java`, `bot.py` or `utils.rs` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `JavaFarmerApi.java` for Java. It lists all the game methods and constants with their Java types, so you can look up names and parameter types.
Don't expect full IntelliSense: the code windows use a small Java dialect (see the pages under Programming), and a normal Java editor doesn't know the game's methods like `harvest()`.

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install a Java extension in VS Code for syntax highlighting.

Once you have that, open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. 

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
