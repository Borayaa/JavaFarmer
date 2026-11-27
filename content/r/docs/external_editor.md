# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code or RStudio.

With JavaFarmer every code file is saved with its real extension: the code window `main.R` is the file `main.R`, a Java window `Utils.java` is `Utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.R`, `bot.py` or `Utils.java` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.R` for R. It defines all the game functions (`harvest()`, `move(direction)` and so on) and constants (`Entities$Carrot`, `North`, ...) as R code with documentation comments, so you can look up names and parameters.
Write `source("farm.R")` at the top of a file when you run it outside of the game (with Rscript or in RStudio). In the game you don't need it. The game functions don't do anything outside of the game, but you can check that your code parses and test calculations that don't need the farm.
Don't expect everything to behave like normal R: the code windows use a part of R (see the pages under Programming).

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced. RStudio works too.

You can install VS Code [here](https://code.visualstudio.com/download).

After downloading it, you can install an R extension in VS Code for syntax highlighting.

Once you have that, open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. 

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
