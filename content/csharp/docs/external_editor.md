# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

The game saves all code files as `.py` files. With JavaFarmer the files keep the `.py` extension, but their content is C#. So tell your editor to treat them as C# files (in VS Code: click the language name in the bottom right corner and choose "C#").
You can find the files in the [save folder](persistent_data_path/Saves).

Each save also contains a `__builtins__.py` file with the game's Python definitions. JavaFarmer may also write a `Farm.cs` file next to it. It declares all the game methods and constants with their C# types (the class `Farm` with `Harvest()`, `Move(Direction direction)` and so on), so you can look up names and parameter types.
Don't expect full IntelliSense: the code windows use a small part of C# (see the pages under Programming), and an editor only knows the game's methods like `Harvest()` if it reads `Farm.cs`.

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install a C# extension in VS Code for syntax highlighting.

Once you have that, open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. 

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
