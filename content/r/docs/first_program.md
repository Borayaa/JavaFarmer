# First Program
## About this mod
You are playing with the JavaFarmer mod. The original game teaches you Python. With this mod, you program your drone in R instead, a programming language that is used all over the world for statistics and data analysis. 
Everything else in the game stays the same.

## Text editor
All programming is done in code windows. Each code window corresponds to a text file containing code. 
You can rename the file by clicking on it's name at the top of the window. R files end with `.R`, like `main.R`.

The code can be edited like in any text editor as long as it's not running.
You can execute the program directly by pressing the green play button in the code window.
![](PlayButton50)

You can create more code files using the "+" button in the upper right corner of the screen.
You can dock a window to another window by dragging it onto it.

You will notice that once you start typing, a simple code completion window will pop up.
Press Tab to insert the code completion.
Use the arrow keys to navigate through the completion options.

Don't worry if this is your first time programming. The language is unlocked step by step, so you won't be overwhelmed by all the things you can do. 
What you write here is real R, so learning it is not wasted at all.

If you already know R, it's not a problem either, you'll just be able to skip the early game quickly to get to the more interesting stuff.

## How a program runs
An R file is a script. A new code window is empty, and you don't need a `main` function or anything else around your code.
When you press the play button, the drone runs the statements of the file from the top to the bottom, one after the other.

## Drone commands
Currently, there are two drone commands available.

`harvest()`

and 

`do_a_flip()`

These are function calls. You can think of a function as a command that can be executed. You execute it using the `()` parentheses.

Try typing these statements in the code window and pressing the execute button.

You can think of your code as a sequence of statements. Put each statement on its own line to run several statements in a row:

`harvest()
do_a_flip()
harvest()`

R needs no semicolon at the end of a line: a line break ends the statement. If you want to write two statements on the same line, separate them with a semicolon `;`:

`harvest(); do_a_flip()`

## Unlocks
Collecting grass will give you hay. Hay can be used to unlock loops in the unlock menu. Open the unlock menu with the button in the top right corner.

