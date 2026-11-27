# First Program
## About this mod
You are playing with the JavaFarmer mod. The original game teaches you Python. With this mod, you program your drone in Java instead, one of the most widely used programming languages in the world. 
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
What you learn here is real Java, so learning it is not wasted at all.

If you already know Java, it's not a problem either, you'll just be able to skip the early game quickly to get to the more interesting stuff.

## The main method
Every Java program needs a place to start. In JavaFarmer this is the `main` method. A new code window already contains it:

`void main() {
    
}`

When you press the play button, the drone runs the code between the curly braces `{` and `}` from top to bottom. 
You don't need to understand the words `void` and `main` yet, just write your code between the braces. (`main` is the name of the method and `void` means that it does not give back a value. More on that on the Functions page later.)

Writing `void main()` does not cost anything and does not need to be unlocked.

## Drone commands
Currently, there are two drone commands available.

`harvest();`

and 

`doAFlip();`

These are method calls. You can think of a method as a command that can be executed. You execute it using the `()` parentheses.

In Java, every statement ends with a semicolon `;`. It tells Java where one command stops and the next one begins. If you forget it, the editor will tell you `';' expected`.

Try typing one of these statements between the braces of `main` and pressing the execute button.

`void main() {
    harvest();
}`

You can think of your code as a sequence of statements. You can run multiple statements in a row like this:

`void main() {
    harvest();
    doAFlip();
    harvest();
}`

Java doesn't care about line breaks or indentation, but you should still put each statement on its own line and indent the code inside the braces. It makes your code much easier to read.

## Unlocks
Collecting grass will give you hay. Hay can be used to unlock loops in the unlock menu. Open the unlock menu with the button in the top right corner.
