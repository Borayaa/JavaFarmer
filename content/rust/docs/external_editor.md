# External Editor
The in-game text editor is usually sufficient to play this game, but of course it cannot compete with more sophisticated text editors like Visual Studio Code.

With JavaFarmer every code file is saved with its real extension: the code window `main.rs` is the file `main.rs`, a Java window `Utils.java` is `Utils.java`, a Python window `bot.py` is `bot.py`. Your editor recognises the language by itself.
You can find the files in the [save folder](persistent_data_path/Saves).

The extension also decides the language of a file in the game. Rename a code window (click its name) to `main.rs`, `bot.py` or `utils.rs` to change its language. A new window gets the extension of the default language, which you choose in the Mods menu.

Each save also contains a `__builtins__.py` file with the game's Python definitions. Next to it, JavaFarmer writes an API file for every language used in the save: `farm.rs` for Rust. It is a small Rust library crate called `farm` with all the game functions, the enums `Entity`, `Item`, `Ground`, `Unlock`, `Hat`, `Leaderboard` and `Direction`, and the types `Position`, `Companion` and `Drone`. This is what `use farm::*;` at the top of your files refers to, so you can look up every name and parameter type there.
Don't expect everything of Rust to work: the code windows use a part of Rust (see the pages under Programming). Everything the game accepts is real Rust, though, so the real Rust compiler accepts your files too.

## Checking your code with the Rust compiler
If you have Rust installed, you can let the real compiler check your code. Open a terminal in the save folder and build the API file once with `rustc --edition 2024 --crate-type lib --crate-name farm farm.rs`. This creates `libfarm.rlib`.
Then check a code file with `rustc --edition 2024 --emit=metadata --extern farm=libfarm.rlib -L . main.rs`. Other code windows that `main.rs` uses with `mod utils;` are found automatically, because they are files in the same folder.
The game functions in `farm.rs` don't do anything outside the game, so only run your programs in the game.

To see external changes in-game without having to reload the save, you must enable the "File Watcher" option. If you create or delete files externally, you will still need to reload the save to see them.

## Using VS Code
Visual Studio Code is a good code editor to use with The Farmer Was Replaced.

You can install it [here](https://code.visualstudio.com/download).

After downloading it, you can install the rust-analyzer extension in VS Code for syntax highlighting and error checking.
rust-analyzer gives full code completion for the game functions only inside a Cargo project. For that you need Rust with Cargo and a file Cargo.toml next to your code files. It needs a package section (a name, a version and `edition = "2024"`), a lib section with `name = "farm"` and `path = "farm.rs"`, and a bin section with `name = "main"` and `path = "main.rs"`. If you only want syntax highlighting, you don't need any of that.

Open the [folder](persistent_data_path/Saves) that holds your code files in VS Code. Make sure you open the whole folder, not just the individual files, otherwise `farm.rs` and your other code windows won't be found.

In the game, make sure you have the "File Watcher" option turned on. Now, every time you save in VS Code, the changes will automatically show up in the game.

That's it! Now you can write your code in a professional code editor!
