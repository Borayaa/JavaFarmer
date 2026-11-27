# First Program
## About this mod
You are playing with the JavaFarmer mod. The original game teaches you Python. With this mod, you program your drone in C# instead, one of the most widely used programming languages in the world. 
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
What you learn here is real C#, so learning it is not wasted at all.

If you already know C#, it's not a problem either, you'll just be able to skip the early game quickly to get to the more interesting stuff.

## How a program starts
In C# the code window itself is the program. When you press the play button, the drone runs the statements in the window from top to bottom. C# calls these statements "top-level statements". You don't need any wrapper around them.

If you have seen C# before, you may know the longer form with a class and a `Main` method. It works too, and the drone then starts at `Main`:

`class Program
{
	static void Main()
	{
		Harvest();
	}
}`

Writing `class Program` and `static void Main()` does not cost anything and does not need to be unlocked. The pages of this guide use the short form.

## Drone commands
Currently, there are two drone commands available.

`Harvest();`

and 

`DoAFlip();`

These are method calls. You can think of a method as a command that can be executed. You execute it using the `()` parentheses.

In C#, every statement ends with a semicolon `;`. It tells C# where one command stops and the next one begins. If you forget it, the editor will tell you `; expected`.

Try typing these statements in the code window and pressing the execute button.

You can think of your code as a sequence of statements. You can run multiple statements in a row like this:

`Harvest();
DoAFlip();
Harvest();`

C# doesn't care about line breaks or indentation, but you should still put each statement on its own line. It makes your code much easier to read.

## Unlocks
Collecting grass will give you hay. Hay can be used to unlock loops in the unlock menu. Open the unlock menu with the button in the top right corner.
