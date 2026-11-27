# First Program
## About this mod
You are playing with the JavaFarmer mod. The original game teaches you Python. With this mod, you program your drone in Zig instead, a modern language that is known for being simple and explicit: nothing happens in Zig that you can't see in the code. 
Everything else in the game stays the same. Behind the scenes the game translates your Zig into its own language, so all costs and unlocks work exactly as in the original.

## Text editor
All programming is done in code windows. Each code window corresponds to a text file containing code. 
You can rename the file by clicking on it's name at the top of the window. A Zig file ends in `.zig`, like `main.zig`.

The code can be edited like in any text editor as long as it's not running.
You can execute the program directly by pressing the green play button in the code window.
![](PlayButton50)

You can create more code files using the "+" button in the upper right corner of the screen.
You can dock a window to another window by dragging it onto it.

You will notice that once you start typing, a simple code completion window will pop up.
Press Tab to insert the code completion.
Use the arrow keys to navigate through the completion options.

Don't worry if this is your first time programming. The language is unlocked step by step, so you won't be overwhelmed by all the things you can do. 
What you learn here is real Zig, so learning it is not wasted at all.

If you already know Zig, it's not a problem either, you'll just be able to skip the early game quickly to get to the more interesting stuff.

## The main function
Every Zig program needs a place to start. This is the `main` function. A new code window already contains it:

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    
}`

When you press the play button, the drone runs the code between the curly braces `{` and `}` from top to bottom. 
You don't need to understand all of this yet, just write your code between the braces. `fn` starts a function, `main` is its name, the empty `()` means it takes no inputs and `void` means that it does not give back a value. `pub` makes it visible from outside the file, which Zig requires for `main`. More on that on the Functions page later.
The two lines at the top make the commands available in your file: `farm` holds all the drone commands of the game and `std` is Zig's standard library, which you will need later, for example to print things. Keep them at the top of every code file.

Writing `pub fn main() void` and the two `@import` lines does not cost anything and does not need to be unlocked.

## Drone commands
Currently, there are two drone commands available.

`_ = farm.harvest();`

and 

`farm.doAFlip();`

These are function calls. You can think of a function as a command that can be executed. You execute it using the `()` parentheses. The `farm.` in front says that the command comes from `farm`, the game's commands you imported at the top.

In Zig, every statement ends with a semicolon `;`. It tells Zig where one command stops and the next one begins. If you forget it, the editor will tell you `expected ';' after statement`.

What about the `_ =` in front of `farm.harvest()`? Some commands give back a value. `farm.harvest()` gives back `true` if it removed something and `false` if there was nothing to harvest. Zig doesn't let you silently ignore a value like that, because ignoring it by accident is a common source of bugs. You have to either use it or throw it away on purpose, and `_ =` is how you throw it away. If you forget it, the editor will tell you `value of type 'bool' ignored`.
`farm.doAFlip()` gives back nothing (its result type is `void`), so it doesn't need `_ =`.

Try typing one of these statements between the braces of `main` and pressing the execute button.

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    _ = farm.harvest();
}`

You can think of your code as a sequence of statements. You can run multiple statements in a row like this:

`const std = @import("std");
const farm = @import("farm");

pub fn main() void {
    _ = farm.harvest();
    farm.doAFlip();
    _ = farm.harvest();
}`

Zig doesn't care about line breaks or indentation, but you should still put each statement on its own line and indent the code inside the braces. It makes your code much easier to read.

## Unlocks
Collecting grass will give you hay. Hay can be used to unlock loops in the unlock menu. Open the unlock menu with the button in the top right corner.
