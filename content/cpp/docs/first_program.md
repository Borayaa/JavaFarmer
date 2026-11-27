# First Program
## About this mod
You are playing with the JavaFarmer mod. The original game teaches you Python. With this mod, you program your drone in C++ instead, one of the most widely used programming languages in the world. 
Everything else in the game stays the same.

## Text editor
All programming is done in code windows. Each code window corresponds to a text file containing code. 
You can rename the file by clicking on it's name at the top of the window.

The code can be edited like in any text editor as long as it's not running.
You can execute the program directly by pressing the green play button in the code window.
![](PlayButton50)

You can create more code files using the "+" button in the upper right corner of the screen.
You can dock a window to another window by dragging it onto it.

You will notice that once you start typing, a simple code completion window will pop up.
Press Tab to insert the code completion.
Use the arrow keys to navigate through the completion options.

Don't worry if this is your first time programming. The language is unlocked step by step, so you won't be overwhelmed by all the things you can do. 
What you write here is real C++, so learning it is not wasted at all.

If you already know C++, it's not a problem either, you'll just be able to skip the early game quickly to get to the more interesting stuff.

## The main function
Every C++ program needs a place to start. This is the `main` function. A new code window already contains it:

`#include "farm.hpp"

int main() {
    
}`

The first line, `#include "farm.hpp"`, gives your program the drone commands of the game. Keep it at the top of every code window.
When you press the play button, the drone runs the code between the curly braces `{` and `}` of `main` from top to bottom. 
You don't need to understand the words `int` and `main` yet, just write your code between the braces. (`main` is the name of the function and `int` is the type of value it gives back. More on that on the Functions page later.)

Writing `#include` and `int main()` does not cost anything and does not need to be unlocked.

## Drone commands
Currently, there are two drone commands available.

`harvest();`

and 

`do_a_flip();`

These are function calls. You can think of a function as a command that can be executed. You execute it using the `()` parentheses.

In C++, every statement ends with a semicolon `;`. It tells the compiler where one command stops and the next one begins. If you forget it, the editor will tell you `expected ';'`.

Try typing one of these statements between the braces of `main` and pressing the execute button.

`#include "farm.hpp"

int main() {
    harvest();
}`

You can think of your code as a sequence of statements. You can run multiple statements in a row like this:

`#include "farm.hpp"

int main() {
    harvest();
    do_a_flip();
    harvest();
}`

C++ doesn't care about line breaks or indentation, but you should still put each statement on its own line and indent the code inside the braces. It makes your code much easier to read.

From now on, most examples in these pages show only the statements that go inside `main`. Put them between the braces of `main` to run them. Examples that define their own functions show them outside of `main`.

## Unlocks
Collecting grass will give you hay. Hay can be used to unlock loops in the unlock menu. Open the unlock menu with the button in the top right corner.
